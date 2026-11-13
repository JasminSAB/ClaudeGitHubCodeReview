using System.Text.Json.Serialization;

namespace ClaudeGitHubCodeReview.Review;

/// <summary>Structured output returned by the review engine (validated against <see cref="ReviewSchema"/>).</summary>
public sealed class ReviewResult
{
    [JsonPropertyName("domainOverview")]
    public string DomainOverview { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("strengths")]
    public List<string> Strengths { get; set; } = [];

    [JsonPropertyName("findings")]
    public List<Finding> Findings { get; set; } = [];

    /// <summary>Metadata filled in by the tool (not by the model).</summary>
    [JsonPropertyName("meta")]
    public ReviewMeta? Meta { get; set; }
}

public sealed class Finding
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("endLine")]
    public int? EndLine { get; set; }

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "other";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("suggestion")]
    public string? Suggestion { get; set; }

    [JsonIgnore]
    public int SeverityRank => Severity switch
    {
        "critical" => 0,
        "high" => 1,
        "medium" => 2,
        "low" => 3,
        _ => 4,
    };
}

public sealed class ReviewMeta
{
    public string Repository { get; set; } = string.Empty;
    public string Commit { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int? PullRequest { get; set; }
    public string Language { get; set; } = string.Empty;
    public string? ReviewedBy { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    /// <summary>Cost billed to an API key. Null when the review ran on a Claude subscription.</summary>
    public decimal? CostUsd { get; set; }
    /// <summary>True when Claude Code ran on a Claude subscription login instead of an API key.</summary>
    public bool UsesSubscription { get; set; }
    public int? Turns { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Model { get; set; }
    /// <summary>Names of the review skills the rubric contained.</summary>
    public List<string> Skills { get; set; } = [];
}

public static class ReviewSchema
{
    public static readonly string[] Severities = ["critical", "high", "medium", "low"];

    public static readonly string[] Categories =
        ["authentication", "authorization", "ddd", "clean-architecture", "ef-core", "security", "correctness", "other"];

    /// <summary>JSON Schema handed to `claude --json-schema`, so the CLI validates the model's answer for us.</summary>
    public static string Json { get; } = $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["domainOverview", "summary", "strengths", "findings"],
          "properties": {
            "domainOverview": { "type": "string", "description": "2-5 sentences: what the system does, main aggregates/entities, business rules." },
            "summary": { "type": "string", "description": "Overall assessment in Markdown, written in the review language." },
            "strengths": { "type": "array", "items": { "type": "string" } },
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["file", "line", "severity", "category", "title", "body"],
                "properties": {
                  "file": { "type": "string", "description": "Path relative to the repository root, forward slashes." },
                  "line": { "type": "integer", "minimum": 1 },
                  "endLine": { "type": "integer", "minimum": 1 },
                  "severity": { "type": "string", "enum": [{{string.Join(", ", Severities.Select(s => $"\"{s}\""))}}] },
                  "category": { "type": "string", "enum": [{{string.Join(", ", Categories.Select(c => $"\"{c}\""))}}] },
                  "title": { "type": "string" },
                  "body": { "type": "string", "description": "Markdown: problem, why it matters (concrete scenario), how to fix." },
                  "suggestion": { "type": "string", "description": "Optional replacement or example code (no Markdown fences)." }
                }
              }
            }
          }
        }
        """;
}
