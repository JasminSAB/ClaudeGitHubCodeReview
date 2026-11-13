using System.Text;
using ClaudeGitHubCodeReview.Review;

namespace ClaudeGitHubCodeReview.Publishing;

/// <summary>Turns a <see cref="ReviewResult"/> into GitHub Markdown (inline comments, review body, local report).</summary>
public static class ReviewFormatter
{
    private const int GitHubBodyLimit = 65000;

    public static string SeverityIcon(string severity) => severity switch
    {
        "critical" => "🔴",
        "high" => "🟠",
        "medium" => "🟡",
        "low" => "🔵",
        _ => "⚪",
    };

    public static string InlineComment(Finding finding, Labels labels)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{SeverityIcon(finding.Severity)} **{labels.SeverityName(finding.Severity)} · {labels.CategoryName(finding.Category)}: {finding.Title}**");
        sb.AppendLine();
        sb.AppendLine(finding.Body.Trim());
        AppendSuggestion(sb, finding, labels);
        return sb.ToString().TrimEnd();
    }

    public static string ReviewBody(ReviewResult review, IReadOnlyList<Finding> notInline, Labels labels, string? reviewer, int inlineCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## 🤖 {labels.ReviewTitle}");
        sb.AppendLine($"_{labels.Focus(review.Meta)}_");
        sb.AppendLine();

        AppendOverview(sb, review, labels);

        if (notInline.Count > 0)
        {
            sb.AppendLine($"### {labels.NotInline}");
            foreach (var finding in notInline)
            {
                sb.AppendLine($"<details><summary>{SeverityIcon(finding.Severity)} <b>{Escape(finding.Title)}</b> · <code>{Escape(finding.File)}:{finding.Line}</code></summary>");
                sb.AppendLine();
                sb.AppendLine(finding.Body.Trim());
                AppendSuggestion(sb, finding, labels);
                sb.AppendLine();
                sb.AppendLine("</details>");
            }
            sb.AppendLine();
        }

        AppendFooter(sb, review, labels, reviewer);
        var body = sb.ToString();
        return body.Length <= GitHubBodyLimit ? body : body[..GitHubBodyLimit] + "\n\n…";
    }

    /// <summary>Full, self-contained Markdown report written to disk on every run (including dry runs).</summary>
    public static string Report(ReviewResult review, Labels labels)
    {
        var meta = review.Meta;
        var sb = new StringBuilder();
        sb.AppendLine($"# {labels.ReviewTitle}: {meta?.Repository}");
        sb.AppendLine();
        sb.AppendLine($"_{labels.Focus(meta)}_");
        sb.AppendLine();
        if (meta is not null)
        {
            sb.AppendLine($"- Commit: `{meta.Commit}`");
            sb.AppendLine($"- Mode: {meta.Mode}{(meta.PullRequest is { } pr ? $" (PR #{pr})" : string.Empty)}");
            sb.AppendLine($"- Language: {meta.Language}");
            if (meta.ReviewedBy is not null) sb.AppendLine($"- Run by: @{meta.ReviewedBy}");
            sb.AppendLine($"- Generated: {meta.GeneratedAt:yyyy-MM-dd HH:mm zzz}");
            if (meta.Model is not null) sb.AppendLine($"- Model: {meta.Model}");
            sb.AppendLine();
        }

        AppendOverview(sb, review, labels);

        sb.AppendLine($"## {labels.Findings}");
        sb.AppendLine();
        var number = 1;
        foreach (var finding in review.Findings)
        {
            sb.AppendLine($"### {number++}. {SeverityIcon(finding.Severity)} {finding.Title}");
            var range = finding.EndLine is { } end ? $"{finding.Line}-{end}" : finding.Line.ToString();
            sb.AppendLine($"**{labels.Severity}:** {labels.SeverityName(finding.Severity)} · **{labels.Category}:** {labels.CategoryName(finding.Category)} · **{labels.Location}:** `{finding.File}:{range}`");
            sb.AppendLine();
            sb.AppendLine(finding.Body.Trim());
            AppendSuggestion(sb, finding, labels);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendOverview(StringBuilder sb, ReviewResult review, Labels labels)
    {
        if (!string.IsNullOrWhiteSpace(review.DomainOverview))
        {
            sb.AppendLine($"### {labels.Domain}");
            sb.AppendLine(review.DomainOverview.Trim());
            sb.AppendLine();
        }

        sb.AppendLine($"### {labels.Summary}");
        sb.AppendLine(review.Summary.Trim());
        sb.AppendLine();

        if (review.Strengths.Count > 0)
        {
            sb.AppendLine($"### {labels.Strengths}");
            foreach (var strength in review.Strengths) sb.AppendLine($"- {strength}");
            sb.AppendLine();
        }

        if (review.Findings.Count > 0)
        {
            sb.AppendLine($"| {labels.Severity} | # |");
            sb.AppendLine("|---|---|");
            foreach (var group in review.Findings.GroupBy(f => f.Severity).OrderBy(g => g.First().SeverityRank))
            {
                sb.AppendLine($"| {SeverityIcon(group.Key)} {labels.SeverityName(group.Key)} | {group.Count()} |");
            }
            sb.AppendLine();
        }
    }

    private static void AppendFooter(StringBuilder sb, ReviewResult review, Labels labels, string? reviewer)
    {
        sb.AppendLine("---");
        var who = reviewer is null ? string.Empty : $" @{reviewer}";
        sb.AppendLine($"<sub>{labels.GeneratedBy}{who}. Commit `{review.Meta?.Commit[..Math.Min(7, review.Meta?.Commit.Length ?? 0)]}`.</sub>");
    }

    private static void AppendSuggestion(StringBuilder sb, Finding finding, Labels labels)
    {
        if (string.IsNullOrWhiteSpace(finding.Suggestion)) return;
        sb.AppendLine();
        sb.AppendLine($"**{labels.Suggestion}:**");
        sb.AppendLine("```csharp");
        sb.AppendLine(finding.Suggestion.Trim('\n', '\r'));
        sb.AppendLine("```");
    }

    private static string Escape(string text) => System.Net.WebUtility.HtmlEncode(text);
}
