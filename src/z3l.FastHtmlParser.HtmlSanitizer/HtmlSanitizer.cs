namespace z3l.FastHtmlParser;

/// <summary>
/// Untrusted HTML made safe to put in a page: only the allowed tags, attributes, URL schemes and CSS properties
/// are kept, and the result is written anew, with every text and attribute escaped and every element closed.
/// </summary>
/// <remarks>
/// The sanitizer is a part of z3l.FastHtmlParser, which comes with this package; this is its entry point for a string
/// of HTML. For a part of a page, parse the page and ask the tag: <c>new HtmlParser(html, url).CssFirst("article").Sanitize()</c>.
/// What is kept by default and what is never kept: see <see cref="SanitizeOptions"/>.
/// </remarks>
/// <example>
/// <code>
/// string safe = HtmlSanitizer.Sanitize("&lt;p onclick=\"steal()\"&gt;Hi &lt;script&gt;alert(1)&lt;/script&gt;&lt;/p&gt;");   // "&lt;p&gt;Hi &lt;/p&gt;"
///
/// // relative links made absolute, every link with rel="nofollow noopener", no styles
/// var options = new SanitizeOptions { LinkRel = "nofollow noopener" };
/// options.AllowedAttributes.Remove("style");
/// string comment = HtmlSanitizer.Sanitize(html, "https://example.com/post/1", options);
/// </code>
/// </example>
public static class HtmlSanitizer
{
    /// <summary>
    /// The HTML with only what the options allow. An empty string for null or empty HTML.
    /// </summary>
    /// <param name="html">The untrusted HTML: a fragment or a whole page.</param>
    /// <param name="url">The address of the page: with it relative links and images are made absolute.</param>
    /// <param name="options">What is kept. The defaults when null: text, lists, tables, links, images, no forms, frames or media.</param>
    public static string Sanitize(string? html, string? url = null, SanitizeOptions? options = null)
    {
        return string.IsNullOrEmpty(html) ? "" : new HtmlParser(html, url).Sanitize(options);
    }
}
