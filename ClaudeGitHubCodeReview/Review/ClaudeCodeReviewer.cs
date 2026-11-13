using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeGitHubCodeReview.Infrastructure;

namespace ClaudeGitHubCodeReview.Review;

public sealed record ClaudeSettings(
    string Executable = "claude",
    string? Model = null,
    string? Effort = null,
    decimal? MaxBudgetUsd = null,
    TimeSpan? Timeout = null);

/// <summary>
/// Runs Claude Code headless (`claude -p`) as the review engine.
///
/// Safety model: the repository being reviewed is untrusted input. The CLI is therefore started with
/// --restricted (no shell/code-running tools, no WebFetch, the cloned repo's .claude settings and hooks
/// are ignored), --strict-mcp-config (no MCP servers), only the read-only tools Read/Grep/Glob, and
/// permission mode dontAsk (anything not pre-approved is denied instead of prompting).
/// The answer is forced into <see cref="ReviewSchema.Json"/> via --json-schema.
/// </summary>
public sealed class ClaudeCodeReviewer(ClaudeSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ReviewResult> ReviewAsync(
        string repositoryDirectory,
        string prompt,
        IEnumerable<string> additionalDirectories,
        Action<string> onProgress,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--verbose",
            "--json-schema", ReviewSchema.Json,
            "--restricted",
            "--strict-mcp-config",
            "--tools", "Read,Grep,Glob",
            "--allowedTools", "Read,Grep,Glob",
            "--permission-mode", "dontAsk",
            "--no-session-persistence",
        };

        foreach (var directory in additionalDirectories)
        {
            args.Add("--add-dir");
            args.Add(directory);
        }

        if (!string.IsNullOrWhiteSpace(settings.Model)) { args.Add("--model"); args.Add(settings.Model); }
        if (!string.IsNullOrWhiteSpace(settings.Effort)) { args.Add("--effort"); args.Add(settings.Effort); }
        if (settings.MaxBudgetUsd is { } budget)
        {
            args.Add("--max-budget-usd");
            args.Add(budget.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // When this tool is itself launched from inside a Claude Code session, these variables would make
        // the child believe it is nested. Clearing them lets the review run as an independent session.
        var environment = new Dictionary<string, string?>
        {
            ["CLAUDECODE"] = null,
            ["CLAUDE_CODE_ENTRYPOINT"] = null,
        };

        JsonNode? finalEvent = null;
        JsonNode? structuredFromTool = null;
        string? model = null;
        string? apiKeySource = null;

        void HandleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return;
            JsonNode? evt;
            try { evt = JsonNode.Parse(line); }
            catch (JsonException) { return; }
            if (evt is null) return;

            switch (evt["type"]?.GetValue<string>())
            {
                case "system" when evt["subtype"]?.GetValue<string>() == "init":
                    model = evt["model"]?.GetValue<string>();
                    apiKeySource = evt["apiKeySource"]?.GetValue<string>();
                    onProgress($"Claude Code session started (model: {model ?? "default"})");
                    break;

                case "assistant":
                    if (evt["message"]?["content"] is not JsonArray content) break;
                    foreach (var block in content)
                    {
                        if (block?["type"]?.GetValue<string>() != "tool_use") continue;
                        var name = block["name"]?.GetValue<string>() ?? "?";
                        var input = block["input"];
                        if (name == "StructuredOutput")
                        {
                            structuredFromTool = input;
                            onProgress("Writing structured review result");
                            continue;
                        }

                        var target = input?["file_path"]?.GetValue<string>()
                                     ?? input?["pattern"]?.GetValue<string>()
                                     ?? input?["path"]?.GetValue<string>()
                                     ?? string.Empty;
                        onProgress($"{name} {MakeRelative(target, repositoryDirectory)}");
                    }
                    break;

                case "result":
                    finalEvent = evt;
                    break;
            }
        }

        ProcessResult process;
        try
        {
            process = await ProcessRunner.RunAsync(
                settings.Executable,
                args,
                workingDirectory: repositoryDirectory,
                standardInput: prompt,
                environment: environment,
                onStdOutLine: HandleLine,
                timeout: settings.Timeout ?? TimeSpan.FromMinutes(45),
                cancellationToken: cancellationToken);
        }
        catch (ToolNotFoundException)
        {
            throw new InvalidOperationException(
                "Claude Code CLI ('claude') was not found. Install it (https://docs.claude.com/en/docs/claude-code) and run `claude` once to log in.");
        }

        if (finalEvent is null)
        {
            throw new InvalidOperationException(
                $"Claude Code exited with code {process.ExitCode} without a result.\n{Tail(process.StdErr)}{Tail(process.StdOut)}");
        }

        if (finalEvent["is_error"]?.GetValue<bool>() == true)
        {
            var subtype = finalEvent["subtype"]?.GetValue<string>();
            var text = finalEvent["result"]?.ToString() ?? string.Empty;
            throw new InvalidOperationException($"Claude Code reported an error ({subtype}): {text}\n{Tail(process.StdErr)}");
        }

        var structured = finalEvent["structured_output"] ?? structuredFromTool ?? TryParseJson(finalEvent["result"]?.ToString());
        if (structured is null)
        {
            throw new InvalidOperationException("Claude Code finished but returned no structured review. Raw result:\n" + finalEvent["result"]);
        }

        var review = structured.Deserialize<ReviewResult>(JsonOptions)
                     ?? throw new InvalidOperationException("The structured review could not be read.");

        review.Meta ??= new ReviewMeta();
        review.Meta.Model = model;
        // Claude Code always reports total_cost_usd, an estimate at API prices. It is only a real charge
        // when the session runs on an API key; on a Claude subscription (apiKeySource "none") nothing is billed per token.
        review.Meta.UsesSubscription = apiKeySource is null or "none";
        review.Meta.CostUsd = review.Meta.UsesSubscription ? null : finalEvent["total_cost_usd"]?.GetValue<decimal>();
        review.Meta.Turns = finalEvent["num_turns"]?.GetValue<int>();
        review.Meta.DurationSeconds = finalEvent["duration_ms"]?.GetValue<double>() / 1000d;

        Normalize(review, repositoryDirectory);
        return review;
    }

    /// <summary>Clean up paths and drop findings that point at files that do not exist.</summary>
    internal static void Normalize(ReviewResult review, string repositoryDirectory)
    {
        var root = Path.GetFullPath(repositoryDirectory);
        foreach (var finding in review.Findings)
        {
            var path = finding.File.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(path))
            {
                path = Path.GetRelativePath(root, path).Replace('\\', '/');
            }
            finding.File = path.TrimStart('.', '/');
            finding.Severity = finding.Severity.ToLowerInvariant();
            finding.Category = finding.Category.ToLowerInvariant();
            if (finding.EndLine is { } end && end <= finding.Line) finding.EndLine = null;
        }

        review.Findings = review.Findings
            .Where(f => File.Exists(Path.Combine(root, f.File)))
            .OrderBy(f => f.SeverityRank)
            .ToList();
    }

    private static JsonNode? TryParseJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine > 0 && lastFence > firstNewLine) trimmed = trimmed[(firstNewLine + 1)..lastFence];
        }

        try { return JsonNode.Parse(trimmed); }
        catch (JsonException) { return null; }
    }

    private static string MakeRelative(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return path;
        try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
        catch (ArgumentException) { return path; }
    }

    private static string Tail(string text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : (text.Length > 2000 ? "..." + text[^2000..] : text) + "\n";
}
