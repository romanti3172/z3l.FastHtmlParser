namespace z3l.FastHtmlParser.Core;

internal static class UriCore
{
    internal static string CorrectLink(ReadOnlySpan<char> link, HtmlParser parser, string? currentUrl)
    {
        // The page's own URL is parsed once per document instead of once per link.
        return currentUrl == null
            ? CorrectLink(link, parser.PageUrl, parser.PageUri)
            : CorrectLink(link, currentUrl);
    }

    /// <summary>
    /// The link a browser follows: the raw attribute value <paramref name="link"/> with its character references
    /// decoded ("&amp;amp;" → '&amp;', by the HTML5 attribute rules) and made absolute (relative to <paramref name="currentPage"/>).
    /// Percent-escapes are part of the URL and are kept: "%3F" in a path is not a '?'.
    /// An empty link returns <paramref name="currentPage"/>, as a browser does.
    /// </summary>
    internal static string CorrectLink(ReadOnlySpan<char> link, string currentPage, Uri? currentPageUri = null)
    {
        if (link.IsEmpty)
        {
            return currentPage;
        }

        string linkString = SpanHandlingCore.DecodeAttribute(link);

        if (!string.IsNullOrEmpty(currentPage) && !IsAbsoluteLink(linkString) &&
            (currentPageUri ?? (Uri.TryCreate(currentPage, UriKind.Absolute, out var parsed) ? parsed : null)) is { } baseUri)
        {
            // Combining with an explicitly relative Uri: on Linux/macOS the string overload
            // could still read "/path" as a file path.
            if ((Uri.TryCreate(linkString, UriKind.Relative, out var relativeUri) && Uri.TryCreate(baseUri, relativeUri, out var combinedUri))
                || Uri.TryCreate(baseUri, linkString, out combinedUri))
            {
                return combinedUri.ToString();
            }
        }

        return linkString;
    }

    /// <summary>
    /// True for a link with its own scheme ("https://…", "mailto:…", "file:…").
    /// On Linux/macOS <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> also accepts "/path" and
    /// "//host/path" as implicit file paths; in HTML these are relative to the page, so they are
    /// not absolute here (the same on every OS).
    /// </summary>
    private static bool IsAbsoluteLink(string link)
    {
        return Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && (!uri.IsFile || link.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
    }
}
