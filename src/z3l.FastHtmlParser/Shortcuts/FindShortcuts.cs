namespace z3l.FastHtmlParser.Shortcuts;

/// <summary>
/// One-call searches for the first matching tag: shortcuts for
/// <see cref="ParserTagSearchExtensions.Find(ITagSearchable?, in TagQuery)"/>.
/// </summary>
public static class FindShortcuts
{
    /// <summary>First tag with the given id.</summary>
    public static ParserTag? FindById(this ITagSearchable? tags, string? id)
        => string.IsNullOrEmpty(id) ? null : tags.Find(new TagQuery { Id = id });

    /// <summary>First tag with the given name.</summary>
    public static ParserTag? FindByName(this ITagSearchable? tags, HtmlTag name)
        => tags.Find(new TagQuery(name));

    /// <summary>First tag with the given name (case-insensitive).</summary>
    public static ParserTag? FindByName(this ITagSearchable? tags, string? name)
        => string.IsNullOrEmpty(name) ? null : tags.Find(new TagQuery(name));

    /// <summary>First tag with the given name whose inner HTML equals <paramref name="body"/>.</summary>
    public static ParserTag? FindByNameBody(this ITagSearchable? tags, HtmlTag name, string body)
        => tags.Find(new TagQuery(name) { Body = body });

    /// <inheritdoc cref="FindByNameBody(ITagSearchable?, HtmlTag, string)"/>
    public static ParserTag? FindByNameBody(this ITagSearchable? tags, string name, string body)
        => tags.Find(new TagQuery(name) { Body = body });

    /// <summary>First tag with the given name whose decoded attribute value equals the given value.</summary>
    public static ParserTag? FindByNameAttribute(this ITagSearchable? tags, HtmlTag name, HtmlAttribute attributeName, string attributeValue)
        => tags.Find(new TagQuery(name) { Attribute = attributeName, AttributeValue = attributeValue });

    /// <inheritdoc cref="FindByNameAttribute(ITagSearchable?, HtmlTag, HtmlAttribute, string)"/>
    public static ParserTag? FindByNameAttribute(this ITagSearchable? tags, HtmlTag name, string attributeName, string attributeValue)
        => tags.Find(new TagQuery(name) { AttributeName = attributeName, AttributeValue = attributeValue });

    /// <inheritdoc cref="FindByNameAttribute(ITagSearchable?, HtmlTag, HtmlAttribute, string)"/>
    public static ParserTag? FindByNameAttribute(this ITagSearchable? tags, string name, HtmlAttribute attributeName, string attributeValue)
        => tags.Find(new TagQuery(name) { Attribute = attributeName, AttributeValue = attributeValue });

    /// <inheritdoc cref="FindByNameAttribute(ITagSearchable?, HtmlTag, HtmlAttribute, string)"/>
    public static ParserTag? FindByNameAttribute(this ITagSearchable? tags, string name, string attributeName, string attributeValue)
        => tags.Find(new TagQuery(name) { AttributeName = attributeName, AttributeValue = attributeValue });

    /// <summary>First tag with the given name that has the class (one of its space-separated classes, case-insensitive).</summary>
    public static ParserTag? FindByNameClass(this ITagSearchable? tags, HtmlTag name, string classValue)
        => tags.Find(new TagQuery(name) { Class = classValue });

    /// <inheritdoc cref="FindByNameClass(ITagSearchable?, HtmlTag, string)"/>
    public static ParserTag? FindByNameClass(this ITagSearchable? tags, string name, string classValue)
        => tags.Find(new TagQuery(name) { Class = classValue });

    /// <summary>First tag with the given name and class whose inner HTML equals <paramref name="body"/>.</summary>
    public static ParserTag? FindByNameClassBody(this ITagSearchable? tags, HtmlTag name, string classValue, string body)
        => tags.Find(new TagQuery(name) { Class = classValue, Body = body });

    /// <inheritdoc cref="FindByNameClassBody(ITagSearchable?, HtmlTag, string, string)"/>
    public static ParserTag? FindByNameClassBody(this ITagSearchable? tags, string name, string classValue, string body)
        => tags.Find(new TagQuery(name) { Class = classValue, Body = body });

    /// <summary>First tag with the given name, class and visible text; an empty class or text is not checked.</summary>
    public static ParserTag? FindByNameClassText(this ITagSearchable? tags, HtmlTag name, string classValue, string text)
        => tags.Find(new TagQuery(name) { Class = Shortcut.NullIfEmpty(classValue), Text = Shortcut.NullIfEmpty(text) });

    /// <summary>First tag with the given name, class and visible text; an empty name, class or text is not checked.</summary>
    public static ParserTag? FindByNameClassText(this ITagSearchable? tags, string name, string classValue, string text)
        => tags.Find(new TagQuery(Shortcut.NullIfEmpty(name)) { Class = Shortcut.NullIfEmpty(classValue), Text = Shortcut.NullIfEmpty(text) });

    /// <summary>First tag with the given name, id and visible text; an empty id or text is not checked.</summary>
    public static ParserTag? FindByNameIdText(this ITagSearchable? tags, HtmlTag name, string id, string text)
        => tags.Find(new TagQuery(name) { Id = Shortcut.NullIfEmpty(id), Text = Shortcut.NullIfEmpty(text) });

