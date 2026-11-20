using System.Buffers;
using System.Runtime.CompilerServices;
using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser.Core.Markdown;

/// <summary>
/// HTML → Markdown (CommonMark with the GitHub tables, strikethrough and task lists) in one pass over the tag list.
/// </summary>
/// <remarks>
/// The tags are read as the parser recorded them, in document order; the text is what the source has between them.
/// Elements that end with something (a block, a list item, <c>**</c>) are kept on a stack of frames, by their position
/// in the source; all others (span, font, custom tags) need nothing: their end tags are skipped as markup in the text.
/// What is written before a piece of content (a line break, a blank line, the indent of a list, its marker, <c>**</c>)
/// is only noted and written when the content comes, so empty elements give nothing and no line ends with a space.
/// The time and the size of the result grow linearly with the page on any markup.
/// </remarks>
internal ref partial struct MarkdownConverter
{
    /// <summary>The whole document as Markdown.</summary>
    internal static string Convert(HtmlParser document, MarkdownOptions options)
    {
        return Convert(document, first: 0, root: -1, start: 0, end: int.MaxValue, options);
    }

    /// <summary>A tag as Markdown: the tag itself with its content.</summary>
    internal static string Convert(ParserTag tag, MarkdownOptions options)
    {
        return Convert(tag.Document, tag.Index, tag.Index, tag.Start, tag.End, options);
    }

    /// <summary>
    /// The Markdown of <c>Source[start..end]</c>; <paramref name="first"/> is the first tag that starts in it,
    /// <paramref name="root"/> the tag that was asked for, or -1.
    /// </summary>
    private static string Convert(HtmlParser document, int first, int root, int start, int end, MarkdownOptions options)
    {
        var data = document.Data;
        var source = document.Source;
        if (end >= source.Length) end = source.Length - 1;
        if (start > end) return string.Empty;

        var converter = new MarkdownConverter(document, data, source, options, end - start + 1);
        try
        {
            converter.Run(first, root, start, end);
            return converter._len == 0 ? string.Empty : new string(converter._buf, 0, converter._len);
        }
        finally
        {
            converter.Release();
        }
    }

    // All the fields are here: the parts of a partial struct have no defined order of fields among them.

    // the page
    private readonly HtmlParser _document;
    private readonly TagList _data;
    private readonly ReadOnlySpan<char> _source;
    private readonly bool _links, _images;
    private string? _pageUrl;
    private Uri? _pageUri;
    private bool _pageRead;
    private string? _pageSite;      // "https://example.com": what a link that starts with "/" is put after
    private string? _pageFolder;    // "https://example.com/dir/": the same for a link that starts with a name

    // the result, and text taken out of markup on its way there (a piece of text, the content of <code> or <pre>)
    private char[] _buf;
    private int _len;
    private char[] _scratch;

    // the open elements
    private Frame[] _frames;
    private int _frameCount;
    private int _listFrame;         // the innermost open list, or -1
    private int _lastListEnd;       // the length of the result when the last list ended: nothing is written since if it is the same
    private int _lastListDepth;     // the prefixes around that list
    private bool _lastListOrdered;
    private bool _lastListOther;    // it had the other marker

    // what is written before the next content
    private int _break;
    private int _breakDepth;        // how many of the prefixes the blank line keeps: those of the outermost block that asked for it
    private bool _pendingSpace;     // whitespace seen: one space before the next text
    private bool _lineEmpty;        // nothing is written on the line (in the table cell) yet: no space goes there
    private bool _blockStart;       // the next char is where Markdown looks for "#", "-", "1." and the like
    private int _headingPending;    // the level of a heading whose "## " is not written yet
    private int _headingStart;      // where the text of the heading starts in the result
    private bool _inHeading;

    // the prefixes of the lines: lists and quotes
    private PrefixList _prefixes;
    private int _prefixCount;
    private int _prefixWidth;       // the chars they put before every line
    private int _markerPending;     // markers not written yet
    private int _cellPrefixBase;    // the prefixes from here on are of lists inside the open table cell

    // The open inline pairs, outermost first. A pair starts with the first content inside it and ends with its element,
    // or before a block ends (then it starts again with the next content): the written ones are always the outer ones.
    private WrapperList _wraps;
    private int _wrapCount;
    private int _wrapsOpened;
    private int _wrapsToOpen;
    private int _active;            // a bit per kind: <b><b>x</b></b> is one pair
    private MdKind _lastClose;      // the "**", "*" or "~~" written last, to join it with the same one starting right after
    private int _lastClosePosition;
    private int _linkDepth;         // inside the text of a link or of an image: brackets are escaped
    private int _bracket;           // where the last plain ']' is in the result
    private int _closeRisk;         // 1: the "**", "*" or "~~" written last comes after a punctuation char, see BeginContentSlow
    private int _closePosition;     // where that marker ends in the result
    private char _closeMarker;      // its char
    private ReadOnlySpan<char> _linkUrl;    // of the open link
    private string? _linkPrefix;            // what its address is put after, see ResolveUrl
    private int _titleStart, _titleLength;  // its title in the source

    // One table at a time: a table with a table inside is not written as a table.
    private TableState _table;
    private bool _inTable;
    private bool _gap;              // in a table, outside its cells: nothing there is text of the table
    private int _cellDepth;         // 1 inside a cell
    private int[] _covered;         // per column, the rows below the current one that a cell with a rowspan covers
    private int _coveredCount;

    // The cells that may still be written without content, to make the rows of a table even. A row of 1000 columns
    // (colspan="1000") above 50,000 short rows would otherwise make megabytes of "|  |" out of a small page.
    private long _padBudget;

    private MarkdownConverter(HtmlParser document, TagList data, ReadOnlySpan<char> source, MarkdownOptions options, int length)
    {
        _document = document;
        _data = data;
        _source = source;
        _links = options.Links;
        _images = options.Images;

        _buf = ArrayPool<char>.Shared.Rent(Math.Max(256, length / 2));
        _scratch = [];
        _covered = [];
        _frames = ArrayPool<Frame>.Shared.Rent(32);
        _listFrame = -1;
        _lastListEnd = -1;
        _breakDepth = int.MaxValue;
        _bracket = -2;
        _lineEmpty = true;
        _blockStart = true;

        // Cells written only to make the rows of a table even, see ScanTable.
        _padBudget = 4L * length + 4096;
    }

    private void Release()
    {
        ArrayPool<char>.Shared.Return(_buf);
        ArrayPool<Frame>.Shared.Return(_frames);
        if (_scratch.Length > 0) ArrayPool<char>.Shared.Return(_scratch);
        if (_covered.Length > 0) ArrayPool<int>.Shared.Return(_covered);
    }

    // ----- the open elements -----

    private struct Frame
    {
        public int End;         // the last char of the element
        public int InnerEnd;    // the last char of its content
        public MdKind Kind;
        public int A, B, C;     // what its kind needs: a list its next number (-1: bullets), the list around it, its marker
    }

    private const int MaxListNumber = 999_999_999; // CommonMark: at most nine digits

    private void Push(in ParsingCore.TagNode node, MdKind kind, int a = 0, int b = 0, int c = 0)
    {
        if (_frameCount == _frames.Length)
        {
            var bigger = ArrayPool<Frame>.Shared.Rent(_frameCount * 2);
            _frames.AsSpan(0, _frameCount).CopyTo(bigger);
            ArrayPool<Frame>.Shared.Return(_frames);
            _frames = bigger;
        }

        _frames[_frameCount++] = new Frame { End = node.End, InnerEnd = node.InnerEnd, Kind = kind, A = a, B = b, C = c };
    }

    private void Run(int first, int root, int start, int end)
    {
        var data = _data;
        int count = data.Count;
        int pos = start; // the source is written up to here

        for (int i = first; i < count; i++)
        {
            ref readonly var node = ref data.Node(i);
            int tagStart = node.Start;
            if (tagStart > end) break;

            while (_frameCount > 0 && _frames[_frameCount - 1].End < tagStart) pos = CloseFrame(pos);
            Text(pos, tagStart);

            // a tag without an end (<br>, <img>, a comment) has no content
            bool container = node.InnerStart > 0 && node.InnerEnd > 0;
            pos = container ? node.InnerStart : node.End + 1;

            int key = node.Key;
            if (key < 0) continue; // a custom tag: only its content counts

            var kind = MarkdownKinds.Of[key];
            if (kind == MdKind.Inline) continue;

            if (_inTable && _table.RowOpen && _cellDepth == 0 && tagStart > _table.RowEnd) EndRow();

            if (OpenTag(i, in node, kind, container, i == root))
            {
                // written as a whole, or left out: the tags inside it are passed over
                int nodeEnd = node.End;
                pos = nodeEnd + 1;
                while (i + 1 < count && data.Node(i + 1).Start <= nodeEnd) i++;
            }
        }

        while (_frameCount > 0) pos = CloseFrame(pos);
        Text(pos, end + 1);
    }

    /// <summary>Returns true when the element is done with: its content is not to be read as tags and text.</summary>
    private bool OpenTag(int index, in ParsingCore.TagNode node, MdKind kind, bool container, bool isRoot)
    {
        if (_gap)
        {
            // Between the cells of a table only the parts of the table count, and what is wrapped around them (<b>, <a>).
            switch (kind)
            {
                case MdKind.Table:
                case MdKind.TableRow:
                case MdKind.TableCell:
                case MdKind.TablePart:
                case MdKind.Caption:
                case MdKind.Strong:
                case MdKind.Em:
                case MdKind.Del:
                case MdKind.Sub:
                case MdKind.Sup:
                case MdKind.Quote:
                case MdKind.Link:
                    break;

                case MdKind.Skip:
                case MdKind.Code:
                case MdKind.Pre:
                    return true;

                default:
                    return false;
            }
        }

        switch (kind)
        {
            case MdKind.Skip:
                return !isRoot; // asked for directly (title.Markdown()), it still gives its text

            case MdKind.Block:
                Break(Blank);
                if (container) Push(in node, MdKind.Block);
                return false;

            case MdKind.Heading:
                Break(Blank);
                if (!container) return false;
                if (_inHeading || _cellDepth > 0)
                {
                    Push(in node, MdKind.Block);
                    return false;
                }
                _inHeading = true;
                _headingPending = node.Key - (int)HtmlTag.H1 + 1;
                Push(in node, MdKind.Heading);
                return false;

            case MdKind.Strong:
            case MdKind.Em:
            case MdKind.Del:
            case MdKind.Sub:
            case MdKind.Sup:
            case MdKind.Quote:
                if (container && Activate(kind)) Push(in node, kind);
                return false;

            case MdKind.Link:
                // a link inside a link is its text
                if (container && _links && (_active & Bit(MdKind.Link)) == 0 && ReadLink(in node) && Activate(MdKind.Link))
                {
                    Push(in node, MdKind.Link);
                }
                return false;

            case MdKind.Image:
                if (_images) WriteImage(in node);
                return false;

            case MdKind.Code:
                if (!container) return false;
                WriteCode(in node);
                return true;

            case MdKind.Pre:
                if (!container) return false;
                if (_inHeading || _cellDepth > 0)
                {
                    // one line there: a code span, set apart as a block is
                    Break(Blank);
                    WriteCode(in node);
                    Break(Blank);
                }
                else
                {
                    WritePre(index, in node);
                }
                return true;

            case MdKind.Br:
                Break(Hard);
                return false;

            case MdKind.Hr:
                Break(Blank);
                if (_inHeading || _cellDepth > 0) return false; // no rule in one line: what it separates is set apart
                StartLine();
                Write("---");
                _lineEmpty = false;
                _blockStart = false;
                Break(Blank);
                return false;

            case MdKind.List:
            case MdKind.OrderedList:
            {
                // a list in a list item follows its text on the next line; any other list is a block of its own
                Break(_prefixCount > 0 && _prefixes[_prefixCount - 1].Width != 0 ? Line : Blank);
                if (!container) return false;

                // Two lists of one kind with nothing between them would be read as one list:
                // the second one gets the other marker ("*" for "-", "1)" for "1.").
                bool ordered = kind == MdKind.OrderedList;
                bool other = _lastListEnd == _len && _lastListOrdered == ordered && _lastListDepth == _prefixCount && !_lastListOther;

                Push(in node, MdKind.List, ordered ? ReadStart(in node) : -1, _listFrame, other ? 1 : 0);
                _listFrame = _frameCount - 1;
                return false;
            }

            case MdKind.ListItem:
            {
                Break(Line);
                _lastListEnd = -1;
                if (!container) return false;

                int marker = MarkerBullet, width = 2;
                bool other = false;
                if (_listFrame >= 0)
                {
                    ref var list = ref _frames[_listFrame];
                    other = list.C != 0;
                    if (list.A >= 0)
                    {
                        marker = MarkerNumber + list.A;
                        width = Digits(list.A) + 2;
                        if (list.A < MaxListNumber) list.A++;
                    }
                }

                if (!CanIndent(width))
                {
                    Push(in node, MdKind.ListItem);
                    return false;
                }

                PushPrefix(width, marker, other);
                Push(in node, MdKind.ListItem, 1);
                return false;
            }

            case MdKind.Blockquote:
                Break(Blank);
                if (!container) return false;
                if (!CanIndent(2) || _inHeading || _cellDepth > 0)
                {
                    Push(in node, MdKind.Block);
                    return false;
                }
                PushPrefix(0, 0, false);
                Push(in node, MdKind.Blockquote);
                return false;

            case MdKind.Term:
                Break(Blank);
                if (container) Push(in node, MdKind.Term);
                return false;

            case MdKind.Definition:
                Break(Line);
                if (!container) return false;
                if (!CanIndent(2))
                {
                    Push(in node, MdKind.ListItem);
                    return false;
                }
                PushPrefix(2, MarkerDefinition, false);
                Push(in node, MdKind.ListItem, 1);
                return false;

            case MdKind.Input:
                WriteCheckbox(in node);
                return false;

            case MdKind.Table:
                OpenTable(index, in node, container);
                return false;

            case MdKind.TableRow:
                OpenRow(in node, container);
                return false;

            case MdKind.TableCell:
                OpenCell(in node, container);
                return false;

            case MdKind.Caption:
                return OpenCaption(in node, container);

            default: // thead, tbody, tfoot
                return false;
        }
    }

    /// <summary>Ends the innermost open element; returns the position after it.</summary>
    private int CloseFrame(int pos)
    {
        var frame = _frames[--_frameCount];
        Text(pos, frame.InnerEnd + 1);

        switch (frame.Kind)
        {
            case MdKind.Block:
                Break(Blank);
                break;

            case MdKind.Heading:
            {
                bool written = _headingPending == 0;
                _inHeading = false;
                _headingPending = 0;
                _pendingSpace = false;
                Break(Blank);
                if (written) EscapeHeadingEnd();
                break;
            }

            case MdKind.Strong:
            case MdKind.Em:
            case MdKind.Del:
            case MdKind.Sub:
            case MdKind.Sup:
            case MdKind.Quote:
            case MdKind.Link:
                Deactivate(frame.Kind);
                break;

            case MdKind.List:
                _listFrame = frame.B;
                _lastListEnd = _len;
                _lastListOrdered = frame.A >= 0;
                _lastListOther = frame.C != 0;
                _lastListDepth = _prefixCount;
                Break(Blank);
                break;

            case MdKind.ListItem:
                // A blank line asked for at the end of an item (</p></li>, </ul></li>) is not one between the items.
                if (frame.A != 0 && PopPrefix() && _break > Line) _break = Line;
                _lastListEnd = -1; // a list in the next item is not a neighbour of the one that ended in this item
                Break(Line);
                break;

            case MdKind.Blockquote:
                PopPrefix();
                Break(Blank);
                break;

            case MdKind.Term:
                Break(Line);
                break;

            case MdKind.Table:
                CloseTable();
                break;

            case MdKind.TableCell:
                CloseCell(frame.A, frame.B);
                break;

            case MdKind.Caption:
                Break(Blank);
                _gap = true;
                break;
        }

        return frame.End + 1;
    }

    // ----- text -----

    private void EnsureScratch(int length)
    {
        if (_scratch.Length >= length) return;

        if (_scratch.Length > 0) ArrayPool<char>.Shared.Return(_scratch);
        _scratch = ArrayPool<char>.Shared.Rent(length);
    }

    /// <summary>Writes the source text <c>[from, to)</c>: what is between two tags.</summary>
    private void Text(int from, int to)
    {
        if (to <= from || _gap) return;

        var text = _source.Slice(from, to - from);
        if (text.IndexOfAny('<', '&') >= 0) text = Clean(text);
        WriteText(text);
    }

    /// <summary>
    /// The text without what is left of the markup in it (end tags, a tag cut off by the next one) and with
    /// its character references decoded.
    /// </summary>
    private ReadOnlySpan<char> Clean(ReadOnlySpan<char> text)
    {
        EnsureScratch(text.Length); // decoding never makes the text longer
        var destination = _scratch;
        int length = 0;
        Span<char> decoded = stackalloc char[2];

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<')
            {
                if (HtmlToTextCore.IsMarkupStart(text, i))
                {
                    int close = text.Slice(i + 1).IndexOf('>');
                    if (close < 0) break; // an unfinished tag: the rest is markup
                    i += close + 1;
                    continue;
                }
            }
            else if (c == '&')
            {
                int consumed = HtmlDecodeCore.DecodeReference(text.Slice(i + 1), decoded, out int written);
                if (consumed > 0)
                {
                    destination[length++] = decoded[0];
                    if (written == 2) destination[length++] = decoded[1];
                    i += consumed;
                    continue;
                }
            }

            destination[length++] = c;
        }

        return destination.AsSpan(0, length);
    }

    /// <summary>
    /// The text of <c>Source[from..to)</c> in <see cref="_scratch"/>: tags, comments and invisible content removed,
    /// character references decoded. Whitespace is collapsed and trimmed, or with <paramref name="preserve"/>
    /// kept as written (line breaks as LF, &lt;br&gt; as a line break). <paramref name="raw"/>: the content is text
    /// only, nothing in it is a tag or a character reference.
    /// </summary>
    private int CollectText(int from, int to, bool preserve, bool raw = false)
    {
        if (to <= from) return 0;

        var text = _source.Slice(from, to - from);
        EnsureScratch(text.Length);
        var destination = _scratch;
        int length = 0;
        bool space = false;
        Span<char> decoded = stackalloc char[2];

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<' && !raw && HtmlToTextCore.IsMarkupStart(text, i))
            {
                if (preserve && IsLineBreakTag(text, i)) destination[length++] = '\n';

                int last = HtmlToTextCore.SkipMarkup(text, i);
                if (last < 0) break;
                i = last;
                continue;
            }

            char second = '\0';
            if (c == '&' && !raw)
            {
                int consumed = HtmlDecodeCore.DecodeReference(text.Slice(i + 1), decoded, out int written);
                if (consumed > 0)
                {
                    c = decoded[0];
                    if (written == 2) second = decoded[1];
                    i += consumed;
                }
            }

            if (preserve)
            {
                if (c == '\r')
                {
                    // CR LF and a lone CR are line breaks, as a browser reads the page
                    if (i + 1 < text.Length && text[i + 1] == '\n') continue;
                    c = '\n';
                }
                destination[length++] = c;
            }
            else if (c <= ' ' || char.IsWhiteSpace(c))
            {
                space = length > 0;
                continue;
            }
            else
            {
                if (space)
                {
                    destination[length++] = ' ';
                    space = false;
                }
                destination[length++] = c;
            }

            if (second != '\0') destination[length++] = second;
        }

        return length;
    }

    /// <summary>Whether the tag at <paramref name="i"/> is a &lt;br&gt;.</summary>
    private static bool IsLineBreakTag(ReadOnlySpan<char> text, int i)
    {
        return i + 3 < text.Length
            && (text[i + 1] | 0x20) == 'b'
            && (text[i + 2] | 0x20) == 'r'
            && (text[i + 3] is '>' or '/' || char.IsWhiteSpace(text[i + 3]));
    }

    // ----- attributes -----

    /// <summary>Position of the '&gt;' that ends the opening tag.</summary>
    private static int OpenTagEnd(in ParsingCore.TagNode node)
    {
        return node.InnerStart > 0 && node.InnerEnd > 0 ? node.InnerStart - 1 : node.End;
    }

    /// <summary>The value of the first attribute with that name as written, or nothing.</summary>
    private ReadOnlySpan<char> Attribute(in ParsingCore.TagNode node, string name)
    {
        var scanner = new AttributeScanner(_source, node.Start, OpenTagEnd(in node));
        while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
        {
            if (nameLength == name.Length && _source.Slice(nameStart, nameLength).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return valueStart < 0 ? default : _source.Slice(valueStart, valueLength);
            }
        }

        return default;
    }

    private static ReadOnlySpan<char> Decoded(ReadOnlySpan<char> raw)
    {
        return HtmlDecodeCore.NeedsDecoding(raw) ? SpanHandlingCore.DecodeAttribute(raw) : raw;
    }

    /// <summary>The number an ordered list starts with.</summary>
    private int ReadStart(in ParsingCore.TagNode node)
    {
        var value = Attribute(in node, "start").Trim();
        if (value.IsEmpty || value.Length > 9) return 1;

        int start = 0;
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c)) return 1;
            start = start * 10 + (c - '0');
        }
        return start;
    }

    // ----- links and images -----

    /// <summary>Reads href and title of an &lt;a&gt;; false when it is not a link to write (no address, a script).</summary>
    private bool ReadLink(in ParsingCore.TagNode node)
    {
        ReadOnlySpan<char> href = default;
        bool hasHref = false, hasTitle = false;
        int titleStart = 0, titleLength = 0;

        var scanner = new AttributeScanner(_source, node.Start, OpenTagEnd(in node));
        while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
        {
            if (nameLength == 4)
            {
                if (hasHref || !_source.Slice(nameStart, 4).Equals("href", StringComparison.OrdinalIgnoreCase)) continue;
                hasHref = true;
                if (valueStart >= 0) href = _source.Slice(valueStart, valueLength);
            }
            else if (nameLength == 5)
            {
                if (hasTitle || !_source.Slice(nameStart, 5).Equals("title", StringComparison.OrdinalIgnoreCase)) continue;
                hasTitle = true;
                if (valueStart >= 0)
                {
                    titleStart = valueStart;
                    titleLength = valueLength;
                }
            }
        }

        if (!hasHref) return false;

        var url = ResolveUrl(href, out string? prefix);
        if (!IsUsableUrl(url)) return false;

        _linkUrl = url;
        _linkPrefix = prefix;
        _titleStart = titleStart;
        _titleLength = titleLength;
        return true;
    }

    /// <summary>
    /// The address as <c>AttributeHref()</c> gives it: character references decoded and, when the page has a URL,
    /// made absolute: <paramref name="prefix"/>, if there is one, followed by what is returned.
    /// Nothing for an empty address and for "#".
    /// </summary>
    private ReadOnlySpan<char> ResolveUrl(ReadOnlySpan<char> raw, out string? prefix)
    {
        prefix = null;
        raw = raw.Trim();
        if (raw.IsEmpty || raw is "#") return default;

        bool decode = HtmlDecodeCore.NeedsDecoding(raw);
        if (!decode && (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return raw; // already what it would be resolved to
        }

        if (!_pageRead) ReadPage();
        if (_pageUri == null) return decode ? SpanHandlingCore.DecodeAttribute(raw) : raw;

        // Most links of a page are plain paths ("/catalogue/page-2.html", "item.html?id=5"): resolved, they are
        // the address of the site or of the folder of the page followed by the link as it is written.
        // Nothing is made for them; any other link is resolved as AttributeHref() resolves it.
        if (!decode && IsPlainPath(raw))
        {
            prefix = raw[0] == '/' ? _pageSite : _pageFolder;
            if (prefix != null) return raw;
        }

        return UriCore.CorrectLink(raw, _pageUrl!, _pageUri);
    }

    private void ReadPage()
    {
        _pageRead = true;
        _pageUrl = _document.PageUrl;
        _pageUri = _document.PageUri;
        if (_pageUri != null) _document.GetPagePrefixes(out _pageSite, out _pageFolder);
    }

    private static readonly SearchValues<char> PlainPathChars =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~/?=&#+,;");

    /// <summary>
    /// A link that is made absolute by putting it after the address of the site or of the folder of the page:
    /// a path of plain chars with nothing to resolve in it (no "." or ".." names, no "//", no percent-escapes),
    /// that starts with "/" or with a name.
    /// </summary>
    private static bool IsPlainPath(ReadOnlySpan<char> link)
    {
        char first = link[0];
        if (first == '/' ? link.Length > 1 && link[1] == '/' : !char.IsAsciiLetterOrDigit(first)) return false;
        if (link.ContainsAnyExcept(PlainPathChars)) return false;

        // a name that is or ends with a dot, an empty name, an empty query or fragment
        return !link.Contains("/.", StringComparison.Ordinal) && !link.Contains("./", StringComparison.Ordinal)
            && !link.Contains("//", StringComparison.Ordinal) && link[^1] is not ('.' or '?' or '#')
            && !link.Contains(".?", StringComparison.Ordinal) && !link.Contains(".#", StringComparison.Ordinal);
    }

    /// <summary>An address worth writing: not a script and not a file packed into the page.</summary>
    private static bool IsUsableUrl(ReadOnlySpan<char> url)
    {
        if (url.IsEmpty) return false;

        // every scheme left out has a ':' among its first chars
        int colon = url.Slice(0, Math.Min(url.Length, 12)).IndexOf(':');
        if (colon < 0) return true;

        var scheme = url.Slice(0, colon);
        return !scheme.Equals("javascript", StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals("vbscript", StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals("data", StringComparison.OrdinalIgnoreCase);
    }

    private void WriteImage(in ParsingCore.TagNode node)
    {
        ReadOnlySpan<char> src = default, lazy = default, alt = default, title = default;
        int lazyRank = 0;

        var scanner = new AttributeScanner(_source, node.Start, OpenTagEnd(in node));
        while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
        {
            if (valueStart < 0 || valueLength == 0) continue;

            var name = _source.Slice(nameStart, nameLength);
            var value = _source.Slice(valueStart, valueLength);
            switch (nameLength)
            {
                case 3:
                    if (src.IsEmpty && name.Equals("src", StringComparison.OrdinalIgnoreCase)) src = value;
                    else if (alt.IsEmpty && name.Equals("alt", StringComparison.OrdinalIgnoreCase)) alt = value;
                    break;

                case 5:
                    if (title.IsEmpty && name.Equals("title", StringComparison.OrdinalIgnoreCase)) title = value;
                    break;

                // Images loaded by a script when they scroll into view: the real address is in one of these,
                // and src, if there is one, is a placeholder.
                case 8:
                    if (lazyRank < 3 && name.Equals("data-src", StringComparison.OrdinalIgnoreCase))
                    {
                        lazy = value;
                        lazyRank = 3;
                    }
                    break;

                case 13:
                    if (lazyRank < 2 && name.Equals("data-lazy-src", StringComparison.OrdinalIgnoreCase))
                    {
                        lazy = value;
                        lazyRank = 2;
                    }
                    else if (lazyRank < 1 && name.Equals("data-original", StringComparison.OrdinalIgnoreCase))
                    {
                        lazy = value;
                        lazyRank = 1;
                    }
                    break;
            }
        }

        var url = ResolveUrl(lazy, out string? prefix);
        if (!IsUsableUrl(url))
        {
            url = ResolveUrl(src, out prefix);
            if (!IsUsableUrl(url)) return;
        }

        BeginContent('!');
        Write("![");
        _lineEmpty = false;
        _blockStart = false;

        var text = Decoded(alt).Trim();
        if (!text.IsEmpty)
        {
            _linkDepth++;
            WriteText(text);
            _linkDepth--;
            _pendingSpace = false;
        }

        Write("](");
        WriteUrl(prefix, url);
        if (!title.IsEmpty) WriteTitle(title);
        Write(')');
    }

    /// <summary>A checkbox that starts a list item makes it a task: <c>- [x] done</c>.</summary>
    private void WriteCheckbox(in ParsingCore.TagNode node)
    {
        if (_prefixCount == 0 || _prefixes[_prefixCount - 1].Marker is 0 or MarkerDefinition) return;

        bool isCheckbox = false, isChecked = false;
        var scanner = new AttributeScanner(_source, node.Start, OpenTagEnd(in node));
        while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
        {
            var name = _source.Slice(nameStart, nameLength);
            if (nameLength == 4 && valueStart >= 0 && name.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                isCheckbox = _source.Slice(valueStart, valueLength).Trim().Equals("checkbox", StringComparison.OrdinalIgnoreCase);
            }
            else if (nameLength == 7 && name.Equals("checked", StringComparison.OrdinalIgnoreCase))
            {
                isChecked = true;
            }
        }

        if (!isCheckbox) return;

        BeginContent('['); // the marker of the item
        Write(isChecked ? "[x]" : "[ ]");
        _lineEmpty = false;
        _blockStart = false;
        _pendingSpace = true;
    }

    // ----- code -----

    /// <summary>&lt;code&gt;, &lt;kbd&gt;, &lt;samp&gt;, &lt;tt&gt;: a code span.</summary>
    private void WriteCode(in ParsingCore.TagNode node)
    {
        int length = CollectText(node.InnerStart, node.InnerEnd + 1, preserve: false);
        if (length == 0) return;

        ReadOnlySpan<char> code = _scratch.AsSpan(0, length);

        // the fence is one backtick longer than the longest run of them in the code
        int fence = LongestRun(code, '`') + 1;
        bool pad = code[0] == '`' || code[^1] == '`';

        BeginContent('`');
        Ensure(code.Length * 2 + fence * 2 + 2);
        WriteRepeat('`', fence);
        if (pad) Write(' ');

        if (_cellDepth > 0 && code.Contains('|'))
        {
            // a table is split into cells before its code spans are read
            foreach (char c in code)
            {
                if (c == '|') Write('\\');
                Write(c);
            }
        }
        else
        {
            Write(code);
        }

        if (pad) Write(' ');
        WriteRepeat('`', fence);
        _lineEmpty = false;
        _blockStart = false;
    }

    /// <summary>&lt;pre&gt;: a fenced code block, with the language of <c>class="language-…"</c>.</summary>
    private void WritePre(int index, in ParsingCore.TagNode node)
    {
        // the content of <xmp> is text as written, with its tags and character references
        int length = CollectText(node.InnerStart, node.InnerEnd + 1, preserve: true, raw: ParsingCore.IsRawText(node.Key));
        ReadOnlySpan<char> code = _scratch.AsSpan(0, length);

        // a line break right after <pre> is not part of its text
        if (code.StartsWith('\n')) code = code.Slice(1);
        code = code.TrimEnd();
        if (code.IsEmpty) return;

        var language = ReadLanguage(index, in node);

        Break(Blank);
        StartLine();

        int fence = Math.Max(3, LongestRun(code, '`') + 1);
        WriteRepeat('`', fence);
        Write(language);

        while (true)
        {
            int lineEnd = code.IndexOf('\n');
            var line = lineEnd < 0 ? code : code.Slice(0, lineEnd);

            NewLine(blank: line.IsEmpty);
            Write(line);

            if (lineEnd < 0) break;
            code = code.Slice(lineEnd + 1);
        }

        NewLine(blank: false);
        WriteRepeat('`', fence);
        _lineEmpty = false;
        _blockStart = false;
        Break(Blank);
    }

    /// <summary>The language of a code block: <c>class="language-x"</c> or <c>lang-x</c> on the &lt;pre&gt; or on its &lt;code&gt;.</summary>
    private ReadOnlySpan<char> ReadLanguage(int index, in ParsingCore.TagNode node)
    {
        var language = LanguageOf(Attribute(in node, "class"));
        if (!language.IsEmpty || index + 1 >= _data.Count) return language;

        ref readonly var child = ref _data.Node(index + 1);
        return child.Start < node.End && child.Key == (int)HtmlTag.Code ? LanguageOf(Attribute(in child, "class")) : default;
    }

    private static ReadOnlySpan<char> LanguageOf(ReadOnlySpan<char> classes)
    {
        while (!classes.IsEmpty)
        {
            classes = classes.TrimStart();
            int end = 0;
            while (end < classes.Length && !char.IsWhiteSpace(classes[end])) end++;

            var name = classes.Slice(0, end);
            classes = classes.Slice(end);

            int prefix = name.StartsWith("language-", StringComparison.OrdinalIgnoreCase) ? 9
                : name.StartsWith("lang-", StringComparison.OrdinalIgnoreCase) ? 5 : 0;
            if (prefix == 0 || name.Length == prefix || name.Length > prefix + 32) continue;

            name = name.Slice(prefix);
            if (name.ContainsAnyExcept(LanguageChars)) continue;
            return name;
        }

        return default;
    }

    private static readonly SearchValues<char> LanguageChars =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+-#._");

    private static int LongestRun(ReadOnlySpan<char> text, char c)
    {
        int longest = 0;
        for (int i = text.IndexOf(c); i >= 0 && i < text.Length;)
        {
            int run = 1;
            while (i + run < text.Length && text[i + run] == c) run++;
            if (run > longest) longest = run;

            int next = text.Slice(i + run).IndexOf(c);
            if (next < 0) break;
            i += run + next;
        }
        return longest;
    }

    private static int Digits(int number)
    {
        int digits = 1;
        while (number >= 10)
        {
            number /= 10;
            digits++;
        }
        return digits;
    }
}
