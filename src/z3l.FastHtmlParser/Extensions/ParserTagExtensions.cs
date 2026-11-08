using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>Source, inner HTML and text of a tag.</summary>
public static class ParserTagExtensions
{
    /// <summary>
    /// Visible text of the tag with entities decoded. With child tags: tags, comments and the content of
    /// script, style, noscript, template, noframes, noembed removed, whitespace collapsed; without: the inner text as is (trimmed).
    /// </summary>
    public static string? Text(this ParserTag? tag)
    {
        if (tag == null) return default;

        var body = SpanHandlingCore.SpanBody(tag);

        // No child tags and no '<' at all: the body is the text. A '<' without a tag of its own
        // (a stray end tag "x</span>", or "a < b") still goes through ClearTags,
        // except in script, style, textarea, title, ...: their content is text as written.
        if (!tag.HasChildren() && (!body.Contains('<') || ParsingCore.IsRawText(tag.Key)))
        {
            return body.Contains('&')
                ? HtmlDecodeCore.HtmlDecode(body)
                : body.ToString();
        }

        return HtmlToTextCore.ClearTags(body);
    }
    /// <summary>Text of the tag with line breaks kept: &lt;br&gt; and block elements → new line, p/h1–h6 → paragraph.</summary>
    public static string? TextLineBreaks(this ParserTag? tag)
    {
        return ParsingCore.ClearTagsLineBreaks(SpanHandlingCore.SpanBody(tag));
    }
    /// <summary>Inner HTML of the tag (trimmed); empty for unclosed tags.</summary>
    public static string? Body(this ParserTag? tag)
    {
        return SpanHandlingCore.SpanBody(tag).ToString();
    }
    /// <summary>Source of the opening tag without '&lt;' and '&gt;': the name and the attributes.</summary>
    public static string Source(this ParserTag? tag)
    {
        return SpanHandlingCore.SpanSource(tag).ToString();
    }
    /// <summary>Outer HTML: from the opening '&lt;' to the closing '&gt;' (trimmed); empty for unclosed tags.</summary>
    public static string FullSource(this ParserTag? tag)
    {
        return SpanHandlingCore.SpanFullSource(tag).ToString();
    }
}
