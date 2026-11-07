namespace z3l.FastHtmlParser.Shortcuts;

/// <summary>
/// One-call checks of a tag: shortcuts for <see cref="ParserTagMatchExtensions.Is(ParserTag?, in TagQuery)"/>.
/// </summary>
public static class IsShortcuts
{
    /// <summary>Whether the tag has the given name and the decoded attribute value equals the given value.</summary>
    public static bool IsNameAttribute(this ParserTag? tag, HtmlTag tagName, HtmlAttribute attributeName, string value)
        => tag.Is(new TagQuery(tagName) { Attribute = attributeName, AttributeValue = value });

    /// <inheritdoc cref="IsNameAttribute(ParserTag?, HtmlTag, HtmlAttribute, string)"/>
    public static bool IsNameAttribute(this ParserTag? tag, HtmlTag tagName, string attributeName, string value)
        => tag.Is(new TagQuery(tagName) { AttributeName = attributeName, AttributeValue = value });

    /// <inheritdoc cref="IsNameAttribute(ParserTag?, HtmlTag, HtmlAttribute, string)"/>
    public static bool IsNameAttribute(this ParserTag? tag, string name, HtmlAttribute attributeName, string attributeValue)
        => tag.Is(new TagQuery(name) { Attribute = attributeName, AttributeValue = attributeValue });

    /// <inheritdoc cref="IsNameAttribute(ParserTag?, HtmlTag, HtmlAttribute, string)"/>
    public static bool IsNameAttribute(this ParserTag? tag, string name, string attributeName, string attributeValue)
        => tag.Is(new TagQuery(name) { AttributeName = attributeName, AttributeValue = attributeValue });

    /// <summary>Whether the tag has the given name and its inner HTML equals the given value.</summary>
    public static bool IsNameBody(this ParserTag? tag, HtmlTag tagName, string body)
        => tag.Is(new TagQuery(tagName) { Body = body });

    /// <inheritdoc cref="IsNameBody(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameBody(this ParserTag? tag, string name, string body)
        => tag.Is(new TagQuery(name) { Body = body });

    /// <summary>Whether the tag has the given name and its visible text equals the given value.</summary>
    public static bool IsNameText(this ParserTag? tag, HtmlTag tagName, string text)
        => tag.Is(new TagQuery(tagName) { Text = text });

    /// <inheritdoc cref="IsNameText(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameText(this ParserTag? tag, string name, string text)
        => tag.Is(new TagQuery(name) { Text = text });

    /// <summary>Whether the tag has the given name and class.</summary>
    public static bool IsNameClass(this ParserTag? tag, HtmlTag tagName, string classValue)
        => tag.Is(new TagQuery(tagName) { Class = classValue });

    /// <inheritdoc cref="IsNameClass(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameClass(this ParserTag? tag, string name, string classValue)
        => tag.Is(new TagQuery(name) { Class = classValue });

    /// <summary>Whether the tag has the given name and class and its inner HTML equals the given value.</summary>
    public static bool IsNameClassBody(this ParserTag? tag, HtmlTag tagName, string classValue, string body)
        => tag.Is(new TagQuery(tagName) { Class = classValue, Body = body });

    /// <inheritdoc cref="IsNameClassBody(ParserTag?, HtmlTag, string, string)"/>
    public static bool IsNameClassBody(this ParserTag? tag, string name, string classValue, string body)
        => tag.Is(new TagQuery(name) { Class = classValue, Body = body });

    /// <summary>Whether the tag has the given name and the given class, id and inner HTML; null or empty criteria are not checked.</summary>
    public static bool IsNameClassIdBody(this ParserTag? tag, HtmlTag tagName, string? classValue = null, string? id = null, string? body = null)
        => tag.Is(new TagQuery(tagName) { Class = Shortcut.NullIfEmpty(classValue), Id = Shortcut.NullIfEmpty(id), Body = Shortcut.NullIfEmpty(body) });

    /// <summary>Whether the tag has the given name, class, id and inner HTML; null or empty criteria are not checked.</summary>
    public static bool IsNameClassIdBody(this ParserTag? tag, string? name = null, string? classValue = null, string? id = null, string? body = null)
        => tag.Is(new TagQuery(Shortcut.NullIfEmpty(name)) { Class = Shortcut.NullIfEmpty(classValue), Id = Shortcut.NullIfEmpty(id), Body = Shortcut.NullIfEmpty(body) });

    /// <summary>Whether the tag has the given name and the given class and visible text; null or empty criteria are not checked.</summary>
    public static bool IsNameClassText(this ParserTag? tag, HtmlTag tagName, string? classValue = null, string? text = null)
        => tag.Is(new TagQuery(tagName) { Class = Shortcut.NullIfEmpty(classValue), Text = Shortcut.NullIfEmpty(text) });

