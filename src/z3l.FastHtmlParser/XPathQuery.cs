using System.Collections.Concurrent;
using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser;

/// <summary>
/// A compiled XPath 1.0 expression. Compile once and reuse it on any number of documents, from any number of threads;
/// the string overloads of <c>XPath</c> / <c>XPathValue</c> compile and cache expressions automatically.
/// </summary>
/// <example>
/// <code>
/// static readonly XPathQuery Price = XPathQuery.Compile("//div[@class='card']//span[@class='price']");
/// var prices = doc.XPath(Price).Select(p => p.Text());
/// </code>
/// </example>
/// <remarks>
/// <para>The full XPath 1.0 syntax: all axes except namespace, predicates (<c>[1]</c>, <c>[last()]</c>, <c>[@id='x']</c>,
/// <c>[a/b]</c>), <c>|</c>, operators and the core functions, plus <c>ends-with()</c>, <c>lower-case()</c> and
/// <c>upper-case()</c>. Variables and the <c>id()</c> / <c>lang()</c> functions are not supported.</para>
/// <para>HTML rules: tag and attribute names are case-insensitive; prefixed names (<c>dc:creator</c>) are matched as written,
/// without namespace URIs. The string-value of an element is its <c>Text()</c> (visible text, whitespace collapsed,
/// entities decoded); <c>text()</c> nodes and attribute values are decoded; <c>normalize-space()</c> treats any Unicode
/// whitespace (including &amp;nbsp;) as a space.</para>
/// <para>A relative path is evaluated from the tag it is called on (<c>tag.XPath("a")</c>: its child links,
/// <c>tag.XPath(".//a")</c>: all links inside it); a path starting with <c>/</c> or <c>//</c> searches the whole document.</para>
/// </remarks>
public sealed class XPathQuery
{
    private const int CacheLimit = 1024;
    private static readonly ConcurrentDictionary<string, XPathQuery> Cache = new(StringComparer.Ordinal);

    private readonly Expr _expr;

    private XPathQuery(string expression, Expr expr)
    {
        Expression = expression;
        _expr = expr;
    }

    /// <summary>The source of the expression.</summary>
    public string Expression { get; }

    /// <summary>Compiles an XPath 1.0 expression.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is null.</exception>
    /// <exception cref="ArgumentException">The expression is not valid XPath; the message gives the position.</exception>
    public static XPathQuery Compile(string xpath)
    {
        ArgumentNullException.ThrowIfNull(xpath);

        return new XPathQuery(xpath, XPathCompiler.Compile(xpath));
    }

    /// <summary>A compiled expression from the cache (compiled and added on the first use).</summary>
    internal static XPathQuery Get(string xpath)
    {
        ArgumentNullException.ThrowIfNull(xpath);

        if (Cache.TryGetValue(xpath, out var query)) return query;

        query = Compile(xpath);
        if (Cache.Count >= CacheLimit) Cache.Clear(); // dynamically built expressions must not grow the cache forever
        Cache.TryAdd(xpath, query);
        return query;
    }

    /// <inheritdoc/>
    public override string ToString() => Expression;

    /// <summary>The tags (elements and comments) selected by the expression, in document order.</summary>
    internal IReadOnlyList<ParserTag> SelectTags(ITagSearchable? scope)
    {
        if (_expr.Type != XType.NodeSet)
        {
            throw new ArgumentException($"The XPath \"{Expression}\" gives a {_expr.Type.ToString().ToLowerInvariant()}, not tags; use XPathValue().", "xpath");
        }

        if (Evaluate(scope) is not List<XNode> nodes || nodes.Count == 0) return [];

        var tags = new List<ParserTag>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Kind is XNodeKind.Element or XNodeKind.Comment) tags.Add(node.Tag!);
        }
        return tags;
    }

    /// <summary>The string-value of each selected node; for a number, string or boolean result: that one value.</summary>
    internal IReadOnlyList<string> SelectValues(ITagSearchable? scope)
    {
        var result = Evaluate(scope);
        if (result == null) return [];

        if (result is not List<XNode> nodes) return [XValue.ToString(result)];

        var values = new string[nodes.Count];
        for (int i = 0; i < values.Length; i++) values[i] = XNav.StringValue(nodes[i]);
        return values;
    }

    /// <summary>The string-value of the first selected node (null if none), or the number / string / boolean result.</summary>
    internal string? SelectValue(ITagSearchable? scope)
    {
        var result = Evaluate(scope);
        if (result is List<XNode> nodes) return nodes.Count == 0 ? null : XNav.StringValue(nodes[0]);

        return result == null ? null : XValue.ToString(result);
    }

    private object? Evaluate(ITagSearchable? scope)
    {
        XNode context;
        switch (scope)
        {
            case HtmlParser document:
                context = XNode.Root(document);
                break;
            case ParserTag tag:
                context = XNode.FromTag(tag);
                break;
            default:
                return null;
        }

        return _expr.Eval(new Ctx(context, 1, 1, new EvalState(context.Document)));
    }
}
