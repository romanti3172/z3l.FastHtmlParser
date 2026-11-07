using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>
/// Moving between tags: parent, children, siblings, next/previous.
/// The overloads with a <see cref="TagQuery"/> move to the nearest tag that matches it.
/// </summary>
public static class ParserTagNavigationExtensions
{
    /// <summary>The enclosing tag, or null for top-level tags.</summary>
    public static ParserTag? Parent(this ParserTag? tag)
    {
        if (tag == null) return default;

        var previous = tag.Previous();
        while (previous != null && previous.Depth >= tag.Depth)
        {
            previous = previous.Previous();
        }

        return previous?.Depth + 1 == tag.Depth ? previous : default;
    }

    /// <summary>The nearest enclosing tag that matches <paramref name="query"/>.</summary>
    public static ParserTag? Parent(this ParserTag? tag, in TagQuery query)
    {
        var result = tag.Parent();
        while (result != null && !query.Matches(result))
        {
            result = result.Parent();
        }
        return result;
    }

    /// <summary>The direct children of the tag, in document order.</summary>
    public static IEnumerable<ParserTag> Children(this ParserTag? tag)
    {
        if (tag == null) yield break;

        var depth = tag.Depth + 1;
        foreach (var descendant in tag)
        {
            if (descendant.Depth == depth) yield return descendant;
        }
    }

    /// <summary>The next tag in document order (the first child, if any).</summary>
    public static ParserTag? Next(this ParserTag? tag)
    {
        if (tag == null || tag.Index + 1 >= tag.Document.Data.Count) return default; // last tag: no next one

        return tag.Document.Data[tag.Index + 1];
    }

    /// <summary>The first tag after this one (after its closing tag) that matches <paramref name="query"/>.</summary>
    public static ParserTag? Next(this ParserTag? tag, in TagQuery query)
    {
        if (tag == null) return default;

        // Tags are stored in Start order: scan from right after the tag and stop at the first match.
        var data = tag.Document.Data;
        var end = tag.End;
        for (int i = tag.Index + 1, count = data.Count; i < count; i++)
        {
            var item = data[i];
            if (item.Start > end && query.Matches(item)) return item;
        }

        return default;
    }

    /// <summary>The previous tag in document order.</summary>
    public static ParserTag? Previous(this ParserTag? tag)
    {
        if (tag == null || tag.Index < 1) return default;

        return tag.Document.Data[tag.Index - 1];
    }

    /// <summary>The next tag with the same parent.</summary>
    public static ParserTag? NextSibling(this ParserTag? tag)
    {
        return tag.NextSibling(default(TagQuery));
    }

    /// <summary>The next tag with the same parent that matches <paramref name="query"/>.</summary>
    public static ParserTag? NextSibling(this ParserTag? tag, in TagQuery query)
    {
        if (tag == null) return default;

        // deeper tags are inside the following siblings; a shallower one ends the parent
        var next = tag.Next();
        while (next != null && next.Depth >= tag.Depth)
        {
            if (next.Depth == tag.Depth && query.Matches(next)) return next;
            next = next.Next();
        }

        return default;
    }

    /// <summary>The previous tag with the same parent.</summary>
    public static ParserTag? PreviousSibling(this ParserTag? tag)
    {
        return tag.PreviousSibling(default(TagQuery));
    }

    /// <summary>The previous tag with the same parent that matches <paramref name="query"/>.</summary>
    public static ParserTag? PreviousSibling(this ParserTag? tag, in TagQuery query)
    {
        if (tag == null) return default;

        // deeper tags are inside the previous siblings; a shallower one is the parent
        var previous = tag.Previous();
        while (previous != null && previous.Depth >= tag.Depth)
        {
            if (previous.Depth == tag.Depth && query.Matches(previous)) return previous;
            previous = previous.Previous();
        }

        return default;
    }

    /// <summary>Raw text between the end of this tag and the next '&lt;' (or the end of the document).</summary>
    public static string? TextAfterTagUntilNextOne(this ParserTag? tag)
    {
        if (tag == null) return default;

        var source = tag.Document.PageSource;
        var next = source.IndexOf('<', tag.End);
        return source.Substring(tag.End + 1, (next < 0 ? source.Length : next) - tag.End - 1);
    }

    /// <summary>Text between the end of this tag and the next '&lt;', whitespace collapsed and entities decoded.</summary>
    public static string? TextNoTagsAfterTagUntilNextOne(this ParserTag? tag)
    {
        return HtmlToTextCore.ClearTags(tag.TextAfterTagUntilNextOne());
    }

    /// <summary>
    /// Raw HTML between the end of this tag and the next tag that matches <paramref name="query"/>;
    /// empty if there is none.
    /// </summary>
    public static string? TextAfterUntil(this ParserTag? tag, in TagQuery query)
    {
        if (tag == null) return default;

        var until = tag.Next(query);
        return until == null
            ? ""
            : tag.Document.PageSource.Substring(tag.End + 1, until.Start - tag.End - 1);
    }
}
