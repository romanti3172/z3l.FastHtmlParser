namespace z3l.FastHtmlParser;

/// <summary>
/// XPath 1.0 queries on a document or a tag. See <see cref="XPathQuery"/> for what is supported.
/// A <see langword="null"/> scope gives empty results; an invalid expression throws <see cref="ArgumentException"/>.
/// </summary>
/// <example>
/// <code>
/// doc.XPath("//ul[@id='menu']/li[position() &lt;= 3]/a");
/// doc.XPathFirst("//h1")?.Text();
/// doc.XPathValues("//a[contains(@class, 'product')]/@href");
/// doc.XPathValue("count(//table//tr)");
/// </code>
/// </example>
public static class ParserTagXPathExtensions
{
    /// <summary>
    /// The tags selected by <paramref name="xpath"/>, in document order. Only tags are returned: use
    /// <see cref="XPathValues(ITagSearchable?, string)"/> for <c>@attribute</c> and <c>text()</c> results.
    /// </summary>
    /// <exception cref="ArgumentException">The expression is not valid, or gives a number, string or boolean instead of nodes.</exception>
    public static IReadOnlyList<ParserTag> XPath(this ITagSearchable? scope, string xpath)
    {
        return XPathQuery.Get(xpath).SelectTags(scope);
    }

    /// <inheritdoc cref="XPath(ITagSearchable?, string)"/>
    public static IReadOnlyList<ParserTag> XPath(this ITagSearchable? scope, XPathQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.SelectTags(scope);
    }

    /// <summary>The first tag selected by <paramref name="xpath"/>, or null.</summary>
    /// <exception cref="ArgumentException">The expression is not valid, or gives a number, string or boolean instead of nodes.</exception>
    public static ParserTag? XPathFirst(this ITagSearchable? scope, string xpath)
    {
        var tags = scope.XPath(xpath);
        return tags.Count > 0 ? tags[0] : null;
    }

    /// <inheritdoc cref="XPathFirst(ITagSearchable?, string)"/>
    public static ParserTag? XPathFirst(this ITagSearchable? scope, XPathQuery query)
    {
        var tags = scope.XPath(query);
        return tags.Count > 0 ? tags[0] : null;
    }

    /// <summary>
    /// The values of the nodes selected by <paramref name="xpath"/>: decoded attribute values for <c>@href</c>,
    /// decoded text for <c>text()</c>, the visible text (<c>Text()</c>) for tags. An expression that gives a number,
    /// string or boolean (<c>count(//a)</c>) returns that one value as a string.
    /// </summary>
    /// <exception cref="ArgumentException">The expression is not valid.</exception>
    public static IReadOnlyList<string> XPathValues(this ITagSearchable? scope, string xpath)
    {
        return XPathQuery.Get(xpath).SelectValues(scope);
    }

    /// <inheritdoc cref="XPathValues(ITagSearchable?, string)"/>
    public static IReadOnlyList<string> XPathValues(this ITagSearchable? scope, XPathQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.SelectValues(scope);
    }

    /// <summary>
    /// The value of the first node selected by <paramref name="xpath"/> (null if nothing is selected), or the result
    /// of an expression that gives a number, string or boolean: <c>"3"</c> for <c>count(//li)</c>, <c>"true"</c> / <c>"false"</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The expression is not valid.</exception>
    public static string? XPathValue(this ITagSearchable? scope, string xpath)
    {
        return XPathQuery.Get(xpath).SelectValue(scope);
    }

    /// <inheritdoc cref="XPathValue(ITagSearchable?, string)"/>
    public static string? XPathValue(this ITagSearchable? scope, XPathQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.SelectValue(scope);
    }
}
