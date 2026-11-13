using ClaudeGitHubCodeReview.GitHub;

namespace ClaudeGitHubCodeReview.Tests;

public class DiffParserTests
{
    [Fact]
    public void Added_and_context_lines_are_commentable_removed_lines_are_not()
    {
        const string patch = """
            @@ -10,4 +10,5 @@ public class Foo
             context10
            -removed
            +added11
            +added12
             context13

             context15
            """;

        var lines = DiffParser.GetCommentableLines(patch);

        Assert.Equal([10, 11, 12, 13, 14, 15], lines.OrderBy(l => l));
    }

    [Fact]
    public void Multiple_hunks_restart_numbering()
    {
        const string patch = "@@ -1,2 +1,2 @@\n a\n+b\n@@ -40,1 +50,2 @@\n x\n+y\n\\ No newline at end of file";

        var lines = DiffParser.GetCommentableLines(patch);

        Assert.Equal([1, 2, 50, 51], lines.OrderBy(l => l));
    }

    [Fact]
    public void New_file_patch_covers_every_line()
    {
        var patch = "@@ -0,0 +1,3 @@\n+one\n+two\n+three";

        Assert.Equal([1, 2, 3], DiffParser.GetCommentableLines(patch).OrderBy(l => l));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_patch_has_no_commentable_lines(string? patch) =>
        Assert.Empty(DiffParser.GetCommentableLines(patch));
}
