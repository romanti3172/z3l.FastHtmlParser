using System.Buffers;
using System.Runtime.CompilerServices;

namespace z3l.FastHtmlParser.Core.Markdown;

// What is written and when: line breaks, the prefixes of lists and quotes, the inline pairs, escaped text.
internal ref partial struct MarkdownConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Ensure(int extra)
    {
        if (_buf.Length - _len < extra) Grow(extra);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int extra)
    {
        var bigger = ArrayPool<char>.Shared.Rent(Math.Max(_buf.Length * 2, _len + extra));
        _buf.AsSpan(0, _len).CopyTo(bigger);
        ArrayPool<char>.Shared.Return(_buf);
        _buf = bigger;
    }

    private void Write(char c)
    {
        Ensure(1);
        _buf[_len++] = c;
    }

    private void Write(scoped ReadOnlySpan<char> text)
    {
        Ensure(text.Length);
        text.CopyTo(_buf.AsSpan(_len));
        _len += text.Length;
    }

    private void WriteRepeat(char c, int count)
    {
        Ensure(count);
        _buf.AsSpan(_len, count).Fill(c);
        _len += count;
    }

    // ----- breaks -----

    // What separates the next content from what is written, in rising order.
    private const int None = 0;
    private const int Hard = 1;     // <br>: a line break inside a paragraph
    private const int Line = 2;     // the next line: a list item, a table row
    private const int Blank = 3;    // a blank line: the next block

    /// <summary>Asks for a break before the next content. Nothing is written until that content comes.</summary>
    private void Break(int level)
    {
        // Not between the cells of a table, and not before the first content of a list item: "- " and its text are one line.
        if (_gap || _markerPending > 0) return;

        if (_inHeading)
        {
            // a heading is one line
            if (_headingPending == 0) _pendingSpace = true;
            return;
        }

        // "**" and "[…](…)" do not reach over blocks: they end here and start again with the next text
        if (level >= Line && _wrapsOpened > 0) CloseWrappers();

        // <br><br> is a paragraph; where a block starts or ends anyway, a <br> adds nothing
        if (level == Hard && _break != None) level = _break == Hard ? Blank : _break;

        if (level > _break) _break = level;
        if (_prefixCount < _breakDepth) _breakDepth = _prefixCount;
        _pendingSpace = false;
    }

    /// <summary>Before any content that is not text; <paramref name="next"/> is its first char.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BeginContent(char next)
    {
        if ((_break | _markerPending | _headingPending | _wrapsToOpen | _closeRisk) != 0 || _pendingSpace) BeginContentSlow(next);
    }

    /// <summary>
    /// Writes what was put off until some content comes: the break, the prefixes, a space, the inline pairs.
    /// <paramref name="next"/> is the first char of that content.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BeginContentSlow(char next)
    {
        if ((_break | _markerPending | _headingPending) != 0)
        {
            FlushBlockStart();
        }
        else if (_pendingSpace)
        {
            if (!_lineEmpty) Write(' ');
            _pendingSpace = false;
        }

        if (_closeRisk != 0)
        {
            // "**" after a punctuation char ends the emphasis only before whitespace or punctuation ("right-flanking"):
            // in "**Note:**text" it would stay two asterisks. An empty comment after it is such punctuation.
            if (_closePosition == _len && !IsSpace(next) && !IsPunctuation(next)) Write(EmptyComment);
            _closeRisk = 0;
        }

        if (_wrapsToOpen != 0) OpenWrappers(next);
    }

    // What makes "**" before or after it count as emphasis where the text alone would not; nothing of it is shown.
    private const string EmptyComment = "<!---->";

    /// <summary>A punctuation char for the rules of emphasis: Unicode punctuation and symbols (an emoji is one).</summary>
    private static bool IsPunctuation(char c) => char.IsPunctuation(c) || char.IsSymbol(c) || char.IsSurrogate(c);

    /// <summary>The same for what is not inline: a rule, a code block, a table row.</summary>
    private void StartLine()
    {
        if ((_break | _markerPending | _headingPending) != 0) FlushBlockStart();
        _pendingSpace = false;
    }

    private void FlushBlockStart()
    {
        int level = _break;
        _break = None;
        _pendingSpace = false;

        if (_cellDepth > 0)
        {
            // A table cell is one line: a break in it is <br>, and of a list only the markers are left.
            if (level != None && !_lineEmpty) Write("<br>");
            WriteMarkers(_cellPrefixBase);
            _lineEmpty = true;
            _breakDepth = int.MaxValue;
            return;
        }

        if (_len > 0)
        {
            switch (level)
            {
                case Hard:
                    Write("  \n");
                    break;

                case Line:
                    Write('\n');
                    break;

                case Blank:
                    Write('\n');
                    WriteBlankLine();
                    Write('\n');
                    break;
            }
        }

        if (_len == 0 || level != None)
        {
            WritePrefix();
            _lineEmpty = true;
            _blockStart = true;
        }
        else
        {
            WriteMarkers(0); // no new line (a list in a heading)
        }

        if (_headingPending != 0)
        {
            WriteRepeat('#', _headingPending);
            Write(' ');
            _headingPending = 0;
            _headingStart = _len;
            _blockStart = false;
        }

        _breakDepth = int.MaxValue;
    }

    /// <summary>A line break inside a block that is written as a whole (a code block, a table).</summary>
    private void NewLine(bool blank)
    {
        Write('\n');
        int start = _len;
        WritePrefix();
        if (blank)
        {
            while (_len > start && _buf[_len - 1] == ' ') _len--;
        }
    }

    /// <summary>"## C #" would lose its "#": a run of '#' at the end, after a space, closes the heading.</summary>
    private void EscapeHeadingEnd()
    {
        int last = _len - 1;
        if (last < _headingStart || _buf[last] != '#') return;

        int run = last;
        while (run > _headingStart && _buf[run - 1] == '#') run--;
        if (run > _headingStart && _buf[run - 1] != ' ') return;

        Ensure(1);
        _buf.AsSpan(run, _len - run).CopyTo(_buf.AsSpan(run + 1));
        _buf[run] = '\\';
        _len++;
    }

    // ----- prefixes: what every line of a list item or of a quote starts with -----

    // Deeper lists and quotes are not indented any more: every line of a page starts with at most this much,
    // whatever the page nests (32 lists numbered from 999,999,999 would put 352 chars before every line).
    private const int MaxPrefixDepth = 32;
    private const int MaxPrefixWidth = 64;

    private const int MarkerBullet = 1;
    private const int MarkerDefinition = 2;
    private const int MarkerNumber = 3; // + the number

    private struct Prefix
    {
        public int Width;   // of a list item: its indent; 0: a quote, "> "
        public int Marker;  // the marker that takes the place of the indent on the first line; 0 when it is written
        public bool Other;  // "*" and "1)" in place of "-" and "1.": the list comes right after another one of its kind
    }

    [InlineArray(MaxPrefixDepth)]
    private struct PrefixList
    {
        private Prefix _first;
    }

    /// <summary>Whether one more prefix of that width (2 for a quote) may be put before the lines.</summary>
    private bool CanIndent(int width) => _prefixCount < MaxPrefixDepth && _prefixWidth + width <= MaxPrefixWidth;

    private void PushPrefix(int width, int marker, bool other)
    {
        _prefixes[_prefixCount++] = new Prefix { Width = width, Marker = marker, Other = other };
        _prefixWidth += width == 0 ? 2 : width;
        if (marker != 0) _markerPending++;
    }

    /// <summary>Returns false when the marker was never written: the item had no content.</summary>
    private bool PopPrefix()
    {
        ref var prefix = ref _prefixes[--_prefixCount];
        _prefixWidth -= prefix.Width == 0 ? 2 : prefix.Width;
        if (prefix.Marker == 0) return true;

        _markerPending--;
        return false;
    }

    private void WritePrefix()
    {
        for (int p = 0; p < _prefixCount; p++)
        {
            ref var prefix = ref _prefixes[p];
            if (prefix.Marker != 0) WriteMarker(ref prefix);
            else if (prefix.Width == 0) Write("> ");
            else WriteRepeat(' ', prefix.Width);
        }
    }

    private void WriteMarkers(int from)
    {
        for (int p = from; p < _prefixCount; p++)
        {
            ref var prefix = ref _prefixes[p];
            if (prefix.Marker != 0) WriteMarker(ref prefix);
        }
    }

    private void WriteMarker(ref Prefix prefix)
    {
        switch (prefix.Marker)
        {
            case MarkerBullet:
                Write(prefix.Other ? "* " : "- ");
                break;

            case MarkerDefinition:
                Write(": ");
                break;

            default:
                Ensure(12);
                (prefix.Marker - MarkerNumber).TryFormat(_buf.AsSpan(_len), out int digits);
                _len += digits;
                Write(prefix.Other ? ") " : ". ");
                break;
        }

        prefix.Marker = 0;
        _markerPending--;
    }

    /// <summary>The blank line between two blocks: a quote goes on over it (">"), an indent is not needed.</summary>
    private void WriteBlankLine()
    {
        int depth = Math.Min(_breakDepth, _prefixCount);
        int start = _len;
        for (int p = 0; p < depth; p++)
        {
            if (_prefixes[p].Width == 0) Write("> ");
            else WriteRepeat(' ', _prefixes[p].Width);
        }
        while (_len > start && _buf[_len - 1] == ' ') _len--;
    }

    // ----- inline pairs: ** * ~~ <sub> <sup> " and links -----

    private struct Wrapper
    {
        public MdKind Kind;
        public bool Opened; // its start is written
    }

    [InlineArray(8)]
    private struct WrapperList
    {
        private Wrapper _first;
    }

    private static int Bit(MdKind kind) => 1 << (kind - MdKind.Strong);

    private bool Activate(MdKind kind)
    {
        int bit = Bit(kind);
        if ((_active & bit) != 0) return false;

        _active |= bit;
        _wraps[_wrapCount++] = new Wrapper { Kind = kind };
        _wrapsToOpen++;
        return true;
    }

    private void Deactivate(MdKind kind)
    {
        bool opened = _wraps[--_wrapCount].Opened;
        _active &= ~Bit(kind);

        if (opened)
        {
            _wrapsOpened--;
            WriteClose(kind); // right after the last text: a space seen since then goes after it
        }
        else
        {
            _wrapsToOpen--;
        }
    }

    /// <summary>Starts the pairs that are not written yet; <paramref name="next"/> is the first char of the content after them.</summary>
    private void OpenWrappers(char next)
    {
        bool inRun = false; // the last thing written is "*" or "**" of this call: the next one goes on the same run

        for (int w = 0; w < _wrapCount; w++)
        {
            ref var wrapper = ref _wraps[w];
            if (wrapper.Opened) continue;

            wrapper.Opened = true;
            _wrapsOpened++;

            var kind = wrapper.Kind;
            if (kind is not (MdKind.Strong or MdKind.Em or MdKind.Del))
            {
                WriteOpen(kind);
                inRun = false;
                continue;
            }

            string marker = kind switch { MdKind.Strong => "**", MdKind.Em => "*", _ => "~~" };
            bool star = kind != MdKind.Del;
            if (star && inRun)
            {
                Write(marker);
                continue;
            }

            // the char after the run of markers that starts here: "**" and "*" in a row are one run
            int after = w + 1;
            if (star)
            {
                while (after < _wrapCount && _wraps[after].Kind is MdKind.Strong or MdKind.Em) after++;
            }

            inRun = OpenPair(kind, marker, after < _wrapCount ? FirstChar(_wraps[after].Kind) : next) && star;
        }

        _wrapsToOpen = 0;
    }

    /// <summary>The first char of what starts a pair.</summary>
    private static char FirstChar(MdKind kind) => kind switch
    {
        MdKind.Strong or MdKind.Em => '*',
        MdKind.Del => '~',
        MdKind.Quote => '"',
        MdKind.Link => '[',
        _ => '<',
    };

    private void CloseWrappers()
    {
        for (int w = _wrapCount - 1; w >= 0; w--)
        {
            ref var wrapper = ref _wraps[w];
            if (!wrapper.Opened) continue;

            wrapper.Opened = false;
            _wrapsToOpen++;
            WriteClose(wrapper.Kind);
        }
        _wrapsOpened = 0;
    }

    private void WriteOpen(MdKind kind)
    {
        switch (kind)
        {
            case MdKind.Sub:
                Write("<sub>");
                break;

            case MdKind.Sup:
                Write("<sup>");
                break;

            case MdKind.Quote:
                Write('"');
                break;

            default: // a link
                // "Look!" right before a link would make an image of it
                if (_len > 0 && _buf[_len - 1] == '!')
                {
                    _buf[_len - 1] = '\\';
                    Write('!');
                }
                Write('[');
                _linkDepth++;
                break;
        }
    }

    private void WriteClose(MdKind kind)
    {
        switch (kind)
        {
            case MdKind.Strong:
                ClosePair(kind, "**");
                break;

            case MdKind.Em:
                ClosePair(kind, "*");
                break;

            case MdKind.Del:
                ClosePair(kind, "~~");
                break;

            case MdKind.Sub:
                Write("</sub>");
                break;

            case MdKind.Sup:
                Write("</sup>");
                break;

            case MdKind.Quote:
                Write('"');
                break;

            default: // a link
                _linkDepth--;
                Write("](");
                WriteUrl(_linkPrefix, _linkUrl);
                if (_titleLength > 0) WriteTitle(_source.Slice(_titleStart, _titleLength));
                Write(')');
                break;
        }
    }

    /// <summary>
    /// Starts "**", "*" or "~~"; <paramref name="after"/> is the char that follows the run of markers it starts.
    /// Returns false when nothing is written: the pair goes on from the same one that has just ended.
    /// </summary>
    private bool OpenPair(MdKind kind, string marker, char after)
    {
        // <b>a</b><b>b</b>: "**a****b**" is not read as two bold words; the two are written as one, "**ab**"
        if (_lastClose == kind && _lastClosePosition == _len - marker.Length)
        {
            _len -= marker.Length;
            _lastClose = MdKind.Inline;
            return false;
        }

        // "**" before a punctuation char starts emphasis only after whitespace or punctuation ("left-flanking"):
        // in "text**(x)**" it would stay two asterisks. An empty comment before it is such punctuation.
        if (!_lineEmpty && _len > 0 && IsPunctuation(after))
        {
            char before = _buf[_len - 1];
            if (!IsSpace(before) && !char.IsPunctuation(before) && !char.IsSymbol(before)) Write(EmptyComment);
        }

        Write(marker);
        return true;
    }

    private void ClosePair(MdKind kind, string marker)
    {
        // What decides whether the run of markers this one is part of ends the emphasis is the char before the run.
        bool sameRun = _closePosition == _len && _closeMarker == marker[0];
        bool risk = sameRun ? _closeRisk != 0 : _len > 0 && IsPunctuation(_buf[_len - 1]);

        _lastClose = kind;
        _lastClosePosition = _len;
        Write(marker);

        _closePosition = _len;
        _closeMarker = marker[0];
        _closeRisk = risk ? 1 : 0;
    }

    /// <summary>
    /// The address of a link, <paramref name="prefix"/> and <paramref name="url"/> in a row:
    /// without spaces, and with parentheses only when they pair up.
    /// </summary>
    private void WriteUrl(string? prefix, scoped ReadOnlySpan<char> url)
    {
        // the parentheses of the whole address
        int open = 0;
        if (prefix != null && prefix.AsSpan().ContainsAny('(', ')')) open = CountParentheses(prefix, open);
        if (open >= 0) open = CountParentheses(url, open);
        bool paired = open == 0;

        if (prefix != null) WriteUrlPart(prefix, paired);
        WriteUrlPart(url, paired);
    }

    /// <summary>How many "(" are open after <paramref name="url"/>; -1 when a ")" has none.</summary>
    private static int CountParentheses(ReadOnlySpan<char> url, int open)
    {
        foreach (char c in url)
        {
            if (c == '(') open++;
            else if (c == ')' && --open < 0) return -1;
        }
        return open;
    }

    private void WriteUrlPart(scoped ReadOnlySpan<char> url, bool paired)
    {
        Ensure(url.Length * 3);
        var buffer = _buf;
        int length = _len;

        foreach (char c in url)
        {
            switch (c)
            {
                case ' ':
                    buffer[length++] = '%'; buffer[length++] = '2'; buffer[length++] = '0';
                    break;

                case '<':
                    buffer[length++] = '%'; buffer[length++] = '3'; buffer[length++] = 'C';
                    break;

                case '>':
                    buffer[length++] = '%'; buffer[length++] = '3'; buffer[length++] = 'E';
                    break;

                case '\\':
                    buffer[length++] = '%'; buffer[length++] = '5'; buffer[length++] = 'C';
                    break;

                case '(' when !paired:
                    buffer[length++] = '%'; buffer[length++] = '2'; buffer[length++] = '8';
                    break;

                case ')' when !paired:
                    buffer[length++] = '%'; buffer[length++] = '2'; buffer[length++] = '9';
                    break;

                default:
                    if (c > ' ') buffer[length++] = c; // tabs and line breaks are not part of an address
                    break;
            }
        }

        _len = length;
    }

    /// <summary>The title of a link or of an image: <c> "title"</c> after its address.</summary>
    private void WriteTitle(scoped ReadOnlySpan<char> raw)
    {
        var title = Decoded(raw).Trim();
        if (title.IsEmpty) return;

        Ensure(title.Length * 2 + 3);
        var buffer = _buf;
        int length = _len;

        buffer[length++] = ' ';
        buffer[length++] = '"';
        foreach (char c in title)
        {
            if (c is '"' or '\\') buffer[length++] = '\\';
            buffer[length++] = c < ' ' ? ' ' : c; // one line
        }
        buffer[length++] = '"';

        _len = length;
    }

    // ----- text -----

    // The chars that may need a backslash wherever they stand. '#', '-', '+', '>', '=' and digits only start
    // something at the start of a block: see BlockStart.
    private static readonly bool[] Special = BuildSpecial();

    private static readonly SearchValues<char> Indent = SearchValues.Create(" \n\t\r");

    private static bool[] BuildSpecial()
    {
        var special = new bool[128];
        foreach (char c in "\\`*_[]<&|~") special[c] = true;
        return special;
    }

    /// <summary>
    /// Writes text (no markup, references decoded): whitespace collapsed to single spaces, and a backslash before
    /// every char that Markdown would read as its own where it stands. Chars that are safe there are left alone:
    /// "snake_case", "a * b", "2 &lt; 3", "AT&amp;T", "[1]".
    /// </summary>
    private void WriteText(scoped ReadOnlySpan<char> text)
    {
        Ensure(text.Length * 2 + 16); // at most one backslash per char

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c <= ' ' || (c >= '\u0080' && char.IsWhiteSpace(c)))
            {
                _pendingSpace = true;

                // The indent of the source (a line break and a run of spaces between two tags) is passed over at once;
                // a single space between two words is not worth the call.
                if (i + 1 < text.Length && text[i + 1] <= ' ')
                {
                    int run = text.Slice(i + 1).IndexOfAnyExcept(Indent);
                    if (run < 0) return;
                    i += run;
                }
                continue;
            }

            if ((_break | _markerPending | _headingPending | _wrapsToOpen | _closeRisk) != 0)
            {
                BeginContentSlow(c);
                Ensure((text.Length - i) * 2 + 16);
            }
            else if (_pendingSpace)
            {
                if (!_lineEmpty) _buf[_len++] = ' ';
                _pendingSpace = false;
            }

            if (c < 128)
            {
                if (_blockStart)
                {
                    int last = BlockStart(text, i);
                    if (last >= 0)
                    {
                        i = last;
                        _lineEmpty = false;
                        _blockStart = false;
                        continue;
                    }
                }

                // "[text]" is plain text until "(", "[" or ":" comes right after it
                if (_bracket == _len - 1 && (c is '(' or '[' or ':'))
                {
                    _buf[_len - 1] = '\\';
                    _buf[_len++] = ']';
                }

                if (Special[c])
                {
                    Escape(text, i, c);
                    _lineEmpty = false;
                    _blockStart = false;
                    continue;
                }
            }

            _buf[_len++] = c;
            _lineEmpty = false;
            _blockStart = false;
        }
    }

    /// <summary>Writes one of the <see cref="Special"/> chars, with a backslash where it would not be plain text.</summary>
    private void Escape(scoped ReadOnlySpan<char> text, int i, char c)
    {
        char next = i + 1 < text.Length ? text[i + 1] : '\0'; // '\0': not known, the text goes on in another tag
        char previous = _len > 0 ? _buf[_len - 1] : '\n';
        bool escape;

        switch (c)
        {
            case '\\':
                // a backslash is plain text unless it stands before something it could escape
                escape = next == '\0' || (next < 128 && (char.IsPunctuation(next) || char.IsSymbol(next)));
                break;

            case '*':
                // emphasis needs a "*" that touches a word: "2 * 3" is plain text
                escape = _lineEmpty || previous != ' ' || next == '\0' || !IsSpace(next);
                break;

            case '_':
                // inside a word "_" is plain text: snake_case
                escape = !char.IsLetterOrDigit(previous) || !char.IsLetterOrDigit(next);
                break;

            case '[':
                // in a list item "[ ]" and "[x]" would be read as the checkbox of a task
                escape = _linkDepth > 0 || (_prefixCount > 0 && IsTaskBox(text, i));
                break;

            case ']':
                escape = _linkDepth > 0;
                if (!escape) _bracket = _len;
                break;

            case '<':
                // a tag or an autolink: "2 < 3" is plain text
                escape = next == '\0' || char.IsAsciiLetter(next) || next is '/' or '!' or '?';
                break;

            case '&':
                escape = IsReference(text, i);
                break;

            case '|':
                escape = _cellDepth > 0;
                break;

            case '~':
                // strikethrough is "~~": "~5 minutes" is plain text
                escape = next == '~' || previous == '~';
                break;

            default: // '`'
                escape = true;
                break;
        }

        if (escape) _buf[_len++] = '\\';
        _buf[_len++] = c;
    }

    /// <summary>
    /// The first char of a block: what would start a heading, a list, a quote, a rule or a table there gets a backslash.
    /// Returns the index of the last char written when more than the backslash was written, otherwise -1.
    /// </summary>
    private int BlockStart(scoped ReadOnlySpan<char> text, int i)
    {
        char c = text[i];
        char next = i + 1 < text.Length ? text[i + 1] : '\0';

        switch (c)
        {
            case '#':
            {
                // "# heading": up to six '#' and a space
                int end = i + 1;
                while (end < text.Length && text[end] == '#') end++;
                if (end - i <= 6 && (end == text.Length || IsSpace(text[end]))) _buf[_len++] = '\\';
                break;
            }

            case '>':
                _buf[_len++] = '\\';
                break;

            case '+':
                if (next == '\0' || IsSpace(next)) _buf[_len++] = '\\';
                break;

            case '-':
                if (next == '\0' || IsSpace(next) || IsRuleLine(text, i)) _buf[_len++] = '\\';
                break;

            case '=':
            case '|':
            case ':':
                if (IsRuleLine(text, i)) _buf[_len++] = '\\';
                break;

            case >= '0' and <= '9':
            {
                // "1. item", "1) item": up to nine digits
                int end = i + 1;
                while (end < text.Length && end - i < 9 && char.IsAsciiDigit(text[end])) end++;
                if (end < text.Length && (text[end] is '.' or ')') && (end + 1 == text.Length || IsSpace(text[end + 1])))
                {
                    text.Slice(i, end - i).CopyTo(_buf.AsSpan(_len));
                    _len += end - i;
                    _buf[_len++] = '\\';
                    _buf[_len++] = text[end];
                    return end;
                }
                break;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether the text from <paramref name="i"/> on has only what a rule ("---"), a heading underline ("===")
    /// or the second line of a table ("--- | ---") is made of.
    /// </summary>
    private static bool IsRuleLine(ReadOnlySpan<char> text, int i)
    {
        for (; i < text.Length; i++)
        {
            if (text[i] is not ('-' or '=' or '_' or '*' or '|' or ':' or ' ' or '\t')) return false;
        }
        return true;
    }

    /// <summary>Whether the '[' at <paramref name="i"/> starts "[ ]" or "[x]".</summary>
    private static bool IsTaskBox(ReadOnlySpan<char> text, int i)
    {
        int end = i + 1;
        if (end < text.Length && text[end] is 'x' or 'X')
        {
            end++;
        }
        else
        {
            while (end < text.Length && IsSpace(text[end])) end++; // written as one space
            if (end == i + 1) return false;
        }
        return end < text.Length && text[end] == ']';
    }

    /// <summary>Whether the '&amp;' at <paramref name="i"/> starts what looks like a character reference: "&amp;copy;", "&amp;#169;".</summary>
    private static bool IsReference(ReadOnlySpan<char> text, int i)
    {
        int start = i + 1;
        if (start < text.Length && text[start] == '#') start++;

        int end = start;
        while (end < text.Length && end - start < 32 && char.IsAsciiLetterOrDigit(text[end])) end++;
        return end > start && end < text.Length && text[end] == ';';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSpace(char c) => c <= ' ' || char.IsWhiteSpace(c);
}
