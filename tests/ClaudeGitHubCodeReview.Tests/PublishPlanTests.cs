using ClaudeGitHubCodeReview.Publishing;
using ClaudeGitHubCodeReview.Review;

namespace ClaudeGitHubCodeReview.Tests;

public class PublishPlanTests
{
    private static Finding F(string file, int line, int? end = null) =>
        new() { File = file, Line = line, EndLine = end, Severity = "high", Category = "ef-core", Title = "t", Body = "b" };

    private static readonly Dictionary<string, HashSet<int>> Commentable = new()
    {
        ["A.cs"] = [.. Enumerable.Range(1, 50)],
        ["B.cs"] = [10, 11, 12],
    };

    [Fact]
    public void Finding_on_a_diff_line_becomes_inline()
    {
        var plan = GitHubReviewPublisher.Plan([F("A.cs", 20)], Commentable, Labels.English, 10);

        var comment = Assert.Single(plan.Inline).Comment;
        Assert.Equal(("A.cs", 20, (int?)null), (comment.Path, comment.Line, comment.StartLine));
        Assert.Empty(plan.NotInline);
    }

    [Fact]
    public void Multi_line_range_uses_start_line()
    {
        var plan = GitHubReviewPublisher.Plan([F("A.cs", 20, 25)], Commentable, Labels.English, 10);

        var comment = Assert.Single(plan.Inline).Comment;
        Assert.Equal(20, comment.StartLine);
        Assert.Equal(25, comment.Line);
    }

    [Fact]
    public void Long_range_is_anchored_on_its_first_line()
    {
        var plan = GitHubReviewPublisher.Plan([F("A.cs", 3, 40)], Commentable, Labels.English, 10);

        var comment = Assert.Single(plan.Inline).Comment;
        Assert.Null(comment.StartLine);
        Assert.Equal(3, comment.Line);
    }

    [Fact]
    public void Line_close_to_the_diff_is_snapped_far_line_goes_to_summary()
    {
        var plan = GitHubReviewPublisher.Plan([F("B.cs", 14), F("B.cs", 30)], Commentable, Labels.English, 10);

        Assert.Equal(12, Assert.Single(plan.Inline).Comment.Line);
        Assert.Equal(30, Assert.Single(plan.NotInline).Line);
    }

    [Fact]
    public void File_outside_the_pr_and_overflow_go_to_summary()
    {
        var plan = GitHubReviewPublisher.Plan([F("A.cs", 1), F("A.cs", 2), F("Other.cs", 1)], Commentable, Labels.English, maxInlineComments: 1);

        Assert.Single(plan.Inline);
        Assert.Equal(2, plan.NotInline.Count);
    }

    [Fact]
    public void Bosnian_labels_are_used_for_bosnian_reviews()
    {
        var body = ReviewFormatter.InlineComment(F("A.cs", 1), Labels.For("Bosnian"));

        Assert.Contains("Visoko", body);
        Assert.Contains("EF Core", body);
    }
}
