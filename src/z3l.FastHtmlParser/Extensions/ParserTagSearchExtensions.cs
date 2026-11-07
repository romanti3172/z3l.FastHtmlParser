namespace z3l.FastHtmlParser;

/// <summary>
/// Search in a document or inside a tag (its descendants), in document order.
/// For one-call shortcuts (<c>FindByNameClass</c>, <c>FindAllByNameItemprop</c>, ...) add
/// <c>using z3l.FastHtmlParser.Shortcuts;</c>.
/// </summary>
public static class ParserTagSearchExtensions
{
    /// <summary>The first tag that matches every criterion of <paramref name="query"/>, or null.</summary>
    public static ParserTag? Find(this ITagSearchable? scope, in TagQuery query)
    {
        if (scope == null) return null;

        scope.GetScope(out var tags, out int first, out int end);
        for (int i = first, count = tags.Count; i < count; i++)
        {
            var tag = tags[i];
            if (tag.End > end) break;

            if (query.Matches(tag)) return tag;
        }

        return null;
    }

    /// <summary>The first tag with the given name and, when they are not null, the class, id and visible text; or null.</summary>
    public static ParserTag? Find(this ITagSearchable? scope, HtmlTag name, string? cls = null, string? id = null, string? text = null)
    {
        return scope.Find(new TagQuery(name) { Class = cls, Id = id, Text = text });
    }

    /// <summary>
    /// The first tag with the given name (case-insensitive; null: any) and, when they are not null,
    /// the class, id and visible text; or null.
    /// </summary>
    public static ParserTag? Find(this ITagSearchable? scope, string? name, string? cls = null, string? id = null, string? text = null)
    {
        return scope.Find(new TagQuery(name) { Class = cls, Id = id, Text = text });
    }

    /// <summary>All tags that match every criterion of <paramref name="query"/> (lazy).</summary>
    public static IEnumerable<ParserTag> FindAll(this ITagSearchable? scope, TagQuery query)
    {
        return scope == null ? [] : FindAllIterator(scope, query);
    }

    /// <summary>All tags with the given name and, when they are not null, the class, id and visible text (lazy).</summary>
    public static IEnumerable<ParserTag> FindAll(this ITagSearchable? scope, HtmlTag name, string? cls = null, string? id = null, string? text = null)
    {
        return scope.FindAll(new TagQuery(name) { Class = cls, Id = id, Text = text });
    }

    /// <summary>
    /// All tags with the given name (case-insensitive; null: any) and, when they are not null,
    /// the class, id and visible text (lazy).
    /// </summary>
    public static IEnumerable<ParserTag> FindAll(this ITagSearchable? scope, string? name, string? cls = null, string? id = null, string? text = null)
    {
        return scope.FindAll(new TagQuery(name) { Class = cls, Id = id, Text = text });
    }

    private static IEnumerable<ParserTag> FindAllIterator(ITagSearchable scope, TagQuery query)
    {
        scope.GetScope(out var tags, out int first, out int end);
        for (int i = first, count = tags.Count; i < count; i++)
        {
            var tag = tags[i];
            if (tag.End > end) break;

            if (query.Matches(tag)) yield return tag;
        }
    }
}
