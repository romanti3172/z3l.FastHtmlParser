using System.Runtime.CompilerServices;

namespace z3l.FastHtmlParser.Core;

/// <summary>
/// The tags of a document in document order. The parser records them as <see cref="ParsingCore.TagNode"/> structs;
/// the <see cref="ParserTag"/> object of a tag is made when it is first asked for, and then stays the same object.
/// Searches test the structs and make objects only for the tags they return.
/// </summary>
/// <remarks>
/// The structs are kept in chunks of <see cref="ChunkSize"/> (64 KB): a big page does not put one array on the
/// large object heap, which only a gen2 collection frees. Most pages fit in the first chunk, read with one array access.
/// </remarks>
internal sealed class TagList
{
    public const int ChunkShift = 11;
    public const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

    public static TagList Empty(HtmlParser document) => new(document, []);

    private readonly HtmlParser _document;
    private ParsingCore.TagNode[] _first;          // = _chunks[0]
    private ParsingCore.TagNode[][] _chunks;
    private ParserTag?[]?[]? _objects;             // per chunk, made when a tag of it is first asked for

    /// <summary>Copies the recorded tags into chunks.</summary>
    public TagList(HtmlParser document, ReadOnlySpan<ParsingCore.TagNode> nodes)
    {
        _document = document;
        Count = nodes.Length;

        int chunkCount = (nodes.Length + ChunkMask) >> ChunkShift;
        _chunks = new ParsingCore.TagNode[chunkCount][];
        for (int c = 0; c < chunkCount; c++)
        {
            _chunks[c] = nodes.Slice(c << ChunkShift, Math.Min(ChunkSize, nodes.Length - (c << ChunkShift))).ToArray();
        }
        _first = chunkCount > 0 ? _chunks[0] : [];
    }

    public int Count { get; private set; }

    /// <summary>The recorded tag at <paramref name="index"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly ParsingCore.TagNode Node(int index)
    {
        var first = _first;
        if ((uint)index < (uint)first.Length) return ref first[index];
        return ref FarNode(index);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ref ParsingCore.TagNode FarNode(int index)
    {
        if ((uint)index >= (uint)Count) ThrowIndex();
        return ref _chunks[index >> ChunkShift][index & ChunkMask];
    }

    /// <summary>The tag object at <paramref name="index"/>, made on the first request (one object per tag, thread-safe).</summary>
    public ParserTag this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)Count) ThrowIndex();
            var objects = _objects;
            if (objects != null && objects[index >> ChunkShift] is { } chunk && chunk[index & ChunkMask] is { } tag) return tag;
            return Make(index);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ParserTag Make(int index)
    {
        var objects = _objects ?? Interlocked.CompareExchange(ref _objects, new ParserTag?[]?[_chunks.Length], null) ?? _objects!;
        int c = index >> ChunkShift;
        var nodes = _chunks[c];
        var chunk = objects[c] ?? Interlocked.CompareExchange(ref objects[c], new ParserTag?[nodes.Length], null) ?? objects[c]!;

        var tag = new ParserTag(_document, nodes, index);
        return Interlocked.CompareExchange(ref chunk[index & ChunkMask], tag, null) ?? tag;
    }

    public IEnumerator<ParserTag> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    public void Clear()
    {
        Count = 0;
        _first = [];
        _chunks = [];
        _objects = null;
    }

    private static void ThrowIndex() => throw new ArgumentOutOfRangeException("index");
}
