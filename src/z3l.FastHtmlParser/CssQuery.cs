using System.Collections.Concurrent;
using z3l.FastHtmlParser.Core.Css;

namespace z3l.FastHtmlParser;

/// <summary>
/// A compiled CSS selector. Compile once and reuse it on any number of documents, from any number of threads;
/// the string overloads of <c>Css</c> / <c>CssFirst</c> / <c>IsCss</c> compile and cache selectors automatically.
/// </summary>
/// <example>
/// <code>
/// static readonly CssQuery Price = CssQuery.Compile("div.card span.price");
/// var prices = doc.Css(Price).Select(p => p.Text());
/// </code>
/// </example>
/// <remarks>
/// <para>Supported: type, <c>*</c>, <c>#id</c>, <c>.class</c>, attribute selectors (<c>[a]</c>, <c>=</c>, <c>~=</c>, <c>|=</c>,
/// <c>^=</c>, <c>$=</c>, <c>*=</c>, with the <c>i</c> / <c>s</c> flags), the combinators (descendant, <c>&gt;</c>, <c>+</c>,
/// <c>~</c>), selector lists (<c>a, b</c>) and the pseudo-classes <c>:first-child</c>, <c>:last-child</c>, <c>:only-child</c>,
/// <c>:first-of-type</c>, <c>:last-of-type</c>, <c>:only-of-type</c>, <c>:nth-child(An+B [of S])</c>, <c>:nth-last-child()</c>,
/// <c>:nth-of-type()</c>, <c>:nth-last-of-type()</c>, <c>:not()</c>, <c>:is()</c>, <c>:where()</c>, <c>:has()</c>,
/// <c>:empty</c>, <c>:root</c>, <c>:scope</c>, <c>:link</c>, <c>:any-link</c>, <c>:checked</c>, <c>:disabled</c>,
/// <c>:enabled</c>, <c>:required</c>, <c>:optional</c>, and <c>:contains("text")</c> (the visible text contains the string).
/// Pseudo-elements and namespaces are not supported.</para>
/// <para>HTML rules: tag and attribute names are case-insensitive; ids, classes and attribute values are case-sensitive,
/// as in standards mode, except the values the HTML standard lists (<c>type</c>, <c>rel</c>, <c>lang</c>, <c>method</c>, ...:
/// <c>[type=text]</c> matches <c>type="TEXT"</c>); the <c>i</c> / <c>s</c> flags override that. Attribute values are compared decoded
/// (<c>&amp;amp;</c> → <c>&amp;</c>). Only elements are selected, never comments.</para>
/// <para>As <c>querySelectorAll</c>: <c>tag.Css(...)</c> returns the tags inside the tag, while the whole selector is matched
/// against the document (<c>tag.Css("div a")</c> also finds links whose <c>div</c> is outside the tag). <c>:scope</c> is the tag;
/// a selector may start with <c>&gt;</c> for its children: <c>ul.Css("&gt; li")</c>.</para>
/// </remarks>
public sealed class CssQuery
{
    private const int CacheLimit = 1024;
    private static readonly ConcurrentDictionary<string, CssQuery> Cache = new(StringComparer.Ordinal);

    private readonly CssSelectorList _selector;

    private CssQuery(string selector, CssSelectorList compiled)
    {
        Selector = selector;
        _selector = compiled;
    }

    /// <summary>The source of the selector.</summary>
    public string Selector { get; }

    /// <summary>Compiles a CSS selector (or a comma-separated list of them).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is null.</exception>
    /// <exception cref="ArgumentException">The selector is not valid or not supported; the message gives the position.</exception>
    public static CssQuery Compile(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return new CssQuery(selector, CssCompiler.Compile(selector));
    }

    /// <summary>A compiled selector from the cache (compiled and added on the first use).</summary>
    internal static CssQuery Get(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (Cache.TryGetValue(selector, out var query)) return query;

        query = Compile(selector);
        if (Cache.Count >= CacheLimit) Cache.Clear(); // dynamically built selectors must not grow the cache forever
        Cache.TryAdd(selector, query);
        return query;
    }

    /// <inheritdoc/>
    public override string ToString() => Selector;

    /// <summary>The matching tags in the scope, in document order.</summary>
    internal IReadOnlyList<ParserTag> SelectAll(ITagSearchable? scope)
    {
        List<ParserTag>? result = null;
        Scan(scope, ref result, firstOnly: false);
        return result ?? (IReadOnlyList<ParserTag>)[];
    }

    /// <summary>The first matching tag in the scope, or null.</summary>
    internal ParserTag? SelectFirst(ITagSearchable? scope)
    {
        List<ParserTag>? result = null;
        return Scan(scope, ref result, firstOnly: true);
    }

    /// <summary>Whether the tag matches (as <c>element.matches()</c>: <c>:scope</c> is the tag itself).</summary>
    internal bool Matches(ParserTag? tag)
    {
        return tag != null && CssMatcher.IsElement(tag) && _selector.Matches(tag, new CssContext(tag.Document, tag, tag));
    }

    private ParserTag? Scan(ITagSearchable? scope, ref List<ParserTag>? result, bool firstOnly)
    {
        if (scope == null) return null;

        var scopeTag = scope as ParserTag;
        var document = scopeTag?.Document ?? (HtmlParser)scope;
        var context = new CssContext(document, scopeTag, scopeTag);

        scope.GetScope(out var tags, out int first, out int end);
        for (int i = first, count = tags.Count; i < count; i++)
        {
            ref readonly var node = ref tags.Node(i);
            if (node.End > end) break;

            // the tag object is made only for a tag the type test leaves in
            if (!_selector.MayMatch(node)) continue;
            var tag = tags[i];
            if (!_selector.Matches(tag, context)) continue;

            if (firstOnly) return tag;
            (result ??= []).Add(tag);
        }

        return null;
    }
}
