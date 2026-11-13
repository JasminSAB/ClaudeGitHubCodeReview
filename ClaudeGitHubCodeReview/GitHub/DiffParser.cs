using System.Text.RegularExpressions;

namespace ClaudeGitHubCodeReview.GitHub;

/// <summary>
/// GitHub only accepts inline review comments on lines that appear in the pull request diff.
/// This parses a unified-diff "patch" (as returned by the PR files API) and returns the
/// new-file ("RIGHT" side) line numbers that can carry a comment.
/// </summary>
public static partial class DiffParser
{
    public static HashSet<int> GetCommentableLines(string? patch)
    {
        var lines = new HashSet<int>();
        if (string.IsNullOrEmpty(patch)) return lines;

        var right = 0;
        var inHunk = false;

        foreach (var raw in patch.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var header = HunkHeader().Match(line);
            if (header.Success)
            {
                right = int.Parse(header.Groups["start"].Value);
                inHunk = true;
                continue;
            }

            if (!inHunk)
            {
                continue;
            }

            if (line.Length == 0)
            {
                // An empty line inside a hunk is a context line whose leading space was stripped.
                lines.Add(right);
                right++;
                continue;
            }

            switch (line[0])
            {
                case '+':
                case ' ':
                    lines.Add(right);
                    right++;
                    break;
                case '-':
                case '\\': // "\ No newline at end of file"
                    break;
            }
        }

        return lines;
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}
