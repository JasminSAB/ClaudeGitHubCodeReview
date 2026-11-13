using ClaudeGitHubCodeReview.Cli;
using ClaudeGitHubCodeReview.Review;

namespace ClaudeGitHubCodeReview.Tests;

public class AppOptionsTests
{
    [Fact]
    public void Parses_typical_command_line()
    {
        var options = AppOptions.Parse(["--repo", "contoso/booking-api", "--pr", "7", "--language", "bs", "--max-budget", "2.5", "-y"]);

        Assert.Equal("contoso/booking-api", options.Repository);
        Assert.Equal(7, options.PullRequest);
        Assert.Equal("bs", options.Language);
        Assert.Equal(2.5m, options.MaxBudgetUsd);
        Assert.True(options.AssumeYes);
    }

    [Theory]
    [InlineData("--pr", "1", "--snapshot")]
    [InlineData("--unknown")]
    [InlineData("--pr", "zero")]
    [InlineData("--publish", "x.json", "--dry-run")]
    public void Rejects_invalid_combinations(params string[] args) =>
        Assert.Throws<ArgumentException>(() => AppOptions.Parse(args));

    [Fact]
    public void Review_schema_is_valid_json() =>
        Assert.NotNull(System.Text.Json.Nodes.JsonNode.Parse(ReviewSchema.Json));

    [Fact]
    public void Normalize_makes_paths_relative_and_drops_unknown_files()
    {
        var root = Directory.CreateTempSubdirectory("cgcr-norm-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Services"));
            File.WriteAllText(Path.Combine(root, "Services", "A.cs"), "class A {}");
            var review = new ReviewResult
            {
                Findings =
                [
                    new Finding { File = Path.Combine(root, "Services", "A.cs"), Line = 1, Severity = "LOW" },
                    new Finding { File = "./Services/A.cs", Line = 1, EndLine = 1, Severity = "critical" },
                    new Finding { File = "Missing.cs", Line = 1 },
                ],
            };

            ClaudeCodeReviewer.Normalize(review, root);

            Assert.Equal(2, review.Findings.Count);
            Assert.All(review.Findings, f => Assert.Equal("Services/A.cs", f.File));
            Assert.Equal("critical", review.Findings[0].Severity); // sorted by severity
            Assert.Null(review.Findings[0].EndLine);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
