using System.Text.RegularExpressions;

namespace ClaudeGitHubCodeReview.Review;

public sealed record LanguageDetection(string Language, int LocalScore, int EnglishScore, int FilesScanned)
{
    public override string ToString() =>
        $"{Language} (Bosnian/Croatian/Serbian markers: {LocalScore}, English markers: {EnglishScore}, files scanned: {FilesScanned})";
}

/// <summary>
/// Decides which human language the review should be written in, by looking at the language the
/// team itself uses: code comments, Markdown docs and commit messages. The rule is simple and
/// predictable: if the codebase "speaks" Bosnian, review in Bosnian; otherwise review in English.
/// Override with --language when the heuristic is not what you want.
/// </summary>
public static partial class LanguageDetector
{
    public const string English = "English";
    public const string Bosnian = "Bosnian";

    // Frequent function words. Kept short and unambiguous on purpose.
    private static readonly HashSet<string> LocalWords = new(StringComparer.Ordinal)
    {
        "je", "nije", "da", "se", "za", "na", "od", "ako", "samo", "ili", "koji", "koja", "koje", "kad", "kada",
        "nema", "može", "treba", "što", "šta", "sa", "iz", "pri", "već", "bez", "ovaj", "ova", "ovo", "sve",
        "prije", "nakon", "dok", "jer", "ali", "kao", "su", "smo", "nego", "gdje", "zbog", "po", "i", "u",
        "uvijek", "nikad", "ne", "ga", "ih", "mora", "vraća", "provjeri", "dodati", "korisnik", "greška", "podaci",
    };

    private static readonly HashSet<string> EnglishWords = new(StringComparer.Ordinal)
    {
        "the", "and", "is", "are", "to", "of", "for", "if", "only", "or", "which", "when", "not", "this", "that",
        "with", "from", "before", "after", "should", "must", "can", "be", "it", "we", "returns", "get", "set",
        "always", "never", "there", "otherwise", "because", "into", "by", "an", "will", "has", "have", "does",
    };

    private static readonly HashSet<string> CommentedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".cshtml", ".razor", ".ts", ".tsx", ".js", ".jsx", ".java", ".kt", ".go", ".py", ".rb", ".php", ".sql", ".fs",
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", "dist", "build", "packages", "lib", "vendor", "Migrations", ".vs", ".idea",
    };

    public static LanguageDetection Detect(string repositoryDirectory, IEnumerable<string>? commitMessages = null, int maxFiles = 400)
    {
        var local = 0;
        var english = 0;
        var files = 0;

        foreach (var file in EnumerateFiles(repositoryDirectory).Take(maxFiles))
        {
            files++;
            string text;
            try
            {
                var info = new FileInfo(file);
                if (info.Length > 512 * 1024) continue;
                text = File.ReadAllText(file);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            var prose = Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase)
                ? text
                : ExtractComments(text);
            Score(prose, ref local, ref english);
        }

        foreach (var message in commitMessages ?? [])
        {
            Score(message, ref local, ref english);
        }

        return new LanguageDetection(Decide(local, english), local, english, files);
    }

    /// <summary>Exposed for tests: classify a piece of prose.</summary>
    public static string DetectText(string text)
    {
        int local = 0, english = 0;
        Score(text, ref local, ref english);
        return Decide(local, english);
    }

    private static string Decide(int local, int english) =>
        local >= 15 && local > english * 1.2 ? Bosnian : English;

    private static void Score(string text, ref int local, ref int english)
    {
        foreach (Match match in WordPattern().Matches(text))
        {
            var word = match.Value.ToLowerInvariant();
            if (LocalWords.Contains(word)) local++;
            else if (EnglishWords.Contains(word)) english++;

            // Letters that do not occur in English are strong evidence.
            if (word.AsSpan().IndexOfAny("čćđšž") >= 0) local += 2;
        }
    }

    private static string ExtractComments(string source)
    {
        var builder = new System.Text.StringBuilder();
        foreach (Match match in CommentPattern().Matches(source))
        {
            builder.AppendLine(match.Value);
        }
        return builder.ToString();
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var entry in entries.OrderBy(e => e, StringComparer.Ordinal))
            {
                if (Directory.Exists(entry))
                {
                    if (!SkippedDirectories.Contains(Path.GetFileName(entry))) pending.Push(entry);
                    continue;
                }

                var extension = Path.GetExtension(entry);
                if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) || CommentedExtensions.Contains(extension))
                {
                    yield return entry;
                }
            }
        }
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordPattern();

    // // line comments, /* block */ comments, @* razor *@ comments, # comments (python/ruby) and -- (sql).
    [GeneratedRegex(@"//[^\r\n]*|/\*.*?\*/|@\*.*?\*@|(?m)^\s*#(?!include|region|endregion|if|endif|pragma)[^\r\n]*|--[^\r\n]*", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();
}
