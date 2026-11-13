using System.Text;

namespace ClaudeGitHubCodeReview.Infrastructure;

/// <summary>
/// Runs git. When a GitHub token is available it is injected through GIT_CONFIG_* environment
/// variables (an http extraheader), so it never appears on the command line, in the remote URL
/// or in .git/config.
/// </summary>
public sealed class GitCli
{
    private readonly IReadOnlyDictionary<string, string?> _environment;

    public GitCli(string? gitHubHost = null, string? token = null)
    {
        var env = new Dictionary<string, string?>
        {
            ["GIT_TERMINAL_PROMPT"] = "0", // never hang waiting for a password prompt
        };

        if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(gitHubHost))
        {
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"x-access-token:{token}"));
            env["GIT_CONFIG_COUNT"] = "1";
            env["GIT_CONFIG_KEY_0"] = $"http.https://{gitHubHost}/.extraheader";
            env["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {basic}";
        }

        _environment = env;
    }

    public async Task<string> RunAsync(string? workingDirectory, params string[] args)
    {
        var result = await ProcessRunner.RunAsync("git", args, workingDirectory, environment: _environment,
            timeout: TimeSpan.FromMinutes(10));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed (exit {result.ExitCode}):\n{result.StdErr.Trim()}");
        }

        return result.StdOut.Trim();
    }

    public async Task<string?> TryRunAsync(string? workingDirectory, params string[] args)
    {
        var result = await ProcessRunner.RunAsync("git", args, workingDirectory, environment: _environment);
        return result.Succeeded ? result.StdOut.Trim() : null;
    }

    public async Task<(string? Name, string? Email)> GetIdentityAsync(string? workingDirectory = null)
    {
        var name = await TryRunAsync(workingDirectory, "config", "user.name");
        var email = await TryRunAsync(workingDirectory, "config", "user.email");
        return (string.IsNullOrWhiteSpace(name) ? null : name, string.IsNullOrWhiteSpace(email) ? null : email);
    }

    /// <summary>Clone the repository, or refresh an existing cached clone.</summary>
    public async Task EnsureCloneAsync(string cloneUrl, string directory)
    {
        if (Directory.Exists(Path.Combine(directory, ".git")))
        {
            await RunAsync(directory, "remote", "set-url", "origin", cloneUrl);
            await RunAsync(directory, "fetch", "--prune", "--quiet", "origin");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        await RunAsync(null, "clone", "--quiet", "--no-tags", cloneUrl, directory);
    }

    /// <summary>Check out an exact commit and remove any leftovers from previous runs (this is a private cache clone).</summary>
    public async Task CheckoutCleanAsync(string directory, string commitish)
    {
        await RunAsync(directory, "-c", "advice.detachedHead=false", "checkout", "--quiet", "--force", "--detach", commitish);
        await RunAsync(directory, "clean", "-fdxq");
    }
}
