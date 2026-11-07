using System.Collections;
using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>
/// One tag of a parsed document. Positions are indexes into <see cref="HtmlParser.PageSource"/>.
/// Enumerating a tag yields all tags inside it (descendants) in document order.
/// </summary>
public class ParserTag : IEnumerable<ParserTag>, ITagSearchable
{
    private readonly int _key; // (int)HtmlTag, or -1 for a name that is not in HtmlTag
    private readonly string? _customName;

    internal ParserTag(HtmlParser document, int index, HtmlTag? tag, string? customName, int start, int end, int innerStart, int depth)
    {
        Document = document;
        Index = index;
        _key = tag is { } known ? (int)known : -1;
        _customName = customName;
        Start = start;
        End = end;
        InnerStart = innerStart;
        Depth = depth;
    }

    /// <summary>The document this tag belongs to.</summary>
    public HtmlParser Document { get; }

    /// <summary>Position of the opening '&lt;'.</summary>
    public int Start { get; }

    /// <summary>Position of the last '&gt;': of the closing tag when there is one, otherwise of the opening tag.</summary>
    public int End { get; internal set; }

    /// <summary>Position right after the opening tag; 0 when the tag is not closed.</summary>
    public int InnerStart { get; internal set; }

    /// <summary>Position of the last char before the closing tag; 0 when the tag is not closed.</summary>
    public int InnerEnd { get; internal set; }

    /// <summary>Nesting depth: 0 for top-level tags.</summary>
    public int Depth { get; }

    /// <summary>Lower-case tag name ("div", "!--" for comments, "custom-tag", ...).</summary>
    public string? Name => _customName ?? (_key >= 0 ? ParsingCore.TagNames[_key] : null);

    /// <summary>Whether another tag starts inside this one.</summary>
    public bool HasChildren()
    {
        var data = Document.Data;
        if (Index + 1 >= data.Count) return false; // last tag

        return data[Index + 1].Start < End;
    }

    /// <summary>Position in the document's tag list.</summary>
    internal int Index { get; }

    /// <summary>The tag as <see cref="HtmlTag"/>, or null for names not in the enum.</summary>
    internal HtmlTag? Tag => _key >= 0 ? (HtmlTag)_key : null;

    /// <summary>(int)<see cref="HtmlTag"/>, or -1: compared directly by <see cref="TagQuery"/>.</summary>
    internal int Key => _key;

    /// <summary>The lower-case name for tags not in <see cref="HtmlTag"/>.</summary>
    internal string? CustomName => _customName;

    // IEnumerable: descendants

    /// <summary>Enumerates all tags inside this one, in document order.</summary>
    public IEnumerator<ParserTag> GetEnumerator()
    {
        var data = Document.Data;
        for (int i = Index + 1, count = data.Count; i < count; i++)
        {
            var tag = data[i];
            if (tag.End > End) break;

            yield return tag;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // ITagSearchable: searches the descendants

    void ITagSearchable.GetScope(out List<ParserTag> tags, out int first, out int end)
    {
        tags = Document.Data;
        first = Index + 1;
        end = End;
    }
}
