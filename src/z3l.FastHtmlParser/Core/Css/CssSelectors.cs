using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser.Core.Css;

internal enum CssCombinator : byte
{
    None,
    Descendant, // "a b"
    Child,      // "a > b"
    Adjacent,   // "a + b"
    Sibling,    // "a ~ b"
}

/// <summary>
/// What a selector is matched against: the document, the tag the query was called on (<c>:scope</c>, null for the document)
/// and the tag a relative selector starts from (<c>:has(&gt; a)</c>, or <c>tag.Css("&gt; li")</c>; null: the document).
/// </summary>
internal readonly struct CssContext(HtmlParser document, ParserTag? scope, ParserTag? anchor)
{
    public readonly HtmlParser Document = document;
    public readonly ParserTag? Scope = scope;
    public readonly ParserTag? Anchor = anchor;

    public CssTree Tree => CssTree.For(Document);

    public CssContext WithAnchor(ParserTag anchor) => new(Document, Scope, anchor);
}

/// <summary>A selector list: <c>a, b, c</c>. A tag matches when it matches one of them.</summary>
internal sealed class CssSelectorList(CssComplex[] items)
{
    public readonly CssComplex[] Items = items;

    public bool Matches(ParserTag tag, in CssContext context)
    {
        foreach (var item in Items)
        {
            if (CssMatcher.Matches(item, item.Compounds.Length - 1, tag, context)) return true;
        }
        return false;
    }

    /// <summary>The type test of the rightmost compounds on the recorded tag: false rules the tag out with no tag object.</summary>
    public bool MayMatch(in ParsingCore.TagNode node)
    {
        foreach (var item in Items)
        {
            if (item.Compounds[^1].MatchesType(node)) return true;
        }
        return false;
    }
}

/// <summary>
/// Compound selectors joined by combinators: <c>div.card &gt; a</c>. <see cref="Combinators"/>[i] stands between
/// <see cref="Compounds"/>[i] and [i + 1]; <see cref="Leading"/> starts a relative selector (<c>:has(&gt; a)</c>).
/// </summary>
internal sealed class CssComplex(CssCombinator leading, CssCompound[] compounds, CssCombinator[] combinators)
{
    public readonly CssCombinator Leading = leading;
    public readonly CssCompound[] Compounds = compounds;
    public readonly CssCombinator[] Combinators = combinators;
}

/// <summary>A type selector (or none / <c>*</c>) and the simple selectors that follow it: <c>a.ext[href^=http]:not(.x)</c>.</summary>
internal sealed class CssCompound(int key, string? name, CssSimple[] simples)
{
    private readonly int _key = key;          // (int)HtmlTag, or -1
    private readonly string? _name = name;    // lower-case name not in HtmlTag; null with _key -1: any element
    private readonly CssSimple[] _simples = simples;

    public bool MatchesType(in ParsingCore.TagNode node)
    {
        if (_key >= 0) return node.Key == _key;
        if (_name != null) return node.Key < 0 && string.Equals(node.CustomName, _name, StringComparison.Ordinal);
        return node.IsElement;
    }

    public bool Matches(ParserTag tag, in CssContext context)
    {
        if (!MatchesType(tag.Node)) return false;

        foreach (var simple in _simples)
        {
            if (!simple.Matches(tag, context)) return false;
        }
        return true;
    }
}

internal static class CssMatcher
{
    /// <summary>Elements: not comments, the doctype or the XML declaration.</summary>
    public static bool IsElement(ParserTag tag) => XNav.IsElement(tag);

    public static ParserTag? Parent(ParserTag tag) => tag.ParentIndex < 0 ? null : tag.Document.Data[tag.ParentIndex];

    /// <summary>
    /// Whether <paramref name="tag"/> matches compound <paramref name="i"/> of <paramref name="selector"/> and the part
    /// to the left of it matches the tags the combinators lead to, right to left as browsers do.
    /// </summary>
    public static bool Matches(CssComplex selector, int i, ParserTag tag, in CssContext context)
    {
        if (!selector.Compounds[i].Matches(tag, context)) return false;

        if (i == 0) return selector.Leading == CssCombinator.None || IsRelated(selector.Leading, context.Anchor, tag, context);

        // The walks go by index over the recorded tags: a tag object is made only for a tag whose type matches.
        var data = tag.Document.Data;
        switch (selector.Combinators[i - 1])
        {
            case CssCombinator.Child:
                return MatchesAt(selector, i - 1, data, tag.ParentIndex, context);
            case CssCombinator.Descendant:
            {
                for (int parent = tag.ParentIndex; parent >= 0; parent = data.Node(parent).ParentIndex)
                {
                    if (MatchesAt(selector, i - 1, data, parent, context)) return true;
                }
                return false;
            }
            case CssCombinator.Adjacent:
                return MatchesAt(selector, i - 1, data, context.Tree.PreviousIndex(tag.Index), context);
            case CssCombinator.Sibling:
            {
                var tree = context.Tree;
                for (int previous = tree.PreviousIndex(tag.Index); previous >= 0; previous = tree.PreviousIndex(previous))
                {
                    if (MatchesAt(selector, i - 1, data, previous, context)) return true;
                }
                return false;
            }
            default:
                return false;
        }
    }

