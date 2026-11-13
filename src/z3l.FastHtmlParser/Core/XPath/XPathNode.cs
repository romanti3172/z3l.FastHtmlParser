namespace z3l.FastHtmlParser.Core.XPath;

internal enum XNodeKind : byte
{
    Root,
    Element,
    Comment,
    Text,
    Attribute,
}

/// <summary>
/// A node of the XPath data model over the flat tag list: the document root, a tag (element or comment),
/// a text node (a run of source text between tags) or an attribute (positions in the opening tag).
/// </summary>
internal readonly struct XNode
{
    public readonly XNodeKind Kind;
    public readonly HtmlParser Document;
    /// <summary>Element / comment: the tag. Text / attribute: the parent element (null: the root).</summary>
    public readonly ParserTag? Tag;
    /// <summary>Text: first char. Attribute: name start.</summary>
    public readonly int Start;
    /// <summary>Text: length. Attribute: name length.</summary>
    public readonly int Length;
    /// <summary>Attribute: value start, -1 for an attribute without a value.</summary>
    public readonly int ValueStart;
    public readonly int ValueLength;

    private XNode(XNodeKind kind, HtmlParser document, ParserTag? tag, int start, int length, int valueStart, int valueLength)
    {
        Kind = kind;
        Document = document;
        Tag = tag;
        Start = start;
        Length = length;
        ValueStart = valueStart;
        ValueLength = valueLength;
    }

    public static XNode Root(HtmlParser document) => new(XNodeKind.Root, document, null, 0, 0, 0, 0);

    public static XNode FromTag(ParserTag tag) =>
        new(tag.Key == (int)HtmlTag.Comment ? XNodeKind.Comment : XNodeKind.Element, tag.Document, tag, 0, 0, 0, 0);

    public static XNode Text(HtmlParser document, ParserTag? parent, int start, int length) =>
        new(XNodeKind.Text, document, parent, start, length, 0, 0);

    public static XNode Attribute(ParserTag owner, int nameStart, int nameLength, int valueStart, int valueLength) =>
        new(XNodeKind.Attribute, owner.Document, owner, nameStart, nameLength, valueStart, valueLength);

    /// <summary>
    /// Document order and identity: tags and text nodes by their first char; attributes right after
    /// their element (same high half), before its children.
    /// </summary>
    public long Key => Kind switch
    {
        XNodeKind.Root => -1,
        XNodeKind.Element or XNodeKind.Comment => (long)Tag!.Start << 32,
        XNodeKind.Attribute => ((long)Tag!.Start << 32) | (uint)Start,
        _ => (long)Start << 32,
    };

    /// <summary>First source position the node covers (for following / preceding).</summary>
    public int RangeStart => Kind switch
    {
        XNodeKind.Root => -1,
        XNodeKind.Text => Start,
        _ => Tag!.Start,
    };

    /// <summary>Last source position the node covers (for following / preceding).</summary>
    public int RangeEnd => Kind switch
    {
        XNodeKind.Root => int.MaxValue,
        XNodeKind.Element or XNodeKind.Comment => Tag!.End,
        XNodeKind.Text => Start + Length - 1,
        _ => Tag!.Start, // attribute: the children of its element follow it
    };

    public ReadOnlySpan<char> AttributeName => Document.PageSource.AsSpan(Start, Length);

    public ReadOnlySpan<char> AttributeRawValue => ValueStart < 0 ? default : Document.PageSource.AsSpan(ValueStart, ValueLength);
}

/// <summary>Scans the attributes of an opening tag, as name / value positions in the page source.</summary>
internal ref struct AttributeScanner
{
    private readonly ReadOnlySpan<char> _source;
    private readonly int _end; // exclusive: the closing '>' of the opening tag
    private int _pos;

    public AttributeScanner(ParserTag tag)
    {
        _source = tag.Document.PageSource;

        var openEnd = tag.InnerStart > 0 && tag.InnerEnd > 0 ? tag.InnerStart - 1 : tag.End;
        _end = Math.Min(openEnd, _source.Length);

        // skip '<' and the name
        var i = tag.Start + 1;
        while (i < _end)
        {
            var c = _source[i];
            if (char.IsWhiteSpace(c) || c == '/' || c == '>') break;
            i++;
        }
        _pos = i;
    }

    public bool Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength)
    {
        var s = _source;
        while (true)
        {
            while (_pos < _end && (char.IsWhiteSpace(s[_pos]) || s[_pos] == '/')) _pos++;
            if (_pos >= _end) break;

            nameStart = _pos;
            while (_pos < _end)
            {
                var c = s[_pos];
                if (char.IsWhiteSpace(c) || c == '=' || c == '/' || c == '>') break;
                _pos++;
            }
            nameLength = _pos - nameStart;

            if (nameLength == 0)
            {
                _pos++; // a stray '=': skip it
                continue;
            }

            var p = _pos;
            while (p < _end && char.IsWhiteSpace(s[p])) p++;

            if (p < _end && s[p] == '=')
            {
                p++;
                while (p < _end && char.IsWhiteSpace(s[p])) p++;

                if (p < _end && (s[p] == '"' || s[p] == '\''))
                {
                    var quote = s[p];
                    valueStart = p + 1;
                    var close = s[valueStart.._end].IndexOf(quote);
                    valueLength = close < 0 ? _end - valueStart : close;
                    _pos = close < 0 ? _end : valueStart + close + 1;
                }
                else
                {
                    valueStart = p;
                    while (p < _end && !char.IsWhiteSpace(s[p])) p++;
                    valueLength = p - valueStart;
                    _pos = p;
                }
            }
            else
            {
                valueStart = -1;
                valueLength = 0;
            }

            return true;
        }

        nameStart = nameLength = valueStart = valueLength = 0;
        return false;
    }
}