    /// <summary>First tag with the given name, id and visible text; an empty name, id or text is not checked.</summary>
    public static ParserTag? FindByNameIdText(this ITagSearchable? tags, string name, string id, string text)
        => tags.Find(new TagQuery(Shortcut.NullIfEmpty(name)) { Id = Shortcut.NullIfEmpty(id), Text = Shortcut.NullIfEmpty(text) });

    /// <summary>First tag with the given name whose visible text equals <paramref name="text"/>.</summary>
    public static ParserTag? FindByNameText(this ITagSearchable? tags, HtmlTag name, string text)
        => tags.Find(new TagQuery(name) { Text = text });

    /// <inheritdoc cref="FindByNameText(ITagSearchable?, HtmlTag, string)"/>
    public static ParserTag? FindByNameText(this ITagSearchable? tags, string name, string text)
        => tags.Find(new TagQuery(name) { Text = text });

    /// <summary>First tag with the given name and id.</summary>
    public static ParserTag? FindByNameId(this ITagSearchable? tags, HtmlTag name, string id)
        => tags.Find(new TagQuery(name) { Id = id });

    /// <inheritdoc cref="FindByNameId(ITagSearchable?, HtmlTag, string)"/>
    public static ParserTag? FindByNameId(this ITagSearchable? tags, string name, string id)
        => tags.Find(new TagQuery(name) { Id = id });

    /// <summary>First tag with the given name whose id starts with the given value.</summary>
    public static ParserTag? FindByNameIdStartsWith(this ITagSearchable? tags, HtmlTag name, string idStartsWith)
        => tags.Find(new TagQuery(name) { IdStartsWith = idStartsWith });

    /// <inheritdoc cref="FindByNameIdStartsWith(ITagSearchable?, HtmlTag, string)"/>
    public static ParserTag? FindByNameIdStartsWith(this ITagSearchable? tags, string name, string idStartsWith)
        => tags.Find(new TagQuery(name) { IdStartsWith = idStartsWith });

    /// <summary>First tag with the given name whose inner HTML starts with the given value.</summary>
    public static ParserTag? FindByNameBodyStartsWith(this ITagSearchable? tags, HtmlTag name, string bodyStartsWith)
        => tags.Find(new TagQuery(name) { BodyStartsWith = bodyStartsWith });

    /// <inheritdoc cref="FindByNameBodyStartsWith(ITagSearchable?, HtmlTag, string)"/>
    public static ParserTag? FindByNameBodyStartsWith(this ITagSearchable? tags, string name, string bodyStartsWith)
        => tags.Find(new TagQuery(name) { BodyStartsWith = bodyStartsWith });

    /// <summary>First tag with the given name whose visible text starts with the given value; an empty value finds nothing.</summary>
    public static ParserTag? FindByNameTextStartsWith(this ITagSearchable? tags, HtmlTag name, string textStartsWith)
        => string.IsNullOrEmpty(textStartsWith) ? null : tags.Find(new TagQuery(name) { TextStartsWith = textStartsWith });

    /// <inheritdoc cref="FindByNameTextStartsWith(ITagSearchable?, HtmlTag, string)"/>
    public static ParserTag? FindByNameTextStartsWith(this ITagSearchable? tags, string name, string textStartsWith)
        => string.IsNullOrEmpty(textStartsWith) ? null : tags.Find(new TagQuery(name) { TextStartsWith = textStartsWith });

    /// <summary>First tag with the given name and class whose visible text starts with the given value; an empty value finds nothing.</summary>
    public static ParserTag? FindByNameClassTextStartsWith(this ITagSearchable? tags, HtmlTag tagName, string className, string textStartsWith)
        => string.IsNullOrEmpty(textStartsWith) ? null : tags.Find(new TagQuery(tagName) { Class = className, TextStartsWith = textStartsWith });

    /// <inheritdoc cref="FindByNameClassTextStartsWith(ITagSearchable?, HtmlTag, string, string)"/>
    public static ParserTag? FindByNameClassTextStartsWith(this ITagSearchable? tags, string tagName, string className, string textStartsWith)
        => string.IsNullOrEmpty(textStartsWith) ? null : tags.Find(new TagQuery(tagName) { Class = className, TextStartsWith = textStartsWith });

    /// <summary>First tag with the given name and itemprop attribute.</summary>
    public static ParserTag? FindByNameItemprop(this ITagSearchable? tags, HtmlTag name, string itemprop)
        => tags.Find(new TagQuery(name) { Attribute = HtmlAttribute.Itemprop, AttributeValue = itemprop });

    /// <inheritdoc cref="FindByNameItemprop(ITagSearchable?, HtmlTag, string)"/>
    public static ParserTag? FindByNameItemprop(this ITagSearchable? tags, string name, string itemprop)
        => tags.Find(new TagQuery(name) { Attribute = HtmlAttribute.Itemprop, AttributeValue = itemprop });
}

internal static class Shortcut
{
    /// <summary>The older "optional" shortcuts skip an empty criterion: in a <see cref="TagQuery"/> that is null.</summary>
    internal static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
