using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>Text helpers for raw HTML fragments (for tags use <c>Text()</c> / <c>TextLineBreaks()</c>).</summary>
public static class HtmlText
{
    /// <summary>
    /// Decodes HTML5 character references: all named references (&amp;nbsp;, &amp;copy;, &amp;check;, ...),
    /// legacy names without ';', and numeric references (&amp;#169;, &amp;#xA9;), as browsers do.
    /// </summary>
    public static string Decode(ReadOnlySpan<char> html) => HtmlDecodeCore.HtmlDecode(html);

    /// <inheritdoc cref="Decode(ReadOnlySpan{char})"/>
    public static string? Decode(string? html) => html == null ? null : HtmlDecodeCore.HtmlDecode(html);

    /// <summary>Removes tags, collapses whitespace into single spaces, trims and decodes entities.</summary>
    public static string? ToText(ReadOnlySpan<char> html) => HtmlToTextCore.ClearTags(html);

    /// <inheritdoc cref="ToText(ReadOnlySpan{char})"/>
    public static string? ToText(string? html) => HtmlToTextCore.ClearTags(html);

    /// <summary>
    /// Converts HTML to text keeping the visible line structure:
    /// &lt;br&gt; and block elements (div, li, tr, ...) give a new line, p and h1–h6 a new paragraph;
    /// script/style contents and comments are dropped, entities are decoded.
    /// </summary>
    public static string? ToTextWithLineBreaks(ReadOnlySpan<char> html) => ParsingCore.ClearTagsLineBreaks(html);

    /// <inheritdoc cref="ToTextWithLineBreaks(ReadOnlySpan{char})"/>
    public static string? ToTextWithLineBreaks(string? html) => ParsingCore.ClearTagsLineBreaks(html);

    /// <summary>
    /// Converts HTML to Markdown: headings, lists, links, images, tables, code blocks, quotes and emphasis;
    /// see <see cref="ParserTagMarkdownExtensions"/>. Null for null or empty input.
    /// To make the links absolute, parse the page with its URL and call <c>Markdown()</c> on the document:
    /// <c>new HtmlParser(html, url).Markdown()</c>.
    /// </summary>
    public static string? ToMarkdown(string? html, MarkdownOptions? options = null)
    {
        return string.IsNullOrEmpty(html) ? null : new HtmlParser(html).Markdown(options);
    }

    /// <summary>
    /// Untrusted HTML made safe to put in a page: only the allowed tags, attributes, URL schemes and CSS properties
    /// are kept; see <see cref="ParserTagSanitizeExtensions"/> and <see cref="SanitizeOptions"/>.
    /// Null for null input. To make relative links absolute, parse with the page URL and call
    /// <c>Sanitize()</c> on the document: <c>new HtmlParser(html, url).Sanitize()</c>.
    /// </summary>
    public static string? Sanitize(string? html, SanitizeOptions? options = null)
    {
        return html == null ? null : html.Length == 0 ? string.Empty : new HtmlParser(html).Sanitize(options);
    }
}