/// <summary>What a location step selects: a name, *, prefix:*, node(), text(), comment().</summary>
internal sealed class NodeTest
{
    public enum TestKind : byte
    {
        Name,
        Prefix,         // dc:*
        Wildcard,       // *
        Node,           // node()
        Text,           // text()
        Comment,        // comment()
        Pi,             // processing-instruction(): never matches
        Container,      // internal: the root and elements (// before a child or attribute step)
    }

    public static readonly NodeTest AnyNode = new(TestKind.Node, null);
    public static readonly NodeTest Containers = new(TestKind.Container, null);

    public readonly TestKind Kind;
    public readonly string? Name;
    private readonly TagQuery _query;

    public NodeTest(TestKind kind, string? name)
    {
        Kind = kind;
        Name = name;
        if (kind == TestKind.Name) _query = new TagQuery(name);
    }

    public bool MatchesText => Kind is TestKind.Node or TestKind.Text;

    public bool MatchesTags => Kind is TestKind.Name or TestKind.Prefix or TestKind.Wildcard or TestKind.Node or TestKind.Comment or TestKind.Container;

    public bool Matches(ParserTag tag) => Kind switch
    {
        TestKind.Name => _query.Matches(tag),
        TestKind.Prefix => XNav.IsElement(tag) && tag.Name!.StartsWith(Name!, StringComparison.OrdinalIgnoreCase),
        TestKind.Wildcard or TestKind.Container => XNav.IsElement(tag),
        TestKind.Node => XNav.IsElement(tag) || XNav.IsComment(tag),
        TestKind.Comment => XNav.IsComment(tag),
        _ => false,
    };

    public bool MatchesAttribute(ReadOnlySpan<char> name) => Kind switch
    {
        TestKind.Name => name.Equals(Name, StringComparison.OrdinalIgnoreCase),
        TestKind.Prefix => name.StartsWith(Name, StringComparison.OrdinalIgnoreCase),
        TestKind.Wildcard or TestKind.Node => true,
        _ => false,
    };

    public bool Matches(in XNode node, bool attributeAxis) => node.Kind switch
    {
        XNodeKind.Root => Kind is TestKind.Node or TestKind.Container,
        XNodeKind.Element or XNodeKind.Comment => Matches(node.Tag!),
        XNodeKind.Text => MatchesText,
        _ => attributeAxis ? MatchesAttribute(node.AttributeName) : Kind == TestKind.Node,
    };
}

/// <summary>Tree navigation over the flat tag list.</summary>
internal static class XNav
{
    public static bool IsComment(ParserTag tag) => tag.Key == (int)HtmlTag.Comment;

    public static bool IsElement(ParserTag tag)
    {
        var key = tag.Key;
        if (key >= 0) return key != (int)HtmlTag.Comment && key != (int)HtmlTag.Doctype && key != (int)HtmlTag.XmlDeclaration;

        var name = tag.CustomName;
        return !string.IsNullOrEmpty(name) && name[0] != '!' && name[0] != '?';
    }

    /// <summary>The tags (Data[first..], up to <paramref name="end"/>, at <paramref name="childDepth"/>) and the text range of a container.</summary>
    private static bool TryGetContent(in XNode node, out int first, out int end, out int childDepth, out int textStart, out int textEnd)
    {
        if (node.Kind == XNodeKind.Root)
        {
            first = 0;
            end = int.MaxValue;
            childDepth = 0;
            textStart = 0;
            textEnd = node.Document.PageSource.Length;
            return true;
        }

        if (node.Kind == XNodeKind.Element)
        {
            var tag = node.Tag!;
            if (tag.InnerStart > 0 && tag.InnerEnd > 0)
            {
                first = tag.Index + 1;
                end = tag.End;
                childDepth = tag.Depth + 1;
                textStart = tag.InnerStart;
                textEnd = tag.InnerEnd + 1;
                return true;
            }
        }

        first = end = childDepth = textStart = textEnd = 0;
        return false;
    }

    /// <summary>Children of the root or an element that match <paramref name="test"/>, text nodes included, in document order.</summary>
    public static void AddChildren(in XNode node, NodeTest test, List<XNode> output)
    {
        bool wantText = test.MatchesText, wantTags = test.MatchesTags;
        if (!wantText && !wantTags) return;
        if (!TryGetContent(node, out var first, out var end, out var childDepth, out var p, out var textEnd)) return;

        var data = node.Document.Data;
        var parent = node.Kind == XNodeKind.Element ? node.Tag : null;

        for (int i = first, count = data.Count; i < count; i++)
        {
            var tag = data[i];
            if (tag.End > end) break;
            if (tag.Depth != childDepth) continue;

            if (wantText && tag.Start > p) AddText(node.Document, parent, p, Math.Min(tag.Start, textEnd), output);
            if (wantTags && test.Matches(tag)) output.Add(XNode.FromTag(tag));

            p = tag.End + 1;
        }

        if (wantText && textEnd > p) AddText(node.Document, parent, p, textEnd, output);
    }

