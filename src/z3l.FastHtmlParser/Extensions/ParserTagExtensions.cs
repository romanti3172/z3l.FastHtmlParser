using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>Source, inner HTML and text of a tag.</summary>
public static class ParserTagExtensions
{
    /// <summary>
    /// Visible text of the tag in one line, entities decoded, as jsoup's <c>text()</c>: tags, comments and the content of
    /// script, style, noscript, template, noframes, noembed removed; &lt;br&gt;, block elements and table cells separate
    /// words ("a&lt;br&gt;b" → "a b"), inline tags do not ("a&lt;b&gt;b&lt;/b&gt;" → "ab"); whitespace, line breaks included,
    /// collapsed to one space and trimmed. The content of script, style, textarea, title, ... itself is its text as written.
    /// For the line structure use <see cref="TextLineBreaks"/>.
    /// </summary>
    public static string? Text(this ParserTag? tag)
    {
        if (tag == null) return default;

        var body = SpanHandlingCore.SpanBody(tag);

        bool leaf = !tag.HasChildren();

        // script, style, textarea, title, ... without child tags: their content is text as written.
        if (leaf && ParsingCore.IsRawText(tag.Key))
        {
            return HtmlDecodeCore.NeedsDecoding(body) ? HtmlDecodeCore.HtmlDecode(body) : body.ToString();
        }

        // No child tags, no '<' and no whitespace to collapse (the body is trimmed): the body is the text, decoded.
        // Otherwise ("a\n  b", a stray end tag "x</span>", "a < b") the full path.
        bool plain = leaf && !body.Contains('<');
        if (plain && !HtmlToTextCore.NeedsCollapse(body))
        {
            return HtmlDecodeCore.NeedsDecoding(body) ? HtmlDecodeCore.HtmlDecode(body) : body.ToString();
        }

        // <pre> and what is in it keep their whitespace, as in jsoup (looked up only when there is whitespace to keep)
        bool inPre = InPre(tag);
        if (plain && inPre)
        {
            return HtmlDecodeCore.NeedsDecoding(body) ? HtmlDecodeCore.HtmlDecode(body) : body.ToString();
        }

        // a tag with only text that has line breaks or double spaces: collapsed without looking for markup
        if (plain) return HtmlToTextCore.CollapseText(body);

        return HtmlToTextCore.ClearTags(body, inPre);
    }

    /// <summary>Whether the tag is &lt;pre&gt;, &lt;listing&gt; or &lt;plaintext&gt;, or inside one.</summary>
    internal static bool InPre(ParserTag tag)
    {
        if (!tag.Document.HasPre) return false;
        for (ParserTag? t = tag; t != null; t = t.Parent())
        {
            if (t.Key is (int)HtmlTag.Pre or (int)HtmlTag.Listing or (int)HtmlTag.Plaintext) return true;
        }
        return false;
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