    /// <summary><see cref="Matches(CssComplex, int, ParserTag, in CssContext)"/> for the tag at <paramref name="index"/> (-1: none).</summary>
    private static bool MatchesAt(CssComplex selector, int i, TagList data, int index, in CssContext context) =>
        index >= 0 && selector.Compounds[i].MatchesType(data.Node(index)) && Matches(selector, i, data[index], context);

    /// <summary>Whether <paramref name="tag"/> stands in the <paramref name="combinator"/> relation to <paramref name="anchor"/> (null: the document).</summary>
    private static bool IsRelated(CssCombinator combinator, ParserTag? anchor, ParserTag tag, in CssContext context)
    {
        switch (combinator)
        {
            case CssCombinator.Child:
                return anchor == null ? tag.ParentIndex < 0 : tag.ParentIndex == anchor.Index;
            case CssCombinator.Descendant:
                if (anchor == null) return true;
                var data = tag.Document.Data;
                for (int parent = tag.ParentIndex; parent >= anchor.Index; parent = data.Node(parent).ParentIndex)
                {
                    if (parent == anchor.Index) return true;
                }
                return false;
            case CssCombinator.Adjacent:
                return anchor != null && context.Tree.Previous(tag) == anchor;
            case CssCombinator.Sibling:
                return anchor != null && anchor.Index < tag.Index && anchor.ParentIndex == tag.ParentIndex && IsElement(anchor);
            default:
                return false;
        }
    }