    private static void AddText(HtmlParser document, ParserTag? parent, int start, int end, List<XNode> output)
    {
        if (end > start) output.Add(XNode.Text(document, parent, start, end - start));
    }

    /// <summary>Descendants of the root or an element that match <paramref name="test"/>, in document order.</summary>
    public static void AddDescendants(in XNode node, NodeTest test, List<XNode> output)
    {
        if (test.MatchesText)
        {
            AddDescendantsWithText(node, test, output);
            return;
        }

        if (!test.MatchesTags || !TryGetContent(node, out var first, out var end, out _, out _, out _)) return;

        // tags only: one linear scan of the flat list
        var data = node.Document.Data;
        for (int i = first, count = data.Count; i < count; i++)
        {
            var tag = data[i];
            if (tag.End > end) break;

            if (test.Matches(tag)) output.Add(XNode.FromTag(tag));
        }
    }

    private static void AddDescendantsWithText(in XNode node, NodeTest test, List<XNode> output)
    {
        var children = new List<XNode>();
        AddChildren(node, NodeTest.AnyNode, children);

        foreach (var child in children)
        {
            if (test.Matches(child, attributeAxis: false)) output.Add(child);
            if (child.Kind == XNodeKind.Element) AddDescendantsWithText(child, test, output);
        }
    }

    public static bool TryGetParent(in XNode node, out XNode parent)
    {
        switch (node.Kind)
        {
            case XNodeKind.Root:
                parent = default;
                return false;

            case XNodeKind.Text:
            case XNodeKind.Attribute:
                parent = node.Tag is { } owner ? XNode.FromTag(owner) : XNode.Root(node.Document);
                return true;

            default:
                var tag = node.Tag!;
                parent = tag.Depth > 0 && tag.Parent() is { } p ? XNode.FromTag(p) : XNode.Root(node.Document);
                return true;
        }
    }

    public static void AddAttributes(in XNode node, NodeTest test, List<XNode> output)
    {
        if (node.Kind != XNodeKind.Element) return;

        var tag = node.Tag!;
        var scanner = new AttributeScanner(tag);
        var source = tag.Document.PageSource.AsSpan();
        while (scanner.Next(out var ns, out var nl, out var vs, out var vl))
        {
            if (test.MatchesAttribute(source.Slice(ns, nl))) output.Add(XNode.Attribute(tag, ns, nl, vs, vl));
        }
    }

    /// <summary>The raw value of the first attribute with the given name; false when the element has none.</summary>
    public static bool TryGetAttribute(in XNode node, string name, out ReadOnlySpan<char> raw)
    {
        raw = default;
        if (node.Kind != XNodeKind.Element) return false;

        var tag = node.Tag!;
        var scanner = new AttributeScanner(tag);
        var source = tag.Document.PageSource.AsSpan();
        while (scanner.Next(out var ns, out var nl, out var vs, out var vl))
        {
            if (source.Slice(ns, nl).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                raw = vs < 0 ? default : source.Slice(vs, vl);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The string-value: the root and elements give their visible text (as <c>Text()</c>: whitespace collapsed,
    /// script/style dropped); text nodes and attributes their decoded value; comments their content.
    /// </summary>
    public static string StringValue(in XNode node)
    {
        switch (node.Kind)
        {
            case XNodeKind.Root:
                return HtmlToTextCore.ClearTags(node.Document.PageSource) ?? "";

            case XNodeKind.Element:
                return node.Tag!.Text() ?? "";

            case XNodeKind.Attribute:
                return node.ValueStart < 0 ? "" : SpanHandlingCore.DecodeAttribute(node.AttributeRawValue);

            case XNodeKind.Text:
            {
                var text = node.Document.PageSource.AsSpan(node.Start, node.Length);
                if (node.Tag is { } parent && ParsingCore.IsRawText(parent.Key)) return text.ToString();
                return text.Contains('&') ? HtmlDecodeCore.HtmlDecode(text) : text.ToString();
            }

            default: // comment
            {
                var tag = node.Tag!;
                var source = tag.Document.PageSource.AsSpan(tag.Start, tag.End - tag.Start + 1);
                if (source.StartsWith("<!--")) source = source[4..];
                if (source.EndsWith("-->")) source = source[..^3];
                return source.ToString();
            }
        }
    }

    /// <summary>The name for name() / local-name(): lower-case tag or attribute name; empty for other nodes.</summary>
    public static string NodeName(in XNode node) => node.Kind switch
    {
        XNodeKind.Element => node.Tag!.Name ?? "",
        XNodeKind.Attribute => node.AttributeName.ToString().ToLowerInvariant(),
        _ => "",
    };
}
