using System.Text.Encodings.Web;
using System.Text.Json;
using ClaudeGitHubCodeReview.Cli;
using ClaudeGitHubCodeReview.GitHub;
using ClaudeGitHubCodeReview.Infrastructure;
using ClaudeGitHubCodeReview.Publishing;
using ClaudeGitHubCodeReview.Review;

namespace ClaudeGitHubCodeReview;

/// <summary>
/// The use case "review a repository and post the result": resolve the target, check who we act as,
/// get the code, run the review, write a local report, and (after confirmation) post to GitHub.
/// </summary>
public sealed class ReviewWorkflow(AppOptions options, ConsoleUi ui)
{
    private static readonly JsonSerializerOptions ReportJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep č, ć, đ, š, ž readable in the report
    };

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        ui.Title("ClaudeGitHubCodeReview");

        // 1. Which repository?
        var repository = RepositoryRef.Parse(options.Repository ?? AskForRepository());
        ui.Info($"Repository: {repository.WebUrl}");

        // 2. Who are we? (every comment is posted as this account)
        ui.Step("Checking identity");
        var (token, tokenSource) = await GitHubClient.ResolveTokenAsync(repository.Host);
        using var gitHub = new GitHubClient(repository, token);
        var git = new GitCli(repository.Host, token);

        GitHubUser? user = null;
        try { user = await gitHub.GetAuthenticatedUserAsync(); }
        catch (GitHubApiException ex) { ui.Warn($"The GitHub token from {tokenSource} was rejected: {ex.Message}"); }

        var (gitName, gitEmail) = await git.GetIdentityAsync();
        ui.Info($"GitHub account: {(user is null ? "(not logged in)" : $"@{user.Login}{(user.Name is null ? "" : $" ({user.Name})")} via {tokenSource}")}");
        ui.Info($"git identity:   {gitName ?? "(not set)"} <{gitEmail ?? "not set"}>");

        var willPublish = !options.DryRun;
        if (willPublish && user is null)
        {
            ui.Error("Posting a review needs a GitHub login. Run `gh auth login` (or set GH_TOKEN), or use --dry-run to only write a local report.");
            return 2;
        }

        RepositoryInfo repositoryInfo;
        try
        {
            repositoryInfo = await gitHub.GetRepositoryAsync(repository);
        }
        catch (GitHubApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            ui.Error($"{repository.FullName} was not found, or {(user is null ? "it is private and you are not logged in (run `gh auth login`)" : $"@{user.Login} has no access to it")}.");
            return 2;
        }
        ui.Info($"Default branch: {repositoryInfo.DefaultBranch} · you can push: {(repositoryInfo.CanPush ? "yes" : "no")}");

        if (options.PublishReportPath is not null)
        {
            var saved = JsonSerializer.Deserialize<ReviewResult>(await File.ReadAllTextAsync(options.PublishReportPath, cancellationToken))
                        ?? throw new InvalidOperationException("The report file is empty.");
            return await PublishAsync(repository, repositoryInfo, gitHub, git, user!, saved, cancellationToken);
        }

        // 3. What to review?
        var (mode, pullRequest) = await ChooseTargetAsync(gitHub, repository, cancellationToken);

        // 4. Get the code.
        ui.Step("Fetching the code");
        var cloneDirectory = Path.Combine(options.ResolvedWorkDirectory, repository.Owner, repository.Name);
        await git.EnsureCloneAsync(repository.CloneUrl, cloneDirectory);

        string commit;
        if (mode == ReviewMode.PullRequest)
        {
            await git.RunAsync(cloneDirectory, "fetch", "--quiet", "origin", $"pull/{pullRequest!.Number}/head", pullRequest.BaseRef);
            commit = pullRequest.HeadSha;
        }
        else
        {
            commit = await git.RunAsync(cloneDirectory, "rev-parse", $"origin/{repositoryInfo.DefaultBranch}");
        }

        await git.CheckoutCleanAsync(cloneDirectory, commit);
        ui.Success($"{cloneDirectory} @ {commit[..7]}");

        // 5. Which language do we write in?
        var language = await ResolveLanguageAsync(git, cloneDirectory);

        // 6. Build the request.
        var sidecar = cloneDirectory + ".cgcr"; // outside the repo, but readable by the reviewer via --add-dir
        Directory.CreateDirectory(sidecar);
        var request = new ReviewRequest(
            repository.FullName, commit, mode, language, LoadGuidelines(), options.MaxFindings, options.Focus);

        if (mode == ReviewMode.PullRequest)
        {
            var diff = await git.RunAsync(cloneDirectory, "diff", "--no-color", $"origin/{pullRequest!.BaseRef}...{commit}");
            var diffPath = Path.Combine(sidecar, $"pr-{pullRequest.Number}.diff");
            await File.WriteAllTextAsync(diffPath, diff, cancellationToken);
            var changed = await gitHub.ListPullRequestFilesAsync(repository, pullRequest.Number);
            request = request with
            {
                PullRequestNumber = pullRequest.Number,
                PullRequestTitle = pullRequest.Title,
                DiffFilePath = diffPath,
                ChangedFiles = changed.Select(f => $"{f.FileName} ({f.Status})").ToList(),
            };
        }

        // 7. Review.
        ui.Step($"Reviewing with Claude Code (read-only, restricted). This usually takes 3-15 minutes");
        var reviewer = new ClaudeCodeReviewer(new ClaudeSettings(
            Model: options.Model, Effort: options.Effort, MaxBudgetUsd: options.MaxBudgetUsd,
            Timeout: TimeSpan.FromMinutes(options.TimeoutMinutes)));
        var started = DateTimeOffset.Now;
        var review = await reviewer.ReviewAsync(cloneDirectory, ReviewPromptBuilder.Build(request), [sidecar], ui.Progress, cancellationToken);

        review.Meta!.Repository = repository.FullName;
        review.Meta.Commit = commit;
        review.Meta.Mode = mode.ToString();
        review.Meta.PullRequest = pullRequest?.Number;
        review.Meta.Language = language;
        review.Meta.ReviewedBy = user?.Login;
        review.Meta.GeneratedAt = started;
        review.Meta.Skills = _usedSkills;

        // 8. Always keep a local copy.
        var (markdownPath, jsonPath) = await WriteReportAsync(review, repository, cancellationToken);
        PrintSummary(review);
        ui.Success($"Report: {markdownPath}");
        ui.Info($"Data:   {jsonPath}  (post it later with: cgcr --repo {repository.WebUrl} --publish \"{jsonPath}\")");

        if (!willPublish)
        {
            ui.Info("Dry run: nothing was posted to GitHub.");
            return 0;
        }

        return await PublishAsync(repository, repositoryInfo, gitHub, git, user!, review, cancellationToken, pullRequest);
    }

    private string AskForRepository()
    {
        if (!ui.IsInteractive)
        {
            return AppOptions.DefaultRepositoryUrl
                   ?? throw new ArgumentException("No repository given. Pass --repo <url> or set CGCR_DEFAULT_REPO.");
        }

        if (AppOptions.DefaultRepositoryUrl is { } suggestion)
        {
            return ui.Ask("GitHub repository URL", suggestion);
        }

        return ui.AskRequired(
            "GitHub repository URL (e.g. https://github.com/owner/app)",
            answer => RepositoryRef.TryParse(answer, out _),
            "That is not a GitHub repository URL. Use https://github.com/owner/repo, git@github.com:owner/repo.git or owner/repo.");
    }

    private async Task<(ReviewMode Mode, PullRequestInfo? PullRequest)> ChooseTargetAsync(GitHubClient gitHub, RepositoryRef repository, CancellationToken cancellationToken)
    {
        if (options.PullRequest is { } number)
        {
            return (ReviewMode.PullRequest, await gitHub.GetPullRequestAsync(repository, number));
        }

        if (options.Snapshot)
        {
            return (ReviewMode.Snapshot, null);
        }

        var open = await gitHub.ListOpenPullRequestsAsync(repository);
        if (open.Count == 0)
        {
            ui.Info("No open pull requests: the existing codebase will be reviewed as a whole (snapshot mode).");
            return (ReviewMode.Snapshot, null);
        }

        var choices = new List<string> { "Whole codebase of the default branch (snapshot)" };
        choices.AddRange(open.Select(pr => $"PR #{pr.Number}: {pr.Title} (@{pr.Author}{(pr.IsDraft ? ", draft" : "")})"));
        var index = ui.Choose("What should be reviewed?", choices);
        return index == 0 ? (ReviewMode.Snapshot, null) : (ReviewMode.PullRequest, open[index - 1]);
    }

    private async Task<string> ResolveLanguageAsync(GitCli git, string cloneDirectory)
    {
        var requested = options.Language.Trim();
        if (!requested.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var language = requested.ToLowerInvariant() switch
            {
                "en" or "english" => LanguageDetector.English,
                "bs" or "bosnian" or "bosanski" => LanguageDetector.Bosnian,
                _ => requested,
            };
            ui.Info($"Review language: {language} (from --language)");
            return language;
        }

        var log = await git.TryRunAsync(cloneDirectory, "log", "-n", "50", "--format=%s%n%b") ?? string.Empty;
        var detection = LanguageDetector.Detect(cloneDirectory, log.Split('\n'));
        ui.Info($"Review language: {detection}");
        return detection.Language;
    }

    private List<string> _usedSkills = [];

    public static string BuiltInSkillsDirectory => Path.Combine(AppContext.BaseDirectory, "Prompts", "skills");

    private string LoadGuidelines()
    {
        var path = options.GuidelinesPath ?? Path.Combine(AppContext.BaseDirectory, "Prompts", "review-guidelines.md");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Review guidelines not found at {path}.");
        }
        ui.Info($"Rubric: {path}");

        var skills = SkillCatalog.Select(SkillCatalog.Load(BuiltInSkillsDirectory, options.SkillsDirectory), options.Skills);
        _usedSkills = skills.Select(s => s.Name).ToList();
        ui.Info($"Skills: {string.Join(", ", _usedSkills)}");
        return SkillCatalog.Compose(File.ReadAllText(path), skills);
    }

    private async Task<int> PublishAsync(
        RepositoryRef repository,
        RepositoryInfo repositoryInfo,
        GitHubClient gitHub,
        GitCli git,
        GitHubUser user,
        ReviewResult review,
        CancellationToken cancellationToken,
        PullRequestInfo? pullRequest = null)
    {
        var meta = review.Meta ?? throw new InvalidOperationException("The review has no metadata (repository/commit).");
        var labels = Labels.For(meta.Language);
        var isSnapshot = meta.Mode == nameof(ReviewMode.Snapshot);

        if (!meta.Repository.Equals(repository.FullName, StringComparison.OrdinalIgnoreCase))
        {
            ui.Warn($"This review was made for {meta.Repository}, not {repository.FullName}.");
            if (!ui.Confirm("Post it anyway?")) return 1;
        }

        if (!isSnapshot)
        {
            pullRequest ??= await gitHub.GetPullRequestAsync(repository, meta.PullRequest!.Value);
            if (pullRequest.HeadSha != meta.Commit)
            {
                ui.Warn($"PR #{pullRequest.Number} has new commits since the review ({meta.Commit[..7]} -> {pullRequest.HeadSha[..7]}). Line numbers may be off.");
                if (!ui.Confirm("Post anyway?")) return 1;
                // GitHub maps comments made against an older commit onto the current diff when possible.
                pullRequest = pullRequest with { HeadSha = meta.Commit };
            }
        }

        // Where would a snapshot PR live?
        var prRepository = repository;
        if (isSnapshot && !repositoryInfo.CanPush)
        {
            ui.Warn($"@{user.Login} cannot push to {repository.FullName}, so the snapshot PR cannot be opened there.");
            if (!options.UseFork && !ui.Confirm($"Fork it to {user.Login}/{repository.Name} and open the snapshot PR in the fork?"))
            {
                ui.Info("Nothing was posted. The local report is still available.");
                return 1;
            }
            prRepository = await gitHub.EnsureForkAsync(repository, user.Login);
            ui.Success($"Using fork {prRepository.WebUrl}");
        }

        // Explicit confirmation: this writes to GitHub under the user's name.
        ui.Step("About to post to GitHub");
        if (isSnapshot)
        {
            ui.Info($"1. Push two branches code-review/<timestamp>/baseline and .../snapshot to {prRepository.FullName}");
            ui.Info($"2. Open a DRAFT pull request there (\"do not merge\") showing commit {meta.Commit[..7]} as the diff");
            ui.Info($"3. Post one review with up to {options.MaxInlineComments} inline comments ({review.Findings.Count} findings) as @{user.Login}");
        }
        else
        {
            ui.Info($"Post one review on {pullRequest!.HtmlUrl} with up to {options.MaxInlineComments} inline comments ({review.Findings.Count} findings) as @{user.Login}");
        }

        if (!ui.Confirm("Continue?"))
        {
            ui.Info("Cancelled. Nothing was posted.");
            return 1;
        }

        if (isSnapshot)
        {
            var cloneDirectory = Path.Combine(options.ResolvedWorkDirectory, repository.Owner, repository.Name);
            await git.EnsureCloneAsync(repository.CloneUrl, cloneDirectory);
            await git.RunAsync(cloneDirectory, "cat-file", "-e", meta.Commit + "^{commit}");

            ui.Step("Creating the snapshot pull request");
            var branches = await new SnapshotPullRequestCreator(git).CreateBranchesAsync(
                cloneDirectory, meta.Commit, prRepository.CloneUrl, user.Name ?? user.Login, $"{user.Login}@users.noreply.github.com");

            pullRequest = await gitHub.CreatePullRequestAsync(
                prRepository,
                string.Format(labels.SnapshotPrTitle, repositoryInfo.DefaultBranch, meta.Commit[..7]),
                branches.HeadBranch,
                branches.BaseBranch,
                string.Format(labels.SnapshotPrBody, branches.BaseBranch, branches.HeadBranch, repositoryInfo.DefaultBranch, meta.Commit));
            ui.Success($"Pull request: {pullRequest.HtmlUrl}");
        }

        ui.Step("Posting the review");
        var url = await new GitHubReviewPublisher(gitHub).PublishAsync(prRepository, pullRequest!, review, labels, user.Login, options.MaxInlineComments);
        ui.Success($"Review posted: {url}");
        return 0;
    }

    private async Task<(string Markdown, string Json)> WriteReportAsync(ReviewResult review, RepositoryRef repository, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var baseName = $"{repository.Owner}-{repository.Name}-{review.Meta!.GeneratedAt:yyyyMMdd-HHmmss}";
        var markdownPath = Path.GetFullPath(Path.Combine(options.OutputDirectory, baseName + ".md"));
        var jsonPath = Path.GetFullPath(Path.Combine(options.OutputDirectory, baseName + ".json"));

        await File.WriteAllTextAsync(markdownPath, ReviewFormatter.Report(review, Labels.For(review.Meta.Language)), cancellationToken);
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(review, ReportJson), cancellationToken);
        return (markdownPath, jsonPath);
    }

    private void PrintSummary(ReviewResult review)
    {
        var meta = review.Meta!;
        ui.Step($"Review finished: {review.Findings.Count} findings"
                + (meta.DurationSeconds is { } s ? $" in {TimeSpan.FromSeconds(s):mm\\:ss}" : string.Empty)
                + (meta.UsesSubscription ? ", Claude subscription"
                   : meta.CostUsd is { } c ? $", ${c.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} USD on the API key" : string.Empty));
        foreach (var finding in review.Findings)
        {
            ui.Info($"{ReviewFormatter.SeverityIcon(finding.Severity)} [{finding.Category}] {finding.Title}  ({finding.File}:{finding.Line})");
        }
    }
}
