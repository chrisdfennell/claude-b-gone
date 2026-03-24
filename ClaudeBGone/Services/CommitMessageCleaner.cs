using System.Text.RegularExpressions;

namespace ClaudeBGone.Services;

public static partial class CommitMessageCleaner
{
    // Matches lines like:
    //   Co-Authored-By: Claude <noreply@anthropic.com>
    //   Co-authored-by: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
    //   co-authored-by: Claude Sonnet 4 <noreply@anthropic.com>
    [GeneratedRegex(
        @"^\s*[Cc]o-[Aa]uthored-[Bb]y:\s*Claude.*<.*@anthropic\.com>.*$\r?\n?",
        RegexOptions.Multiline)]
    private static partial Regex ClaudeCoAuthorPattern();

    public static bool HasClaudeCoAuthor(string message) =>
        ClaudeCoAuthorPattern().IsMatch(message);

    public static string Clean(string message)
    {
        var cleaned = ClaudeCoAuthorPattern().Replace(message, "");
        // Remove trailing whitespace/blank lines left behind
        cleaned = cleaned.TrimEnd();
        return cleaned.Length > 0 ? cleaned + "\n" : message;
    }

    public static string ExtractCoAuthorLine(string message)
    {
        var match = ClaudeCoAuthorPattern().Match(message);
        return match.Success ? match.Value.Trim() : string.Empty;
    }
}
