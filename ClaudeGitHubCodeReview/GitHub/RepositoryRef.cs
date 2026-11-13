using System.Text.RegularExpressions;

namespace ClaudeGitHubCodeReview.GitHub;

/// <summary>An owner/name pair on a GitHub host (github.com or GitHub Enterprise Server).</summary>
public sealed partial record RepositoryRef(string Host, string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";

    public string WebUrl => $"https://{Host}/{Owner}/{Name}";

    public string CloneUrl => $"https://{Host}/{Owner}/{Name}.git";

    public Uri ApiBaseUri => Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        ? new Uri("https://api.github.com/")
        : new Uri($"https://{Host}/api/v3/");

    /// <summary>
    /// Accepts https://github.com/owner/repo(.git)(/anything), git@github.com:owner/repo.git or owner/repo.
    /// </summary>
    public static bool TryParse(string? input, out RepositoryRef? repository)
    {
        repository = null;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var text = input.Trim();

        var ssh = SshPattern().Match(text);
        if (ssh.Success)
        {
            repository = new RepositoryRef(ssh.Groups["host"].Value, ssh.Groups["owner"].Value, Clean(ssh.Groups["name"].Value));
            return true;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) return false;
            repository = new RepositoryRef(uri.Host, segments[0], Clean(segments[1]));
            return true;
        }

        var shortForm = ShortPattern().Match(text);
        if (shortForm.Success)
        {
            repository = new RepositoryRef("github.com", shortForm.Groups["owner"].Value, Clean(shortForm.Groups["name"].Value));
            return true;
        }

        return false;
    }

    public static RepositoryRef Parse(string input) =>
        TryParse(input, out var repository)
            ? repository!
            : throw new ArgumentException($"'{input}' is not a GitHub repository URL (expected https://github.com/owner/repo).");

    private static string Clean(string name) =>
        name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    [GeneratedRegex(@"^git@(?<host>[^:]+):(?<owner>[^/]+)/(?<name>[^/]+?)/?$")]
    private static partial Regex SshPattern();

    [GeneratedRegex(@"^(?<owner>[A-Za-z0-9][A-Za-z0-9-]*)/(?<name>[A-Za-z0-9._-]+)$")]
    private static partial Regex ShortPattern();
}
