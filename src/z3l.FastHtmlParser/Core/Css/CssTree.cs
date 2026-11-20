namespace z3l.FastHtmlParser.Core.Css;

/// <summary>
/// Sibling links and positions of the elements of a document, for the sibling combinators (<c>+</c>, <c>~</c>)
/// and the structural pseudo-classes (<c>:nth-child</c>, <c>:last-of-type</c>, ...). Built once per document
/// on the first selector that needs it (one pass over the tag list), then shared by every query on that document.
/// Comments, the doctype and the XML declaration are not elements: they have no position and are skipped.
/// </summary>
internal sealed class CssTree
{
    private readonly TagList _data;
    private readonly int[] _previous;  // index of the previous element sibling, -1 for none
    private readonly int[] _next;      // index of the next element sibling, -1 for none
    private readonly int[] _position;  // 1-based position among the element siblings
    private readonly int[] _fromEnd;   // 1-based position counted from the last element sibling
    private TypePositions? _types;     // the same for siblings with the same name, built when first needed

    private sealed class TypePositions(int[] position, int[] fromEnd)
    {
        public readonly int[] Position = position;
        public readonly int[] FromEnd = fromEnd;
    }

    /// <summary>The tree of <paramref name="document"/>, built on the first call.</summary>
    public static CssTree For(HtmlParser document)
    {
        // A benign race: two threads may build it at the same time, both results are the same.
        return Volatile.Read(ref document.CssTree) ?? Publish(document, new CssTree(document.Data));
    }

    private static CssTree Publish(HtmlParser document, CssTree tree)
    {
        return Interlocked.CompareExchange(ref document.CssTree, tree, null) ?? tree;
    }

    private CssTree(TagList data)
    {
        _data = data;
        int count = data.Count;
        _previous = new int[count];
        _next = new int[count];
        _position = new int[count];
        _fromEnd = new int[count];

        // per parent (slot = parent index + 1, slot 0: the top level): its last element child so far and their number
        var lastChild = new int[count + 1];
        var children = new int[count + 1];
        Array.Fill(lastChild, -1);

        for (int i = 0; i < count; i++)
        {
            _previous[i] = _next[i] = -1;

            ref readonly var tag = ref data.Node(i);
            if (!tag.IsElement) continue;

            int slot = tag.ParentIndex + 1;
            int previous = lastChild[slot];
            _previous[i] = previous;
            if (previous >= 0) _next[previous] = i;
            lastChild[slot] = i;
            _position[i] = ++children[slot];
        }

        for (int i = 0; i < count; i++)
        {
            if (_position[i] > 0) _fromEnd[i] = children[data.Node(i).ParentIndex + 1] - _position[i] + 1;
        }
    }

    public ParserTag? Previous(ParserTag tag) => At(_previous[tag.Index]);

    /// <summary>Index of the previous element sibling, -1 for none.</summary>
    public int PreviousIndex(int index) => _previous[index];

    public ParserTag? Next(ParserTag tag) => At(_next[tag.Index]);

    /// <summary>1-based position among the element siblings.</summary>
    public int Position(ParserTag tag) => _position[tag.Index];

    /// <summary>1-based position among the element siblings, counted from the last one.</summary>
    public int PositionFromEnd(ParserTag tag) => _fromEnd[tag.Index];

    /// <summary>1-based position among the element siblings with the same name.</summary>
    public int TypePosition(ParserTag tag) => Types().Position[tag.Index];

    /// <summary>1-based position among the element siblings with the same name, counted from the last one.</summary>
    public int TypePositionFromEnd(ParserTag tag) => Types().FromEnd[tag.Index];

    private ParserTag? At(int index) => index < 0 ? null : _data[index];

    private TypePositions Types()
    {
        var types = Volatile.Read(ref _types);
        if (types != null) return types;

        int count = _data.Count;
        var position = new int[count];
        var fromEnd = new int[count];
        var seen = new Dictionary<(int Parent, int Key, string? Name), int>();

        for (int i = 0; i < count; i++)
        {
            if (_position[i] == 0) continue; // not an element
            ref readonly var tag = ref _data.Node(i);

            var key = (tag.ParentIndex, tag.Key, tag.Key >= 0 ? null : tag.CustomName);
            seen.TryGetValue(key, out int n);
            seen[key] = position[i] = n + 1;
        }

        for (int i = 0; i < count; i++)
        {
            if (position[i] == 0) continue;

            ref readonly var tag = ref _data.Node(i);
            fromEnd[i] = seen[(tag.ParentIndex, tag.Key, tag.Key >= 0 ? null : tag.CustomName)] - position[i] + 1;
        }

        types = new TypePositions(position, fromEnd);
        return Interlocked.CompareExchange(ref _types, types, null) ?? types;
    }
}
