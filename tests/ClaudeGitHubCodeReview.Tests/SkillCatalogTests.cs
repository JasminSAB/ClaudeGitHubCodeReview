using ClaudeGitHubCodeReview.Review;
using Xunit;

namespace ClaudeGitHubCodeReview.Tests;

public sealed class SkillCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cgcr-skills-" + Guid.NewGuid().ToString("N"));
    private readonly string _builtIn;
    private readonly string _custom;

    public SkillCatalogTests()
    {
        _builtIn = Directory.CreateDirectory(Path.Combine(_root, "built-in")).FullName;
        _custom = Directory.CreateDirectory(Path.Combine(_root, "custom")).FullName;
        Write(_builtIn, "ef-core", "# EF Core\n\nUse for: queries.\n\n- AsNoTracking for reads.");
        Write(_builtIn, "security-input", "# Input security\n\nUse for: injection.\n\n- FromSqlRaw with interpolation.");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void Write(string directory, string name, string content) =>
        File.WriteAllText(Path.Combine(directory, name + ".md"), content);

    [Fact]
    public void Loads_every_markdown_file_with_title_and_description()
    {
        var skills = SkillCatalog.Load(_builtIn);

        Assert.Equal(["ef-core", "security-input"], skills.Select(s => s.Name));
        Assert.Equal("EF Core", skills[0].Title);
        Assert.Equal("queries.", skills[0].Description);
    }

    [Fact]
    public void A_custom_skill_with_the_same_name_replaces_the_built_in_one()
    {
        Write(_custom, "ef-core", "# Our EF Core rules\n\nUse for: our queries.");
        Write(_custom, "naming", "# Bosnian naming\n\nUse for: domain names in Bosnian.");

        var skills = SkillCatalog.Load(_builtIn, _custom);

        Assert.Equal(["ef-core", "naming", "security-input"], skills.Select(s => s.Name));
        Assert.Equal("Our EF Core rules", skills.Single(s => s.Name == "ef-core").Title);
    }

    [Fact]
    public void Select_keeps_the_requested_skills_in_order()
    {
        var skills = SkillCatalog.Load(_builtIn);

        var selected = SkillCatalog.Select(skills, ["security-input", "EF-CORE"]);

        Assert.Equal(["security-input", "ef-core"], selected.Select(s => s.Name));
    }

    [Fact]
    public void Select_with_no_names_keeps_all_skills()
    {
        var skills = SkillCatalog.Load(_builtIn);

        Assert.Equal(2, SkillCatalog.Select(skills, []).Count);
    }

    [Fact]
    public void Select_rejects_an_unknown_skill_and_lists_the_available_ones()
    {
        var skills = SkillCatalog.Load(_builtIn);

        var error = Assert.Throws<ArgumentException>(() => SkillCatalog.Select(skills, ["caching"]));

        Assert.Contains("caching", error.Message);
        Assert.Contains("ef-core", error.Message);
    }

    [Fact]
    public void Compose_puts_the_core_rubric_first_and_each_skill_under_its_own_heading()
    {
        var skills = SkillCatalog.Load(_builtIn);

        var rubric = SkillCatalog.Compose("# Core\n\nSeverity scale.", skills);

        Assert.StartsWith("# Core", rubric);
        Assert.Contains("## Skill: EF Core", rubric);
        Assert.Contains("## Skill: Input security", rubric);
        Assert.Contains("- FromSqlRaw with interpolation.", rubric);
        Assert.DoesNotContain("\n# EF Core", rubric);
    }

    [Fact]
    public void A_missing_skills_folder_is_reported()
    {
        Assert.Throws<DirectoryNotFoundException>(() => SkillCatalog.Load(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void The_built_in_skills_ship_with_the_tool()
    {
        var skills = SkillCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Prompts", "skills"));

        Assert.Contains(skills, s => s.Name == "async-and-concurrency");
        Assert.All(skills, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
    }
}