    /// <summary>Whether a tag in the scope matches the selector (<c>:has</c>): the scope is searched from its start.</summary>
    public static bool HasMatch(CssSelectorList relative, ParserTag anchor, in CssContext context)
    {
        var data = anchor.Document.Data;
        var inner = context.WithAnchor(anchor);

        foreach (var item in relative.Items)
        {
            int last = item.Compounds.Length - 1;
            int i = anchor.Index + 1;

            if (item.Leading is CssCombinator.Descendant or CssCombinator.Child)
            {
                // the tags inside the anchor
                for (; i < data.Count && data[i].End <= anchor.End; i++)
                {
                    if (IsElement(data[i]) && Matches(item, last, data[i], inner)) return true;
                }
            }
            else
            {
                // the following siblings and the tags inside them, up to the end of the parent
                while (i < data.Count && data[i].End <= anchor.End) i++;

                var parent = Parent(anchor);
                int end = parent?.End ?? int.MaxValue;
                for (; i < data.Count && data[i].End <= end; i++)
                {
                    if (IsElement(data[i]) && Matches(item, last, data[i], inner)) return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a whitespace-separated list contains <paramref name="word"/>, without allocating.</summary>
    public static bool ContainsWord(ReadOnlySpan<char> list, string word, StringComparison comparison)
    {
        if (word.Length == 0 || list.Length < word.Length || list.IndexOf(word, comparison) < 0) return false;

        while (true)
        {
            int start = 0;
            while (start < list.Length && IsWhitespace(list[start])) start++;
            if (start == list.Length) return false;

            list = list[start..];
            int end = list.IndexOfAny(Whitespace);
            var item = end < 0 ? list : list[..end];
            if (item.Equals(word, comparison)) return true;
            if (end < 0) return false;

            list = list[end..];
        }
    }

    /// <summary>The whitespace of class lists and <c>~=</c>: ASCII space, tab, line feed, form feed, carriage return.</summary>
    public static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    private const string Whitespace = " \t\n\f\r";

    /// <summary>Whether the element has the attribute (with or without a value).</summary>
    public static bool HasAttribute(ParserTag tag, string name)
    {
        // the fast lookup settles a non-empty value; an empty or missing one needs the attributes read one by one
        if (!SpanHandlingCore.SpanAttributeByName(tag, name).IsEmpty) return true;

        var scanner = new AttributeScanner(tag);
        var source = tag.Document.Source;
        while (scanner.Next(out var nameStart, out var nameLength, out _, out _))
        {
            if (source.Slice(nameStart, nameLength).Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The decoded value: the raw span when there is nothing to decode.</summary>
    public static ReadOnlySpan<char> Decoded(ReadOnlySpan<char> raw) => HtmlDecodeCore.NeedsDecoding(raw) ? SpanHandlingCore.DecodeAttribute(raw) : raw;
}

/// <summary>One simple selector of a compound: <c>#id</c>, <c>.class</c>, <c>[attr]</c>, <c>:pseudo</c>.</summary>
internal abstract class CssSimple
{
    public abstract bool Matches(ParserTag tag, in CssContext context);
}

internal sealed class CssId(string id) : CssSimple
{
    public override bool Matches(ParserTag tag, in CssContext context) =>
        SpanHandlingCore.AttributeEquals(SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Id), id);
}

internal sealed class CssClass(string name) : CssSimple
{
    public override bool Matches(ParserTag tag, in CssContext context)
    {
        var raw = SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Class);
        return !raw.IsEmpty && CssMatcher.ContainsWord(CssMatcher.Decoded(raw), name, StringComparison.Ordinal);
    }
}

internal enum CssAttributeOperator : byte
{
    Exists,     // [a]
    Equals,     // [a=v]
    Includes,   // [a~=v]
    Dash,       // [a|=v]
    Prefix,     // [a^=v]
    Suffix,     // [a$=v]
    Substring,  // [a*=v]
}

/// <summary><c>[name op value flag]</c>; the flag is 'i', 's', or '\0' when the selector has none: then the HTML rules decide.</summary>
internal sealed class CssAttribute(string name, CssAttributeOperator op, string value, char caseFlag) : CssSimple
{
    private readonly StringComparison _comparison =
        caseFlag == 'i' || (caseFlag == '\0' && CaseInsensitiveValues.Contains(name))
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    // HTML: the values of these attributes are compared ignoring ASCII case unless the selector has the 's' flag
    // ([type=text] matches type="TEXT", [rel~=nofollow] matches rel="NoFollow").
    private static readonly HashSet<string> CaseInsensitiveValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept", "accept-charset", "align", "alink", "axis", "bgcolor", "charset", "checked", "clear", "codetype", "color",
        "compact", "declare", "defer", "dir", "direction", "disabled", "enctype", "face", "frame", "hreflang", "http-equiv",
        "lang", "language", "link", "media", "method", "multiple", "nohref", "noresize", "noshade", "nowrap", "readonly",
        "rel", "rev", "rules", "scope", "scrolling", "selected", "shape", "target", "text", "type", "valign", "valuetype", "vlink",
    };

    public override bool Matches(ParserTag tag, in CssContext context)
    {
        var raw = SpanHandlingCore.SpanAttributeByName(tag, name);
        if (raw.IsEmpty)
        {
            // missing or empty: only these can match an empty value
            return op switch
            {
                CssAttributeOperator.Exists => CssMatcher.HasAttribute(tag, name),
                CssAttributeOperator.Equals or CssAttributeOperator.Dash => value.Length == 0 && CssMatcher.HasAttribute(tag, name),
                _ => false,
            };
        }

        if (op == CssAttributeOperator.Exists) return true;

        var actual = CssMatcher.Decoded(raw);
        return op switch
        {
            CssAttributeOperator.Equals => actual.Equals(value, _comparison),
            CssAttributeOperator.Includes => !value.AsSpan().ContainsAny(" \t\n\f\r") && CssMatcher.ContainsWord(actual, value, _comparison),
            CssAttributeOperator.Dash => actual.Equals(value, _comparison)
                || (actual.Length > value.Length && actual[value.Length] == '-' && actual.StartsWith(value, _comparison)),
            CssAttributeOperator.Prefix => value.Length > 0 && actual.StartsWith(value, _comparison),
            CssAttributeOperator.Suffix => value.Length > 0 && actual.EndsWith(value, _comparison),
            CssAttributeOperator.Substring => value.Length > 0 && actual.Contains(value, _comparison),
            _ => false,
        };
    }
}

/// <summary><c>:nth-child(An+B [of S])</c> and its relatives; <c>:first-child</c> is <c>:nth-child(1)</c>, and so on.</summary>
internal sealed class CssNth(int a, int b, bool fromEnd, bool ofType, CssSelectorList? of) : CssSimple
{
    public override bool Matches(ParserTag tag, in CssContext context)
    {
        var tree = context.Tree;
        int position;

        if (of != null)
        {
            if (!of.Matches(tag, context)) return false;

            position = 1;
            for (var sibling = Step(tree, tag); sibling != null; sibling = Step(tree, sibling))
            {
                if (of.Matches(sibling, context)) position++;
            }
        }
        else if (ofType)
        {
            position = fromEnd ? tree.TypePositionFromEnd(tag) : tree.TypePosition(tag);
        }
        else
        {
            position = fromEnd ? tree.PositionFromEnd(tag) : tree.Position(tag);
        }

        if (position <= 0) return false; // not an element

        // position = a*n + b for some n >= 0
        if (a == 0) return position == b;

        int diff = position - b;
        return diff % a == 0 && diff / a >= 0;
    }

    private ParserTag? Step(CssTree tree, ParserTag tag) => fromEnd ? tree.Next(tag) : tree.Previous(tag);
}

internal sealed class CssNot(CssSelectorList list) : CssSimple
{
    public override bool Matches(ParserTag tag, in CssContext context) => !list.Matches(tag, context);
}

/// <summary><c>:is()</c>, <c>:where()</c>, <c>:matches()</c>.</summary>
internal sealed class CssIs(CssSelectorList list) : CssSimple
{
    public override bool Matches(ParserTag tag, in CssContext context) => list.Matches(tag, context);
}

internal sealed class CssHas(CssSelectorList relative) : CssSimple
{
    public override bool Matches(ParserTag tag, in CssContext context) => CssMatcher.HasMatch(relative, tag, context);
}

/// <summary><c>:contains("text")</c>: the visible text (<c>Text()</c>) contains the string (case-sensitive).</summary>
internal sealed class CssContains(string text) : CssSimple
{
    public override bool Matches(ParserTag tag, in CssContext context) =>
        tag.Text()?.Contains(text, StringComparison.Ordinal) == true;
}

internal enum CssPseudoKind : byte
{
    Root,
    Scope,
    Empty,
    Link,
    Checked,
    Disabled,
    Enabled,
    Required,
    Optional,
}

internal sealed class CssPseudo(CssPseudoKind kind) : CssSimple
{
    public override bool Matches(ParserTag tag, in CssContext context)
    {
        switch (kind)
        {
            case CssPseudoKind.Root:
                return IsRoot(tag, context);
            case CssPseudoKind.Scope:
                return context.Scope != null ? tag == context.Scope : IsRoot(tag, context);
            case CssPseudoKind.Empty:
                return IsEmpty(tag);
            case CssPseudoKind.Link:
                return tag.Name is "a" or "area" && CssMatcher.HasAttribute(tag, "href");
            case CssPseudoKind.Checked:
                return (tag.Name == "input" && CssMatcher.HasAttribute(tag, "checked"))
                    || (tag.Name == "option" && CssMatcher.HasAttribute(tag, "selected"));
            case CssPseudoKind.Disabled:
                return IsFormControl(tag) && CssMatcher.HasAttribute(tag, "disabled");
            case CssPseudoKind.Enabled:
                return IsFormControl(tag) && !CssMatcher.HasAttribute(tag, "disabled");
            case CssPseudoKind.Required:
                return IsInput(tag) && CssMatcher.HasAttribute(tag, "required");
            case CssPseudoKind.Optional:
                return IsInput(tag) && !CssMatcher.HasAttribute(tag, "required");
            default:
                return false;
        }
    }

    // the first top-level element: <html> on a page
    private static bool IsRoot(ParserTag tag, in CssContext context) => tag.ParentIndex < 0 && context.Tree.Previous(tag) == null;

    private static bool IsFormControl(ParserTag tag) =>
        tag.Name is "button" or "input" or "select" or "textarea" or "optgroup" or "option" or "fieldset";

    private static bool IsInput(ParserTag tag) => tag.Name is "input" or "select" or "textarea";

    /// <summary>No child elements and no text; comments are allowed.</summary>
    private static bool IsEmpty(ParserTag tag)
    {
        if (tag.InnerStart <= 0 || tag.InnerEnd <= 0) return true; // a void or unclosed tag has no content

        int position = tag.InnerStart;
        var data = tag.Document.Data;
        for (int i = tag.Index + 1; i < data.Count && data[i].End <= tag.End; i++)
        {
            var child = data[i];
            if (CssMatcher.IsElement(child) || child.Start > position) return false; // an element, or text before a comment
            position = Math.Max(position, child.End + 1);
        }

        return position > tag.InnerEnd;
    }
}
