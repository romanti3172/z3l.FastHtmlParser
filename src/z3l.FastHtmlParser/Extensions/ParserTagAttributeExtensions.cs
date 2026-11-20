using z3l.FastHtmlParser.Core;
using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser;

/// <summary>
/// Attribute values of a tag. Values are returned decoded, the way a browser reads them:
/// character references are replaced (<c>title="It&amp;#39;s"</c> → <c>It's</c>, <c>&amp;amp;</c> → <c>&amp;</c>),
/// with the HTML5 attribute rules (<c>?a=1&amp;copy=2</c> stays as is).
/// Use <see cref="AttributeBody(ParserTag?, string)"/> for the value exactly as written in the source.
/// Links (<see cref="AttributeHref"/>, <see cref="AttributeSrc"/>) are decoded the same way and resolved against the page URL.
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
    /// All attributes of the tag in source order, as a browser reads them: names in lower case, values decoded,
    /// and of attributes with the same name only the first one (<c>&lt;a id=1 ID=2&gt;</c> has one id, <c>1</c>).
    /// Empty for a null tag, a comment, a doctype or an XML declaration.
    /// </summary>
    public static IReadOnlyList<TagAttribute> Attributes(this ParserTag? tag)
    {
        if (tag == null || !XNav.IsElement(tag)) return [];

        var document = tag.Document;
        var source = document.Source;
        var scanner = new AttributeScanner(tag);
        List<TagAttribute>? list = null;

        while (scanner.Next(out var nameStart, out var nameLength, out var valueStart, out var valueLength))
        {
            var name = source.Slice(nameStart, nameLength);
            if (list != null && Contains(list, name)) continue;

            var raw = valueStart < 0 ? default : source.Slice(valueStart, valueLength);
            (list ??= new List<TagAttribute>(4)).Add(new TagAttribute(
                ParsingCore.LowerAttributeName(name), SpanHandlingCore.DecodeAttribute(raw), document, valueStart, valueLength));
        }

        return list ?? (IReadOnlyList<TagAttribute>)[];

        static bool Contains(List<TagAttribute> list, ReadOnlySpan<char> name)
        {
            foreach (var attribute in list)
            {
                if (name.Equals(attribute.Name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The href as the link a browser follows: character references decoded (<c>&amp;amp;</c> → <c>&amp;</c>) and
    /// made absolute against <paramref name="currentUrl"/> (or the page URL). Percent-escapes are kept as written
    /// (<c>%3F</c> in a path is not a <c>?</c>). A tag without href returns the page URL.
    /// </summary>
    public static string? AttributeHref(this ParserTag? tag, string? currentUrl = null)
    {
        if (tag == null) return default;

        return UriCore.CorrectLink(SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Href), tag.Document, currentUrl);
    }

    /// <summary>
    /// The src as the link a browser loads: character references decoded (<c>&amp;amp;</c> → <c>&amp;</c>) and
    /// made absolute against <paramref name="currentUrl"/> (or the page URL). Percent-escapes are kept as written.
    /// A tag without src returns the page URL.
    /// </summary>
    public static string? AttributeSrc(this ParserTag? tag, string? currentUrl = null)
    {
        if (tag == null) return default;

        return UriCore.CorrectLink(SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Src), tag.Document, currentUrl);
    }

    /// <summary>The address from a mailto: href (character references decoded, as <see cref="AttributeHref"/>), or null.</summary>
    public static string? AttributeHrefMailTo(this ParserTag? tag)
    {
        var result = DecodedHref(tag).Trim();
        if (result.StartsWith("mailto", StringComparison.OrdinalIgnoreCase))
        {
            return SpanHandlingCore.CopyFrom(result, ':').ToString();
        }
        return default;
    }

    // The raw span when there is nothing to decode: no allocation.
    private static ReadOnlySpan<char> DecodedHref(ParserTag? tag)
    {
        var raw = SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Href);
        return HtmlDecodeCore.NeedsDecoding(raw) ? SpanHandlingCore.DecodeAttribute(raw) : raw;
    }

    /// <summary>The number from a tel: href (character references decoded, as <see cref="AttributeHref"/>), or null.</summary>
    public static string? AttributeHrefTelephone(this ParserTag? tag)
    {
        var result = DecodedHref(tag).Trim();
        if (result.StartsWith("tel", StringComparison.OrdinalIgnoreCase))
        {
            return SpanHandlingCore.CopyFrom(result, ':').ToString();
        }
        return default;
    }
}
