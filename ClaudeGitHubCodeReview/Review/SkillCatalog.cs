namespace ClaudeGitHubCodeReview.Review;

/// <summary>One review skill: a Markdown file that tells the reviewer what to look for in one area.</summary>
public sealed record Skill(string Name, string Title, string Description, string Content, string SourcePath);

/// <summary>
/// Loads the review skills: every *.md file in the built-in Prompts/skills folder, optionally merged with
/// a user folder (a file with the same name replaces the built-in skill), optionally filtered by name.
/// </summary>
public static class SkillCatalog
{
    public static IReadOnlyList<Skill> Load(string builtInDirectory, string? extraDirectory = null)
    {
        var skills = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in new[] { builtInDirectory, extraDirectory })
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Skills folder not found: {directory}");
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.OrdinalIgnoreCase))
            {
                var skill = Parse(file);
                skills[skill.Name] = skill;
            }
        }

        return skills.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Keeps only the requested skills, in the requested order. Empty selection means all.</summary>
    public static IReadOnlyList<Skill> Select(IReadOnlyList<Skill> available, IReadOnlyCollection<string> requested)
    {
        if (requested.Count == 0) return available;

        var unknown = requested.Where(name => !available.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown skill(s): {string.Join(", ", unknown)}. Available: {string.Join(", ", available.Select(s => s.Name))}.");
        }

        return requested
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => available.First(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>The core rubric followed by every selected skill, as the rubric section of the prompt.</summary>
    public static string Compose(string coreGuidelines, IReadOnlyList<Skill> skills)
    {
        var text = new System.Text.StringBuilder(coreGuidelines.Trim());
        text.AppendLine();
        text.AppendLine();
        text.AppendLine("# Skills to apply");
        text.AppendLine($"Review the code against each of these {skills.Count} skills. Skip a skill only when the code has nothing it applies to.");
        foreach (var skill in skills)
        {
            text.AppendLine();
            text.AppendLine($"## Skill: {skill.Title}");
            text.AppendLine(StripTitle(skill.Content));
        }
        return text.ToString();
    }

    private static Skill Parse(string path)
    {
        var content = File.ReadAllText(path);
        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var name = Path.GetFileNameWithoutExtension(path);
        var title = lines.FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? name;
        var description = lines.FirstOrDefault(l => l.StartsWith("Use for:", StringComparison.OrdinalIgnoreCase))?["Use for:".Length..].Trim() ?? string.Empty;
        return new Skill(name, title, description, content, path);
    }

    private static string StripTitle(string content)
    {
        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var titleIndex = lines.FindIndex(l => l.StartsWith("# ", StringComparison.Ordinal));
        if (titleIndex >= 0) lines.RemoveAt(titleIndex);
        return string.Join('\n', lines).Trim();
    }
}
