using ClaudeGitHubCodeReview.GitHub;
using ClaudeGitHubCodeReview.Review;

namespace ClaudeGitHubCodeReview.Publishing;

public sealed record PublishPlan(IReadOnlyList<(Finding Finding, ReviewComment Comment)> Inline, IReadOnlyList<Finding> NotInline);

/// <summary>Posts a review (summary + inline comments) to a pull request as the authenticated GitHub user.</summary>
public sealed class GitHubReviewPublisher(GitHubClient gitHub)
{
    /// <summary>How far a finding may be moved to reach a commentable line (models are occasionally off by a line or two).</summary>
    private const int SnapDistance = 3;

    /// <summary>Longer ranges (a whole class or method) are anchored on their first line instead of highlighting a wall of code.</summary>
    private const int MaxHighlightedLines = 10;

    public async Task<string> PublishAsync(RepositoryRef repository, PullRequestInfo pullRequest, ReviewResult review, Labels labels, string? reviewer, int maxInlineComments)
    {
        var files = await WaitForFilesAsync(repository, pullRequest.Number);
        var commentable = files.ToDictionary(f => f.FileName, f => DiffParser.GetCommentableLines(f.Patch), StringComparer.Ordinal);

        var plan = Plan(review.Findings, commentable, labels, maxInlineComments);
        var body = ReviewFormatter.ReviewBody(review, plan.NotInline, labels, reviewer, plan.Inline.Count);

        return await gitHub.CreateReviewAsync(repository, pullRequest.Number, pullRequest.HeadSha, body, plan.Inline.Select(i => i.Comment).ToList());
    }

    /// <summary>
    /// Decide which findings become inline comments. A finding is inline only if its file is in the PR
    /// and its line (or a line within <see cref="SnapDistance"/>) is part of the diff; everything else
    /// goes into the review body so nothing is lost and GitHub never rejects the review.
    /// </summary>
    public static PublishPlan Plan(IEnumerable<Finding> findings, IReadOnlyDictionary<string, HashSet<int>> commentableLines, Labels labels, int maxInlineComments)
    {
        var inline = new List<(Finding, ReviewComment)>();
        var notInline = new List<Finding>();

        foreach (var finding in findings)
        {
            if (inline.Count >= maxInlineComments
                || !commentableLines.TryGetValue(finding.File, out var lines)
                || Snap(finding.Line, lines) is not { } line)
            {
                notInline.Add(finding);
                continue;
            }

            int? startLine = null;
            var end = finding.EndLine ?? line;
            if (end > line && end - line <= MaxHighlightedLines && Enumerable.Range(line, end - line + 1).All(lines.Contains))
            {
                // Multi-line comment: GitHub wants start_line < line, both in the same hunk.
                startLine = line;
                line = end;
            }

            inline.Add((finding, new ReviewComment(finding.File, line, startLine, ReviewFormatter.InlineComment(finding, labels))));
        }

        return new PublishPlan(inline, notInline);
    }

    private static int? Snap(int line, HashSet<int> lines)
    {
        if (lines.Contains(line)) return line;
        for (var distance = 1; distance <= SnapDistance; distance++)
        {
            if (lines.Contains(line - distance)) return line - distance;
            if (lines.Contains(line + distance)) return line + distance;
        }
        return null;
    }

    /// <summary>Right after a PR is created GitHub may still be computing its diff; wait briefly for it.</summary>
    private async Task<IReadOnlyList<PullRequestFile>> WaitForFilesAsync(RepositoryRef repository, int number)
    {
        IReadOnlyList<PullRequestFile> files = [];
        for (var attempt = 0; attempt < 10; attempt++)
        {
            files = await gitHub.ListPullRequestFilesAsync(repository, number);
            if (files.Count > 0) return files;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        return files;
    }
}
