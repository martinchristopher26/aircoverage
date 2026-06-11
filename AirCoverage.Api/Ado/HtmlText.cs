using System.Net;
using System.Text.RegularExpressions;

namespace AirCoverage.Api.Ado;

/// <summary>
/// ADO stores Description as HTML; the app edits plain text. These conversions are
/// intentionally lossy-but-safe for a plain-text editor.
/// </summary>
public static partial class HtmlText
{
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var withBreaks = BreakRegex().Replace(html, "\n");
        var noTags = TagRegex().Replace(withBreaks, "");
        return WebUtility.HtmlDecode(noTags).Trim();
    }

    public static string ToHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return WebUtility.HtmlEncode(text).Replace("\n", "<br>");
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();
}
