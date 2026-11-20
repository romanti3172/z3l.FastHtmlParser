namespace z3l.FastHtmlParser;

/// <summary>
/// HTML to Markdown: headings, lists, links, images, tables, code blocks, quotes and emphasis
/// (CommonMark with the GitHub tables, strikethrough and task lists).
/// </summary>
/// <remarks>
/// The converter is a part of z3l.FastHtmlParser, which comes with this package; this is its entry point
/// for a string of HTML. For a part of a page, parse the page and ask the tag:
/// <c>new HtmlParser(html, url).CssFirst("article").Markdown()</c>.
/// </remarks>
/// <example>
/// <code>
/// string markdown = HtmlToMarkdown.Convert(html);
///
/// // with the address of the page the links and images are absolute
/// string page = HtmlToMarkdown.Convert(html, "https://example.com/blog/post");
///
/// // only the text and its structure
/// string text = HtmlToMarkdown.Convert(html, options: new MarkdownOptions { Links = false, Images = false });
/// </code>
/// </example>
public static class HtmlToMarkdown
{
    /// <summary>
    /// A page, or a piece of HTML, as Markdown. An empty string for null or empty HTML and for a page
    /// that shows nothing.
    /// </summary>
    /// <param name="html">The HTML.</param>
    /// <param name="url">The address of the page: with it the links and images are absolute.</param>
    /// <param name="options">What to leave out: links, images. All of them are written by default.</param>
    public static string Convert(string? html, string? url = null, MarkdownOptions? options = null)
    {
        return string.IsNullOrEmpty(html) ? "" : new HtmlParser(html, url).Markdown(options);
    }
}
