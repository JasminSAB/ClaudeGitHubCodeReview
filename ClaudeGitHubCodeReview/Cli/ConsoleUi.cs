namespace ClaudeGitHubCodeReview.Cli;

/// <summary>All console interaction in one place, so the workflow stays readable and non-interactive runs are trivial.</summary>
public sealed class ConsoleUi(bool interactive, bool assumeYes)
{
    private readonly object _gate = new();

    public bool IsInteractive => interactive && !Console.IsInputRedirected;

    public void Title(string text) => Write(ConsoleColor.Cyan, $"\n{text}\n{new string('=', text.Length)}");

    public void Step(string text) => Write(ConsoleColor.White, $"\n> {text}");

    public void Info(string text) => Write(ConsoleColor.Gray, $"  {text}");

    public void Progress(string text) => Write(ConsoleColor.DarkGray, $"    · {text}");

    public void Success(string text) => Write(ConsoleColor.Green, $"  ✔ {text}");

    public void Warn(string text) => Write(ConsoleColor.Yellow, $"  ! {text}");

    public void Error(string text) => Write(ConsoleColor.Red, $"  ✖ {text}");

    public string Ask(string question, string defaultValue)
    {
        if (!IsInteractive) return defaultValue;
        lock (_gate)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  {question} [{defaultValue}]: ");
            Console.ResetColor();
        }
        var answer = Console.ReadLine();
        return string.IsNullOrWhiteSpace(answer) ? defaultValue : answer.Trim();
    }

    /// <summary>Asks until a non-empty answer that passes <paramref name="isValid"/> is given.</summary>
    public string AskRequired(string question, Func<string, bool> isValid, string invalidMessage)
    {
        if (!IsInteractive)
        {
            throw new ArgumentException($"{question} is required in non-interactive mode.");
        }

        while (true)
        {
            lock (_gate)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  {question}: ");
                Console.ResetColor();
            }

            var answer = Console.ReadLine()?.Trim();
            if (answer is null) throw new OperationCanceledException(); // input closed (Ctrl+Z / Ctrl+D)
            if (answer.Length > 0 && isValid(answer)) return answer;
            Warn(invalidMessage);
        }
    }

    public bool Confirm(string question, bool defaultValue = false)
    {
        if (assumeYes) return true;
        if (!IsInteractive) return false;
        var hint = defaultValue ? "Y/n" : "y/N";
        var answer = Ask($"{question} ({hint})", defaultValue ? "y" : "n");
        return answer.StartsWith("y", StringComparison.OrdinalIgnoreCase)
               || answer.StartsWith("d", StringComparison.OrdinalIgnoreCase); // "da"
    }

    /// <summary>Numbered menu; returns the zero-based index of the chosen option.</summary>
    public int Choose(string question, IReadOnlyList<string> choices, int defaultIndex = 0)
    {
        if (!IsInteractive) return defaultIndex;
        Info(question);
        for (var i = 0; i < choices.Count; i++)
        {
            Info($"  {i + 1}) {choices[i]}");
        }

        while (true)
        {
            var answer = Ask("Choose", (defaultIndex + 1).ToString());
            if (int.TryParse(answer, out var number) && number >= 1 && number <= choices.Count) return number - 1;
            Warn($"Enter a number between 1 and {choices.Count}.");
        }
    }

    private void Write(ConsoleColor color, string text)
    {
        lock (_gate)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }
    }
}
