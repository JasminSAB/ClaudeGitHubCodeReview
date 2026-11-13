using System.Text;

namespace ClaudeGitHubCodeReview.Review;

public enum ReviewMode
{
    /// <summary>Review the whole codebase at a commit (no pull request needed).</summary>
    Snapshot,

    /// <summary>Review the changes of an existing pull request.</summary>
    PullRequest,
}

public sealed record ReviewRequest(
    string RepositoryFullName,
    string Commit,
    ReviewMode Mode,
    string Language,
    string Guidelines,
    int MaxFindings,
    string? ExtraFocus = null,
    int? PullRequestNumber = null,
    string? PullRequestTitle = null,
    string? DiffFilePath = null,
    IReadOnlyList<string>? ChangedFiles = null);

public static class ReviewPromptBuilder
{
    public static string Build(ReviewRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are a senior .NET architect doing a code review of the GitHub repository {request.RepositoryFullName} at commit {request.Commit}.");
        sb.AppendLine("The repository is checked out in your current working directory. You have read-only tools (Read, Grep, Glob). Do not try to modify anything.");
        sb.AppendLine();

        sb.AppendLine("## Scope");
        if (request.Mode == ReviewMode.Snapshot)
        {
            sb.AppendLine("There is no pull request: review the EXISTING SOLUTION AND ITS APPROACH as a whole. Every line of every file is in scope.");
            sb.AppendLine("Focus on the backend (C#: controllers, services, domain models, data access, configuration, Program.cs). Views/CSS/JS only when they carry a security problem.");
        }
        else
        {
            sb.AppendLine($"Review pull request #{request.PullRequestNumber}: \"{request.PullRequestTitle}\".");
            sb.AppendLine($"The unified diff is in the file: {request.DiffFilePath} (read it first). The working directory contains the PR head.");
            if (request.ChangedFiles is { Count: > 0 })
            {
                sb.AppendLine("Changed files:");
                foreach (var file in request.ChangedFiles.Take(300)) sb.AppendLine($"- {file}");
            }
            sb.AppendLine("Comment primarily on changed lines. You may read any other file for context. Problems in unchanged code are allowed only when the change makes them worse or depends on them.");
        }
        sb.AppendLine();

        sb.AppendLine("## Process");
        sb.AppendLine("1. Understand the domain first: read CLAUDE.md, README*, TODO*/docs if present, then entities/models, DbContext, Program.cs, services and controllers.");
        sb.AppendLine("2. Review against the rubric below: the core rules, then every skill listed under 'Skills to apply'.");
        sb.AppendLine("3. Verify every finding against the actual code before reporting it. Do not report something you have not seen. No speculative or generic advice.");
        sb.AppendLine("4. Respect business rules documented by the team; if you disagree, flag it as a question for the product owner rather than a defect.");
        if (!string.IsNullOrWhiteSpace(request.ExtraFocus))
        {
            sb.AppendLine($"5. Additional focus requested by the reviewer: {request.ExtraFocus}");
        }
        sb.AppendLine();

        sb.AppendLine("## Output rules");
        sb.AppendLine($"- LANGUAGE: write domainOverview, summary, strengths, every title, body and suggestion text in **{request.Language}**. Keep code identifiers, type names and code samples as they are in the code.");
        sb.AppendLine($"- Report at most {request.MaxFindings} findings, the most important first. Merge duplicates: one finding per root cause, listing other affected locations inside the body.");
        sb.AppendLine("- `file` is the path relative to the repository root with forward slashes, exactly as it exists on disk.");
        sb.AppendLine("- `line` (and optional `endLine`) are 1-based line numbers in the CURRENT version of that file, pointing at the most relevant code (the declaration or statement at fault). Use the line numbers shown by the Read tool.");
        sb.AppendLine("- `body` is GitHub Markdown: the problem, a concrete scenario showing the impact in this domain, and the fix. Keep each body under ~250 words.");
        sb.AppendLine("- `suggestion` is optional plain code (C#), without Markdown fences.");
        sb.AppendLine("- `summary` is an overall assessment (Markdown, max ~300 words) with the 3-5 most important recommendations in priority order.");
        sb.AppendLine("- `strengths`: 2-6 things the codebase already does well (be specific).");
        sb.AppendLine("- Return ONLY the structured JSON result.");
        sb.AppendLine();

        sb.AppendLine("## Rubric");
        sb.AppendLine(request.Guidelines.Trim());

        return sb.ToString();
    }
}
