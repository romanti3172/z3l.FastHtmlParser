namespace z3l.FastHtmlParser.Shortcuts;

/// <summary>
/// One-call getters of common attributes: shortcuts for
/// <see cref="ParserTagAttributeExtensions.AttributeByName(ParserTag?, HtmlAttribute)"/>.
/// Values are decoded; a missing attribute gives an empty string.
/// </summary>
public static class AttributeShortcuts
{
    /// <summary>Decoded value of the alt attribute, or an empty string.</summary>
    public static string AttributeAlt(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Alt);

    /// <summary>Decoded value of the class attribute, or an empty string.</summary>
    public static string AttributeClass(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Class);

    /// <summary>Decoded value of the content attribute, or an empty string.</summary>
    public static string AttributeContent(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Content);

    /// <summary>Decoded value of the id attribute, or an empty string.</summary>
    public static string AttributeId(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Id);

    /// <summary>Decoded value of the itemprop attribute, or an empty string.</summary>
    public static string AttributeItemProp(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Itemprop);

    /// <summary>Decoded value of the name attribute, or an empty string.</summary>
    public static string AttributeName(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Name);

    /// <summary>Decoded value of the style attribute, or an empty string.</summary>
    public static string AttributeStyle(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Style);

    /// <summary>Decoded value of the title attribute, or an empty string.</summary>
    public static string AttributeTitle(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Title);

    /// <summary>Decoded value of the value attribute, or an empty string.</summary>
    public static string AttributeValue(this ParserTag? tag) => tag.AttributeByName(HtmlAttribute.Value);
}
