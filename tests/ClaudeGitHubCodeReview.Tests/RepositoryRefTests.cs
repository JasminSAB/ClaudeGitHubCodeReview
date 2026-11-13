using ClaudeGitHubCodeReview.GitHub;

namespace ClaudeGitHubCodeReview.Tests;

public class RepositoryRefTests
{
    [Theory]
    [InlineData("https://github.com/contoso/booking-api")]
    [InlineData("https://github.com/contoso/booking-api.git")]
    [InlineData("https://github.com/contoso/booking-api/pulls")]
    [InlineData("git@github.com:contoso/booking-api.git")]
    [InlineData("contoso/booking-api")]
    [InlineData("  https://github.com/contoso/booking-api/  ")]
    public void Parses_common_forms(string input)
    {
        var repo = RepositoryRef.Parse(input);

        Assert.Equal("github.com", repo.Host);
        Assert.Equal("contoso", repo.Owner);
        Assert.Equal("booking-api", repo.Name);
        Assert.Equal(new Uri("https://api.github.com/"), repo.ApiBaseUri);
    }

    [Fact]
    public void Enterprise_host_uses_api_v3()
    {
        var repo = RepositoryRef.Parse("https://git.example.com/team/app");

        Assert.Equal(new Uri("https://git.example.com/api/v3/"), repo.ApiBaseUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://github.com/only-owner")]
    public void Rejects_invalid_input(string input) =>
        Assert.False(RepositoryRef.TryParse(input, out _));
}
