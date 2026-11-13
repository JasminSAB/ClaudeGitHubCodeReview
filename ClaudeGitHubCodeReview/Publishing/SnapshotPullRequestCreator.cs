using ClaudeGitHubCodeReview.GitHub;
using ClaudeGitHubCodeReview.Infrastructure;

namespace ClaudeGitHubCodeReview.Publishing;

public sealed record SnapshotBranches(string BaseBranch, string HeadBranch);

/// <summary>
/// GitHub can only attach line comments to a pull request diff, and it refuses pull requests between
/// unrelated histories. To review an existing codebase without any PR we therefore create two branches
/// that share history with the reviewed commit:
///
///   reviewed commit ──► baseline (commit that deletes every file) ──► snapshot (revert of that commit)
///
/// A PR "snapshot → baseline" then shows every file of the reviewed commit as added, so every line is
/// commentable, and the default branch is never touched.
/// </summary>
public sealed class SnapshotPullRequestCreator(GitCli git)
{
    public async Task<SnapshotBranches> CreateBranchesAsync(string repositoryDirectory, string commit, string pushRemoteUrl, string? fallbackName, string? fallbackEmail)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var branches = new SnapshotBranches($"code-review/{stamp}/baseline", $"code-review/{stamp}/snapshot");

        // Commits are made with the user's git identity; if git has none configured, fall back to the GitHub account.
        var identity = await git.GetIdentityAsync(repositoryDirectory);
        var identityArgs = new List<string>();
        if (identity.Name is null && fallbackName is not null) identityArgs.AddRange(["-c", $"user.name={fallbackName}"]);
        if (identity.Email is null && fallbackEmail is not null) identityArgs.AddRange(["-c", $"user.email={fallbackEmail}"]);

        async Task Git(params string[] args) => await git.RunAsync(repositoryDirectory, [.. identityArgs, .. args]);

        await Git("-c", "advice.detachedHead=false", "checkout", "--quiet", "--force", "--detach", commit);
        await Git("checkout", "--quiet", "-B", branches.BaseBranch);
        await Git("rm", "-r", "-q", "--ignore-unmatch", ".");
        await Git("commit", "--quiet", "--allow-empty", "--no-verify", "-m", "Code review baseline: empty tree (do not merge)");
        await Git("checkout", "--quiet", "-B", branches.HeadBranch);
        await Git("revert", "--no-commit", "HEAD");
        await Git("commit", "--quiet", "--allow-empty", "--no-verify", "-m", $"Code review snapshot of {commit} (do not merge)");
        await Git("push", "--quiet", pushRemoteUrl,
            $"refs/heads/{branches.BaseBranch}:refs/heads/{branches.BaseBranch}",
            $"refs/heads/{branches.HeadBranch}:refs/heads/{branches.HeadBranch}");

        // Leave the cache clone on the reviewed commit, and drop the local helper branches.
        await Git("-c", "advice.detachedHead=false", "checkout", "--quiet", "--force", "--detach", commit);
        await Git("branch", "--quiet", "-D", branches.BaseBranch, branches.HeadBranch);

        return branches;
    }
}
