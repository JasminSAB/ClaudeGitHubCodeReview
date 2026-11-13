using System.Text;
using ClaudeGitHubCodeReview;
using ClaudeGitHubCodeReview.Cli;
using ClaudeGitHubCodeReview.GitHub;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

AppOptions options;
try
{
    options = AppOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 64;
}

if (options.ShowHelp)
{
    Console.WriteLine(AppOptions.Usage);
    return 0;
}

if (options.ListSkills)
{
    var skills = ClaudeGitHubCodeReview.Review.SkillCatalog.Load(ReviewWorkflow.BuiltInSkillsDirectory, options.SkillsDirectory);
    Console.WriteLine("Available review skills (use --skills a,b to choose; default: all):");
    foreach (var skill in skills) Console.WriteLine($"  {skill.Name,-24} {skill.Description}");
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var ui = new ConsoleUi(interactive: !options.NonInteractive, assumeYes: options.AssumeYes);
try
{
    return await new ReviewWorkflow(options, ui).RunAsync(cancellation.Token);
}
catch (OperationCanceledException)
{
    ui.Warn("Cancelled.");
    return 130;
}
catch (Exception ex) when (ex is InvalidOperationException or GitHubApiException or ArgumentException or FileNotFoundException or TimeoutException or IOException)
{
    ui.Error(ex.Message);
    return 1;
}
