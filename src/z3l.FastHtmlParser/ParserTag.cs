using System.Collections;
using System.Runtime.CompilerServices;
using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>
/// One tag of a parsed document. Positions are indexes into <see cref="HtmlParser.PageSource"/>.
/// Enumerating a tag yields all tags inside it (descendants) in document order.
/// </summary>
public class ParserTag : IEnumerable<ParserTag>, ITagSearchable
{
    // The chunk of structs the parser recorded this tag in: the tag keeps it, so what it holds itself is still there
    // after Dispose, and reads it with one array access.
    private readonly ParsingCore.TagNode[] _chunk;
    private readonly int _slot;

    internal ParserTag(HtmlParser document, ParsingCore.TagNode[] chunk, int index)
    {
        Document = document;
        _chunk = chunk;
        _slot = index & (TagList.ChunkSize - 1);
        Index = index;
    }

    internal ref readonly ParsingCore.TagNode Node
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _chunk[_slot];
    }

    /// <summary>The document this tag belongs to.</summary>
    public HtmlParser Document { get; }

    /// <summary>Position of the opening '&lt;'.</summary>
    public int Start => Node.Start;

    /// <summary>Position of the last '&gt;': of the closing tag when there is one, otherwise of the opening tag.</summary>
    public int End => Node.End;

    /// <summary>Position right after the opening tag; 0 when the tag is not closed.</summary>
    public int InnerStart => Node.InnerStart;

    /// <summary>Position of the last char before the closing tag; 0 when the tag is not closed.</summary>
    public int InnerEnd => Node.InnerEnd;

    /// <summary>Nesting depth: 0 for top-level tags.</summary>
    public int Depth => Node.Depth;

    /// <summary>Lower-case tag name ("div", "!--" for comments, "custom-tag", ...).</summary>
    public string? Name => Node.CustomName ?? (Node.Key >= 0 ? ParsingCore.TagNames[Node.Key] : null);

    /// <summary>Whether another tag starts inside this one.</summary>
    public bool HasChildren()
    {
        var data = Document.Data;
        if (Index + 1 >= data.Count) return false; // last tag

        return data[Index + 1].Start < End;
    }

    /// <summary>Position in the document's tag list.</summary>
    internal int Index { get; }

    /// <summary>Position of the enclosing tag in the document's tag list, or -1 for top-level tags.</summary>
    internal int ParentIndex => Node.ParentIndex;

    /// <summary>The tag as <see cref="HtmlTag"/>, or null for names not in the enum.</summary>
    internal HtmlTag? Tag => Node.Key is var key && key >= 0 ? (HtmlTag)key : null;

    /// <summary>(int)<see cref="HtmlTag"/>, or -1: compared directly by <see cref="TagQuery"/>.</summary>
    internal int Key => Node.Key;

    /// <summary>The lower-case name for tags not in <see cref="HtmlTag"/>.</summary>
    internal string? CustomName => Node.CustomName;

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

    void ITagSearchable.GetScope(out TagList tags, out int first, out int end)
    {
        tags = Document.Data;
        first = Index + 1;
        end = End;
    }
}
