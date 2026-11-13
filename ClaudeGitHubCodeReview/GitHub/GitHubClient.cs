using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeGitHubCodeReview.Infrastructure;

namespace ClaudeGitHubCodeReview.GitHub;

public sealed record GitHubUser(string Login, string? Name);

public sealed record RepositoryInfo(string DefaultBranch, bool CanPush, bool IsPrivate, string HtmlUrl);

public sealed record PullRequestInfo(int Number, string Title, string Author, string HeadSha, string HeadRef, string BaseRef, string HtmlUrl, bool IsDraft);

public sealed record PullRequestFile(string FileName, string Status, string? Patch);

public sealed record ReviewComment(string Path, int Line, int? StartLine, string Body);

/// <summary>
/// Minimal GitHub REST client. It authenticates as whoever is logged in to the GitHub CLI
/// (gh auth login), so every comment and pull request is created under that account.
/// </summary>
public sealed class GitHubClient : IDisposable
{
    private readonly HttpClient _http;

    public GitHubClient(RepositoryRef repository, string? token)
    {
        Token = token;
        _http = new HttpClient { BaseAddress = repository.ApiBaseUri, Timeout = TimeSpan.FromSeconds(100) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClaudeGitHubCodeReview", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public string? Token { get; }

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    /// <summary>
    /// Finds the credentials of the user who is currently logged in on this machine, in this order:
    /// GH_TOKEN / GITHUB_TOKEN, the GitHub CLI (`gh auth login`), then git's own credential helper
    /// (e.g. Git Credential Manager, the login git uses for clone/push). Never prompts.
    /// </summary>
    public static async Task<(string? Token, string Source)> ResolveTokenAsync(string host)
    {
        foreach (var variable in new[] { "GH_TOKEN", "GITHUB_TOKEN" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)) return (value.Trim(), $"${variable}");
        }

        try
        {
            var gh = await ProcessRunner.RunAsync("gh", ["auth", "token", "--hostname", host], timeout: TimeSpan.FromSeconds(30));
            if (gh.Succeeded && !string.IsNullOrWhiteSpace(gh.StdOut)) return (gh.StdOut.Trim(), "GitHub CLI (gh)");
        }
        catch (ToolNotFoundException) { /* gh is optional */ }

        try
        {
            var credential = await ProcessRunner.RunAsync(
                "git", ["credential", "fill"],
                standardInput: $"protocol=https\nhost={host}\n\n",
                environment: new Dictionary<string, string?> { ["GIT_TERMINAL_PROMPT"] = "0", ["GCM_INTERACTIVE"] = "never" },
                timeout: TimeSpan.FromSeconds(30));
            var password = credential.StdOut.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .FirstOrDefault(l => l.StartsWith("password=", StringComparison.Ordinal))?["password=".Length..];
            if (credential.Succeeded && !string.IsNullOrWhiteSpace(password)) return (password, "git credential helper");
        }
        catch (ToolNotFoundException) { /* git missing is reported elsewhere */ }
        catch (TimeoutException) { /* a helper that wants to prompt; treat as not logged in */ }

        return (null, "none");
    }

    public async Task<GitHubUser?> GetAuthenticatedUserAsync()
    {
        if (!IsAuthenticated) return null;
        var json = await GetAsync("user");
        return new GitHubUser(json["login"]!.GetValue<string>(), json["name"]?.GetValue<string?>());
    }

    public async Task<RepositoryInfo> GetRepositoryAsync(RepositoryRef repo)
    {
        var json = await GetAsync($"repos/{repo.Owner}/{repo.Name}");
        return new RepositoryInfo(
            json["default_branch"]!.GetValue<string>(),
            json["permissions"]?["push"]?.GetValue<bool>() ?? false,
            json["private"]?.GetValue<bool>() ?? false,
            json["html_url"]!.GetValue<string>());
    }

    public async Task<bool> RepositoryExistsAsync(RepositoryRef repo)
    {
        using var response = await _http.GetAsync($"repos/{repo.Owner}/{repo.Name}");
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<PullRequestInfo>> ListOpenPullRequestsAsync(RepositoryRef repo)
    {
        var json = await GetArrayAsync($"repos/{repo.Owner}/{repo.Name}/pulls?state=open&per_page=50");
        return json.Select(n => ToPullRequest(n!)).ToList();
    }

    public async Task<PullRequestInfo> GetPullRequestAsync(RepositoryRef repo, int number) =>
        ToPullRequest(await GetAsync($"repos/{repo.Owner}/{repo.Name}/pulls/{number}"));

    public async Task<IReadOnlyList<PullRequestFile>> ListPullRequestFilesAsync(RepositoryRef repo, int number)
    {
        var files = new List<PullRequestFile>();
        for (var page = 1; page <= 30; page++) // GitHub caps this endpoint at 3000 files
        {
            var batch = await GetArrayAsync($"repos/{repo.Owner}/{repo.Name}/pulls/{number}/files?per_page=100&page={page}");
            files.AddRange(batch.Select(n => new PullRequestFile(
                n!["filename"]!.GetValue<string>(),
                n["status"]!.GetValue<string>(),
                n["patch"]?.GetValue<string>())));
            if (batch.Count < 100) break;
        }

        return files;
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(RepositoryRef repo, string title, string head, string @base, string body)
    {
        var payload = new JsonObject { ["title"] = title, ["head"] = head, ["base"] = @base, ["body"] = body, ["draft"] = true };
        try
        {
            return ToPullRequest(await SendAsync(HttpMethod.Post, $"repos/{repo.Owner}/{repo.Name}/pulls", payload));
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity && ex.Message.Contains("draft", StringComparison.OrdinalIgnoreCase))
        {
            // Draft PRs are not available on every plan; fall back to a regular PR.
            payload["draft"] = false;
            return ToPullRequest(await SendAsync(HttpMethod.Post, $"repos/{repo.Owner}/{repo.Name}/pulls", payload));
        }
    }

    /// <summary>Posts one review with event COMMENT (never APPROVE / REQUEST_CHANGES: the tool informs, humans decide).</summary>
    public async Task<string> CreateReviewAsync(RepositoryRef repo, int number, string commitSha, string body, IReadOnlyList<ReviewComment> comments)
    {
        var commentArray = new JsonArray();
        foreach (var c in comments)
        {
            var node = new JsonObject { ["path"] = c.Path, ["line"] = c.Line, ["side"] = "RIGHT", ["body"] = c.Body };
            if (c.StartLine is { } start && start < c.Line)
            {
                node["start_line"] = start;
                node["start_side"] = "RIGHT";
            }
            commentArray.Add(node);
        }

        var payload = new JsonObject
        {
            ["commit_id"] = commitSha,
            ["body"] = body,
            ["event"] = "COMMENT",
            ["comments"] = commentArray,
        };

        var json = await SendAsync(HttpMethod.Post, $"repos/{repo.Owner}/{repo.Name}/pulls/{number}/reviews", payload);
        return json["html_url"]?.GetValue<string>() ?? $"{repo.WebUrl}/pull/{number}";
    }

    /// <summary>Forks the repository into the authenticated account and waits until the fork is ready.</summary>
    public async Task<RepositoryRef> EnsureForkAsync(RepositoryRef upstream, string login)
    {
        var fork = upstream with { Owner = login };
        if (await RepositoryExistsAsync(fork)) return fork;

        await SendAsync(HttpMethod.Post, $"repos/{upstream.Owner}/{upstream.Name}/forks", new JsonObject());
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (await RepositoryExistsAsync(fork)) return fork;
        }

        throw new InvalidOperationException($"Fork {fork.FullName} was requested but did not become available in time.");
    }

    public void Dispose() => _http.Dispose();

    private static PullRequestInfo ToPullRequest(JsonNode n) => new(
        n["number"]!.GetValue<int>(),
        n["title"]!.GetValue<string>(),
        n["user"]?["login"]?.GetValue<string>() ?? "?",
        n["head"]!["sha"]!.GetValue<string>(),
        n["head"]!["ref"]!.GetValue<string>(),
        n["base"]!["ref"]!.GetValue<string>(),
        n["html_url"]!.GetValue<string>(),
        n["draft"]?.GetValue<bool>() ?? false);

    private async Task<JsonNode> GetAsync(string path)
    {
        using var response = await _http.GetAsync(path);
        return await ReadAsync(response, path);
    }

    private async Task<JsonArray> GetArrayAsync(string path) => (await GetAsync(path)).AsArray();

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode payload)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload) };
        using var response = await _http.SendAsync(request);
        return await ReadAsync(response, path);
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, string path)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var message = text;
            try
            {
                var error = JsonNode.Parse(text);
                message = error?["message"]?.GetValue<string>() ?? text;
                if (error?["errors"] is JsonArray errors && errors.Count > 0)
                {
                    message += " | " + errors.ToJsonString();
                }
            }
            catch (JsonException) { /* keep raw text */ }

            throw new GitHubApiException(response.StatusCode, $"GitHub API {path} returned {(int)response.StatusCode}: {message}");
        }

        return JsonNode.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text)!;
    }
}

public sealed class GitHubApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
