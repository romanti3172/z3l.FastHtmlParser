using System.Diagnostics.CodeAnalysis;
using z3l.FastHtmlParser.Core.Sanitizer;

namespace z3l.FastHtmlParser;

/// <summary>
/// Untrusted HTML made safe to put in a page (comments, posts, e-mails, scraped content): only the allowed tags,
/// attributes, URL schemes and CSS properties are kept, see <see cref="SanitizeOptions"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The result is written anew, never copied: kept tags by their lower-case names with their kept attributes
/// in double quotes, text and attribute values decoded and escaped, every opened element closed. Comments, end tags
/// without a start and broken markup give nothing.</item>
/// <item>Scripts, event handlers (<c>on*</c>), <c>javascript:</c> and other URLs with a scheme that is not allowed,
/// CSS with <c>url()</c> or <c>expression()</c>, <c>svg</c>, <c>math</c>, <c>style</c>, <c>template</c> and the
/// other tags whose content a browser does not read as HTML are never written.</item>
/// <item>A tag that is not allowed is left out and its text stays; script, style, forms controls, frames, media
/// and the like are left out with their content (<see cref="SanitizeOptions.RemovedWithContent"/>).</item>
/// <item>With a page URL (<c>new HtmlParser(html, url)</c>) relative links and images are made absolute.</item>
/// </list>
/// One pass over the tags as the parser recorded them; the time grows linearly with the size of the HTML.
/// </remarks>
/// <example>
/// <code>
/// string safe = new HtmlParser(userHtml).Sanitize();
///
/// var options = new SanitizeOptions { LinkRel = "nofollow noopener" };
/// options.AllowedAttributes.Remove("style");
/// string comment = HtmlText.Sanitize(userHtml, options);
/// </code>
/// </example>
public static class ParserTagSanitizeExtensions
{
    /// <summary>
    /// The tag with its content, sanitized. The tag itself is kept only when it is allowed (a <c>body</c>
    /// gives its content). Null for a null tag.
    /// </summary>
    [return: NotNullIfNotNull(nameof(tag))]
    public static string? Sanitize(this ParserTag? tag, SanitizeOptions? options = null)
    {
        if (tag == null) return null;

        var document = tag.Document;
        var writer = new SanitizerWriter(document, new SanitizerPolicy(options ?? SanitizeOptions.Default), tag.End - tag.Start + 1);
        try
        {
            writer.WriteTag(tag.Index);
            return writer.ToString();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>The whole document, sanitized. Null for a null document.</summary>
    [return: NotNullIfNotNull(nameof(document))]
    public static string? Sanitize(this HtmlParser? document, SanitizeOptions? options = null)
    {
        if (document == null) return null;

        // A page that starts with "<?xml" is parsed as XML, without the rules by which HTML closes tags (a <p> before
        // a <div>). A browser reads the result as HTML, and there "<?xml ...>" is a comment: so is "<!xml ...>",
        // which keeps the parser in HTML.
        var source = document.Source;
        int start = 0;
        while (start < source.Length && (char.IsWhiteSpace(source[start]) || source[start] == (char)0xFEFF)) start++;
        if (source.Slice(start).StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            string html = string.Concat(source.Slice(0, start + 1), "!", source.Slice(start + 2));
            using var asHtml = new HtmlParser(html, document.PageUrl.Length > 0 ? document.PageUrl : null);
            return asHtml.Sanitize(options);
        }

        var writer = new SanitizerWriter(document, new SanitizerPolicy(options ?? SanitizeOptions.Default), document.Source.Length);
        try
        {
            writer.WriteDocument();
            return writer.ToString();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
