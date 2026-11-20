using System.Diagnostics.CodeAnalysis;
using z3l.FastHtmlParser.Core.Markdown;

namespace z3l.FastHtmlParser;

/// <summary>
/// A document or a tag as Markdown: headings, lists, links, images, tables, code blocks, quotes and emphasis
/// (CommonMark with the GitHub tables, strikethrough and task lists).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only what a browser shows is text: <c>head</c>, <c>script</c>, <c>style</c>, <c>noscript</c>, <c>template</c>,
/// comments, <c>svg</c> and form controls are left out.</item>
/// <item>Whitespace is collapsed as a browser collapses it; lines end with LF, and the result has no line break
/// at its start or end.</item>
/// <item>Text is escaped only where Markdown would read it as its own: <c>snake_case</c>, <c>2 * 3</c> and
/// <c>[1]</c> stay as they are, <c>*text*</c> or a line that starts with <c>#</c> or <c>1.</c> get a backslash.</item>
/// <item>Links and images are absolute when the document has a page URL, as <c>AttributeHref()</c> gives them;
/// a link without an address, with <c>#</c> or with a script is written as its text.</item>
/// <item>A table of data is written as a table (<c>colspan</c> and <c>rowspan</c> as empty cells, an empty heading row
/// when it has none); a table used for the layout of a page as the blocks in its cells.</item>
/// </list>
/// The conversion reads the tags as the parser recorded them and makes no object per tag;
/// its time grows linearly with the size of the page on any markup.
/// </remarks>
/// <example>
/// <code>
/// var doc = new HtmlParser(html, "https://example.com/post/1");
///
/// string page = doc.Markdown();                                  // the whole page
/// string? article = doc.Find(HtmlTag.Article).Markdown();        // one part of it
/// string? table = doc.CssFirst("table.specs").Markdown();        // "| Name | Value |\n| --- | --- |\n| ..."
/// string? plain = doc.Find(HtmlTag.Main).Markdown(new MarkdownOptions { Links = false, Images = false });
/// </code>
/// </example>
public static class ParserTagMarkdownExtensions
{
    /// <summary>
    /// The tag as Markdown: the tag itself with its content, so a heading gives <c>## Title</c>, a link
    /// <c>[text](address)</c>, a table a table. Null for a null tag, an empty string when nothing in it is shown.
    /// </summary>
    [return: NotNullIfNotNull(nameof(tag))]
    public static string? Markdown(this ParserTag? tag, MarkdownOptions? options = null)
    {
        if (tag == null) return null;

        return MarkdownConverter.Convert(tag, options ?? MarkdownOptions.Default);
    }

    /// <summary>The whole document as Markdown. Null for a null document.</summary>
    [return: NotNullIfNotNull(nameof(document))]
    public static string? Markdown(this HtmlParser? document, MarkdownOptions? options = null)
    {
        if (document == null) return null;

        return MarkdownConverter.Convert(document, options ?? MarkdownOptions.Default);
    }
}
