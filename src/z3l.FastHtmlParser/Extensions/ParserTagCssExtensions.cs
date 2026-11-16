namespace z3l.FastHtmlParser;

/// <summary>
/// CSS selectors on a document or a tag, as <c>querySelectorAll</c> / <c>querySelector</c> / <c>matches</c>.
/// See <see cref="CssQuery"/> for what is supported. A <see langword="null"/> scope gives empty results;
/// an invalid selector throws <see cref="ArgumentException"/>.
/// </summary>
/// <example>
/// <code>
/// doc.Css("article.product_pod h3 > a");
/// doc.CssFirst("ul#menu > li:last-child a")?.AttributeHref();
/// doc.Css("tr:has(> td.price)");
/// card.Css("> span");                    // the children of card
/// tag.IsCss("a[href^='http']:not([rel~=nofollow])");
/// </code>
/// </example>
public static class ParserTagCssExtensions
{
    /// <summary>The tags that match <paramref name="selector"/>, in document order.</summary>
    /// <exception cref="ArgumentException">The selector is not valid or not supported.</exception>
    public static IReadOnlyList<ParserTag> Css(this ITagSearchable? scope, string selector)
    {
        return CssQuery.Get(selector).SelectAll(scope);
    }

    /// <inheritdoc cref="Css(ITagSearchable?, string)"/>
    public static IReadOnlyList<ParserTag> Css(this ITagSearchable? scope, CssQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.SelectAll(scope);
    }

    /// <summary>The first tag that matches <paramref name="selector"/>, or null. Stops at the first match.</summary>
    /// <exception cref="ArgumentException">The selector is not valid or not supported.</exception>
    public static ParserTag? CssFirst(this ITagSearchable? scope, string selector)
    {
        return CssQuery.Get(selector).SelectFirst(scope);
    }

    /// <inheritdoc cref="CssFirst(ITagSearchable?, string)"/>
    public static ParserTag? CssFirst(this ITagSearchable? scope, CssQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.SelectFirst(scope);
    }

    /// <summary>Whether the tag matches <paramref name="selector"/> (as <c>element.matches()</c>); false for null and comments.</summary>
    /// <exception cref="ArgumentException">The selector is not valid or not supported.</exception>
    public static bool IsCss(this ParserTag? tag, string selector)
    {
        return CssQuery.Get(selector).Matches(tag);
    }

    /// <inheritdoc cref="IsCss(ParserTag?, string)"/>
    public static bool IsCss(this ParserTag? tag, CssQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Matches(tag);
    }
}
