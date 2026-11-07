namespace z3l.FastHtmlParser.Shortcuts;

/// <summary>
/// One-call searches for all matching tags: shortcuts for
/// <see cref="ParserTagSearchExtensions.FindAll(ITagSearchable?, TagQuery)"/>.
/// </summary>
public static class FindAllShortcuts
{
    /// <summary>All tags with the given name.</summary>
    public static IEnumerable<ParserTag> FindAllByName(this ITagSearchable? tags, HtmlTag name)
        => tags.FindAll(new TagQuery(name));

    /// <summary>All tags with the given name (case-insensitive).</summary>
    public static IEnumerable<ParserTag> FindAllByName(this ITagSearchable? tags, string name)
        => tags.FindAll(new TagQuery(name));

    /// <summary>All tags with the given name whose decoded attribute value equals the given value.</summary>
    public static IEnumerable<ParserTag> FindAllByNameAttribute(this ITagSearchable? tags, HtmlTag name, HtmlAttribute attributeName, string attributeValue)
        => tags.FindAll(new TagQuery(name) { Attribute = attributeName, AttributeValue = attributeValue });

    /// <inheritdoc cref="FindAllByNameAttribute(ITagSearchable?, HtmlTag, HtmlAttribute, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameAttribute(this ITagSearchable? tags, HtmlTag name, string attributeName, string attributeValue)
        => tags.FindAll(new TagQuery(name) { AttributeName = attributeName, AttributeValue = attributeValue });

    /// <inheritdoc cref="FindAllByNameAttribute(ITagSearchable?, HtmlTag, HtmlAttribute, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameAttribute(this ITagSearchable? tags, string name, HtmlAttribute attributeName, string attributeValue)
        => tags.FindAll(new TagQuery(name) { Attribute = attributeName, AttributeValue = attributeValue });

    /// <inheritdoc cref="FindAllByNameAttribute(ITagSearchable?, HtmlTag, HtmlAttribute, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameAttribute(this ITagSearchable? tags, string name, string attributeName, string attributeValue)
        => tags.FindAll(new TagQuery(name) { AttributeName = attributeName, AttributeValue = attributeValue });

    /// <summary>All tags with the given name that have the class (one of their space-separated classes, case-insensitive).</summary>
    public static IEnumerable<ParserTag> FindAllByNameClass(this ITagSearchable? tags, HtmlTag name, string classValue)
        => tags.FindAll(new TagQuery(name) { Class = classValue });

    /// <inheritdoc cref="FindAllByNameClass(ITagSearchable?, HtmlTag, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameClass(this ITagSearchable? tags, string name, string classValue)
        => tags.FindAll(new TagQuery(name) { Class = classValue });

    /// <summary>All tags with the given name and id.</summary>
    public static IEnumerable<ParserTag> FindAllByNameId(this ITagSearchable? tags, HtmlTag name, string id)
        => tags.FindAll(new TagQuery(name) { Id = id });

    /// <inheritdoc cref="FindAllByNameId(ITagSearchable?, HtmlTag, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameId(this ITagSearchable? tags, string name, string id)
        => tags.FindAll(new TagQuery(name) { Id = id });

    /// <summary>All tags with the given name whose id starts with the given value.</summary>
    public static IEnumerable<ParserTag> FindAllByNameIdStartsWith(this ITagSearchable? tags, HtmlTag name, string idStartsWith)
        => tags.FindAll(new TagQuery(name) { IdStartsWith = idStartsWith });

    /// <inheritdoc cref="FindAllByNameIdStartsWith(ITagSearchable?, HtmlTag, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameIdStartsWith(this ITagSearchable? tags, string name, string idStartsWith)
        => tags.FindAll(new TagQuery(name) { IdStartsWith = idStartsWith });

    /// <summary>All tags with the given name and itemprop attribute.</summary>
    public static IEnumerable<ParserTag> FindAllByNameItemprop(this ITagSearchable? tags, HtmlTag name, string itemprop)
        => tags.FindAll(new TagQuery(name) { Attribute = HtmlAttribute.Itemprop, AttributeValue = itemprop });

    /// <inheritdoc cref="FindAllByNameItemprop(ITagSearchable?, HtmlTag, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameItemprop(this ITagSearchable? tags, string name, string itemprop)
        => tags.FindAll(new TagQuery(name) { Attribute = HtmlAttribute.Itemprop, AttributeValue = itemprop });

    /// <summary>All tags with the given name whose inner HTML starts with the given value.</summary>
    public static IEnumerable<ParserTag> FindAllByNameBodyStartsWith(this ITagSearchable? tags, HtmlTag name, string bodyStartsWith)
        => tags.FindAll(new TagQuery(name) { BodyStartsWith = bodyStartsWith });

    /// <inheritdoc cref="FindAllByNameBodyStartsWith(ITagSearchable?, HtmlTag, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameBodyStartsWith(this ITagSearchable? tags, string name, string bodyStartsWith)
        => tags.FindAll(new TagQuery(name) { BodyStartsWith = bodyStartsWith });

    /// <summary>Tags of the sequence on the same depth as its first tag; on a tag: its direct children (see also <c>Children()</c>).</summary>
    public static IEnumerable<ParserTag> FindAllByFirstLevel(this IEnumerable<ParserTag>? details)
    {
        var level = details?.FirstOrDefault()?.Depth ?? 0;
        return details?.Where(x => x.Depth == level) ?? [];
    }

    /// <summary>Tags with the given name on the same depth as the first tag of the sequence; on a tag: direct children with that name.</summary>
    public static IEnumerable<ParserTag> FindAllByNameFirstLevel(this IEnumerable<ParserTag>? details, HtmlTag name)
    {
        if (details == null) return [];
        var level = details.FirstOrDefault()?.Depth ?? 0;
        return details.Where(x => x.IsName(name) && x.Depth == level);
    }

    /// <inheritdoc cref="FindAllByNameFirstLevel(IEnumerable{ParserTag}?, HtmlTag)"/>
    public static IEnumerable<ParserTag> FindAllByNameFirstLevel(this IEnumerable<ParserTag>? details, string name)
    {
        if (details == null) return [];
        var level = details.FirstOrDefault()?.Depth ?? 0;
        return details.Where(x => x.IsName(name) && x.Depth == level);
    }

    /// <summary>Tags with the given name and class on the same depth as the first tag of the sequence; on a tag: matching direct children.</summary>
    public static IEnumerable<ParserTag> FindAllByNameClassFirstLevel(this IEnumerable<ParserTag>? details, HtmlTag name, string classValue)
    {
        if (details == null) return [];
        var level = details.FirstOrDefault()?.Depth ?? 0;
        return details.Where(x => x.Is(new TagQuery(name) { Class = classValue }) && x.Depth == level);
    }

    /// <inheritdoc cref="FindAllByNameClassFirstLevel(IEnumerable{ParserTag}?, HtmlTag, string)"/>
    public static IEnumerable<ParserTag> FindAllByNameClassFirstLevel(this IEnumerable<ParserTag>? details, string name, string classValue)
    {
        if (details == null) return [];
        var level = details.FirstOrDefault()?.Depth ?? 0;
        return details.Where(x => x.Is(new TagQuery(name) { Class = classValue }) && x.Depth == level);
    }
}
