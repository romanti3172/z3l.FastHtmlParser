using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>
/// Attribute values of a tag. Values are returned decoded, the way a browser reads them:
/// character references are replaced (<c>title="It&amp;#39;s"</c> → <c>It's</c>, <c>&amp;amp;</c> → <c>&amp;</c>),
/// with the HTML5 attribute rules (<c>?a=1&amp;copy=2</c> stays as is).
/// Use <see cref="AttributeBody(ParserTag?, string)"/> for the value exactly as written in the source.
/// Links (<see cref="AttributeHref"/>, <see cref="AttributeSrc"/>) are not decoded, only resolved.
/// A missing attribute gives an empty string.
/// For one-call getters (<c>AttributeTitle()</c>, <c>AttributeClass()</c>, ...) add <c>using z3l.FastHtmlParser.Shortcuts;</c>.
/// </summary>
public static class ParserTagAttributeExtensions
{
    /// <summary>Decoded value of the given attribute, or an empty string.</summary>
    public static string AttributeByName(this ParserTag? tag, string attribute)
    {
        return SpanHandlingCore.AttributeValue(tag, attribute);
    }

    /// <summary>Decoded value of the given attribute, or an empty string.</summary>
    public static string AttributeByName(this ParserTag? tag, HtmlAttribute attribute)
    {
        return SpanHandlingCore.AttributeValue(tag, attribute);
    }

    /// <summary>
    /// Value of the given attribute exactly as written in the source, without decoding character references
    /// (<c>title="It&amp;#39;s"</c> → <c>It&amp;#39;s</c>), or an empty string.
    /// </summary>
    public static string AttributeBody(this ParserTag? tag, string attribute)
    {
        return SpanHandlingCore.SpanAttributeByName(tag, attribute).ToString();
    }

    /// <inheritdoc cref="AttributeBody(ParserTag?, string)"/>
    public static string AttributeBody(this ParserTag? tag, HtmlAttribute attribute)
    {
        return SpanHandlingCore.SpanAttributeByName(tag, attribute).ToString();
    }

    /// <summary>
    /// The href as a link: made absolute against <paramref name="currentUrl"/> (or the page URL),
    /// with only %3f, %3d and &amp;amp; unescaped; other character references are not decoded.
    /// A tag without href returns the page URL.
    /// </summary>
    public static string? AttributeHref(this ParserTag? tag, string? currentUrl = null)
    {
        if (tag == null) return default;

        return UriCore.CorrectLink(SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Href), tag.Document, currentUrl);
    }

    /// <summary>
    /// The src as a link: made absolute against <paramref name="currentUrl"/> (or the page URL),
    /// with only %3f, %3d and &amp;amp; unescaped; other character references are not decoded.
    /// A tag without src returns the page URL.
    /// </summary>
    public static string? AttributeSrc(this ParserTag? tag, string? currentUrl = null)
    {
        if (tag == null) return default;

        return UriCore.CorrectLink(SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Src), tag.Document, currentUrl);
    }

    /// <summary>The address from a mailto: href (not decoded, as <see cref="AttributeHref"/>), or null.</summary>
    public static string? AttributeHrefMailTo(this ParserTag? tag)
    {
        var result = SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Href).Trim();
        if (result.StartsWith("mailto", StringComparison.OrdinalIgnoreCase))
        {
            return SpanHandlingCore.CopyFrom(result, ':').ToString();
        }
        return default;
    }

    /// <summary>The number from a tel: href (not decoded, as <see cref="AttributeHref"/>), or null.</summary>
    public static string? AttributeHrefTelephone(this ParserTag? tag)
    {
        var result = SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Href).Trim();
        if (result.StartsWith("tel", StringComparison.OrdinalIgnoreCase))
        {
            return SpanHandlingCore.CopyFrom(result, ':').ToString();
        }
        return default;
    }
}