    /// <summary>Whether the tag has the given name, class and visible text; null or empty criteria are not checked.</summary>
    public static bool IsNameClassText(this ParserTag? tag, string? name = null, string? classValue = null, string? text = null)
        => tag.Is(new TagQuery(Shortcut.NullIfEmpty(name)) { Class = Shortcut.NullIfEmpty(classValue), Text = Shortcut.NullIfEmpty(text) });

    /// <summary>Whether the tag has the given name and the given id and visible text; null or empty criteria are not checked.</summary>
    public static bool IsNameIdText(this ParserTag? tag, HtmlTag tagName, string? id = null, string? text = null)
        => tag.Is(new TagQuery(tagName) { Id = Shortcut.NullIfEmpty(id), Text = Shortcut.NullIfEmpty(text) });

    /// <summary>Whether the tag has the given name, id and visible text; null or empty criteria are not checked.</summary>
    public static bool IsNameIdText(this ParserTag? tag, string? name = null, string? id = null, string? text = null)
        => tag.Is(new TagQuery(Shortcut.NullIfEmpty(name)) { Id = Shortcut.NullIfEmpty(id), Text = Shortcut.NullIfEmpty(text) });

    /// <summary>Whether the tag has the given name and id.</summary>
    public static bool IsNameId(this ParserTag? tag, HtmlTag tagName, string id)
        => tag.Is(new TagQuery(tagName) { Id = id });

    /// <inheritdoc cref="IsNameId(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameId(this ParserTag? tag, string name, string id)
        => tag.Is(new TagQuery(name) { Id = id });

    /// <summary>Whether the tag has the given name and its visible text starts with the given value.</summary>
    public static bool IsNameTextStartsWith(this ParserTag? tag, HtmlTag tagName, string bodyStartsWith)
        => tag.Is(new TagQuery(tagName) { TextStartsWith = bodyStartsWith });

    /// <inheritdoc cref="IsNameTextStartsWith(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameTextStartsWith(this ParserTag? tag, string name, string bodyStartsWith)
        => tag.Is(new TagQuery(name) { TextStartsWith = bodyStartsWith });

    /// <summary>Whether the tag has the given name and its inner HTML starts with the given value.</summary>
    public static bool IsNameBodyStartsWith(this ParserTag? tag, HtmlTag name, string bodyStartsWith)
        => tag.Is(new TagQuery(name) { BodyStartsWith = bodyStartsWith });

    /// <inheritdoc cref="IsNameBodyStartsWith(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameBodyStartsWith(this ParserTag? tag, string name, string bodyStartsWith)
        => tag.Is(new TagQuery(name) { BodyStartsWith = bodyStartsWith });

    /// <summary>Whether the tag has the given name and class and its visible text starts with the given value.</summary>
    public static bool IsNameClassTextStartsWith(this ParserTag? tag, HtmlTag tagName, string classValue, string bodyValueStartsFrom)
        => tag.Is(new TagQuery(tagName) { Class = classValue, TextStartsWith = bodyValueStartsFrom });

    /// <inheritdoc cref="IsNameClassTextStartsWith(ParserTag?, HtmlTag, string, string)"/>
    public static bool IsNameClassTextStartsWith(this ParserTag? tag, string name, string classValue, string bodyValueStartsFrom)
        => tag.Is(new TagQuery(name) { Class = classValue, TextStartsWith = bodyValueStartsFrom });

    /// <summary>Whether the tag has the given name and its id starts with the given value.</summary>
    public static bool IsNameIdStartsWith(this ParserTag? tag, HtmlTag tagName, string idStartsFrom)
        => tag.Is(new TagQuery(tagName) { IdStartsWith = idStartsFrom });

    /// <inheritdoc cref="IsNameIdStartsWith(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameIdStartsWith(this ParserTag? tag, string name, string idStartsFrom)
        => tag.Is(new TagQuery(name) { IdStartsWith = idStartsFrom });

    /// <summary>Whether the tag has the given name and itemprop attribute.</summary>
    public static bool IsNameItemprop(this ParserTag? tag, HtmlTag tagName, string itemprop)
        => tag.Is(new TagQuery(tagName) { Attribute = HtmlAttribute.Itemprop, AttributeValue = itemprop });

    /// <inheritdoc cref="IsNameItemprop(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameItemprop(this ParserTag? tag, string name, string itemprop)
        => tag.Is(new TagQuery(name) { Attribute = HtmlAttribute.Itemprop, AttributeValue = itemprop });

    /// <summary>Whether the tag has the given name and its opening tag source equals the given value.</summary>
    public static bool IsNameSource(this ParserTag? tag, HtmlTag tagName, string source)
        => tag.Is(new TagQuery(tagName) { Source = source });

    /// <inheritdoc cref="IsNameSource(ParserTag?, HtmlTag, string)"/>
    public static bool IsNameSource(this ParserTag? tag, string name, string source)
        => tag.Is(new TagQuery(name) { Source = source });
}
