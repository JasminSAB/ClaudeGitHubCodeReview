using System.Globalization;

namespace ClaudeGitHubCodeReview.Cli;

public sealed class AppOptions
{
    public string? Repository { get; set; }
    public int? PullRequest { get; set; }
    public bool Snapshot { get; set; }
    public string Language { get; set; } = "auto";
    public string? Model { get; set; }
    public string? Effort { get; set; }
    public decimal? MaxBudgetUsd { get; set; }
    public int MaxFindings { get; set; } = 25;
    public int MaxInlineComments { get; set; } = 40;
    public string? Focus { get; set; }
    public string? GuidelinesPath { get; set; }
    /// <summary>Skill names to use (file names in Prompts/skills without .md). Empty means all skills.</summary>
    public List<string> Skills { get; } = [];
    /// <summary>An extra folder of skill files; a skill with the same name replaces the built-in one.</summary>
    public string? SkillsDirectory { get; set; }
    public string OutputDirectory { get; set; } = "reviews";
    public string? WorkDirectory { get; set; }
    public int TimeoutMinutes { get; set; } = 45;
    public bool DryRun { get; set; }
    public bool AssumeYes { get; set; }
    public bool NonInteractive { get; set; }
    public bool UseFork { get; set; }
    public string? PublishReportPath { get; set; }
    public bool ShowHelp { get; set; }
    public bool ListSkills { get; set; }

    /// <summary>
    /// Optional suggestion shown when asking for the repository URL (CGCR_DEFAULT_REPO environment variable).
    /// There is deliberately no built-in default: the user chooses the repository at launch.
    /// </summary>
    public static string? DefaultRepositoryUrl =>
        Environment.GetEnvironmentVariable("CGCR_DEFAULT_REPO") is { Length: > 0 } fromEnv ? fromEnv.Trim() : null;

    public string ResolvedWorkDirectory => WorkDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeGitHubCodeReview", "repos");

    public const string Usage = """
        ClaudeGitHubCodeReview (cgcr): AI code review of a GitHub repository, posted as the logged-in GitHub user.

        Usage:
          cgcr [options]                          interactive: asks for the repository URL and what to review
          cgcr --repo <url> --dry-run             review and write a local report only
          cgcr --repo <url> --publish <file.json> post a previously generated report without re-running the review

        Target:
          --repo <url>              GitHub repository, e.g. https://github.com/owner/app or owner/app
                                    (asked at launch when omitted; $CGCR_DEFAULT_REPO pre-fills the prompt)
          --pr <number>             review an open pull request
          --snapshot                review the whole codebase of the default branch (no PR needed)

        Review:
          --language <auto|en|bs|name>  language of the comments (default auto: follows the codebase's own language)
          --focus "<text>"          extra instructions appended to the rubric
          --guidelines <file>       replace the core rubric (default: Prompts/review-guidelines.md next to the executable)
          --skills <a,b,...>        review only with these skills (default: all files in Prompts/skills)
          --skills-dir <dir>        add your own skill files; a file with the same name replaces the built-in skill
          --list-skills             show the available skills and exit
          --max-findings <n>        upper bound of findings (default 25)
          --model <name>            Claude model alias or id (default: your Claude Code default)
          --effort <level>          low | medium | high | xhigh | max
          --max-budget <usd>        stop the review if it would cost more than this
          --timeout <minutes>       default 45

        Publishing:
          --dry-run                 do not touch GitHub; only write reviews/*.md and reviews/*.json
          --max-comments <n>        inline comments per review; the rest go in the summary (default 40)
          --fork                    if you cannot push to the repository, create the snapshot PR in your fork without asking
          -y, --yes                 do not ask for confirmation before posting
          --non-interactive         never prompt (use defaults; posting requires --yes)

        Other:
          --output <dir>            report directory (default ./reviews)
          --workdir <dir>           where repositories are cloned (default %LOCALAPPDATA%/ClaudeGitHubCodeReview/repos)
          -h, --help
        """;

    public static AppOptions Parse(string[] args)
    {
        var options = new AppOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Option {arg} needs a value.");
            int NextInt() => int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0
                ? v : throw new ArgumentException($"Option {arg} needs a positive number.");

            switch (arg)
            {
                case "--repo" or "--repository": options.Repository = Next(); break;
                case "--pr": options.PullRequest = NextInt(); break;
                case "--snapshot": options.Snapshot = true; break;
                case "--language" or "--lang": options.Language = Next(); break;
                case "--focus": options.Focus = Next(); break;
                case "--guidelines": options.GuidelinesPath = Next(); break;
                case "--skills":
                    options.Skills.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--skills-dir": options.SkillsDirectory = Next(); break;
                case "--list-skills": options.ListSkills = true; break;
                case "--max-findings": options.MaxFindings = NextInt(); break;
                case "--max-comments": options.MaxInlineComments = NextInt(); break;
                case "--model": options.Model = Next(); break;
                case "--effort": options.Effort = Next(); break;
                case "--max-budget":
                    options.MaxBudgetUsd = decimal.TryParse(Next(), NumberStyles.Number, CultureInfo.InvariantCulture, out var budget) && budget > 0
                        ? budget : throw new ArgumentException("--max-budget needs a positive amount in USD.");
                    break;
                case "--timeout": options.TimeoutMinutes = NextInt(); break;
                case "--output": options.OutputDirectory = Next(); break;
                case "--workdir": options.WorkDirectory = Next(); break;
                case "--dry-run": options.DryRun = true; break;
                case "--fork": options.UseFork = true; break;
                case "-y" or "--yes": options.AssumeYes = true; break;
                case "--non-interactive": options.NonInteractive = true; break;
                case "--publish": options.PublishReportPath = Next(); break;
                case "-h" or "--help" or "/?": options.ShowHelp = true; break;
                default: throw new ArgumentException($"Unknown option '{arg}'. Use --help.");
            }
        }

        if (options.PullRequest is not null && options.Snapshot)
        {
            throw new ArgumentException("Use either --pr or --snapshot, not both.");
        }

        if (options.PublishReportPath is not null && options.DryRun)
        {
            throw new ArgumentException("--publish and --dry-run cannot be combined.");
        }

        return options;
    }
}
