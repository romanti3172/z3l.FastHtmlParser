using System.Buffers;
using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser.Core.Sanitizer;

/// <summary>
/// Writes the allowed part of a document anew, in one pass over the tag list.
/// </summary>
/// <remarks>
/// Nothing of the input is copied as it is: a kept tag is written by its lower-case name with its kept attributes
/// in double quotes (their values decoded, checked and escaped), every text is decoded and escaped, comments and
/// what is left of markup in the text (end tags, a tag cut off) are dropped, and every element that is opened is
/// closed where the parser ended it. A browser therefore reads the result as it was written, whatever the parser
/// made of the input: the result has only allowed tags, and no text in it can start markup.
/// </remarks>
internal ref struct SanitizerWriter
{
    private readonly HtmlParser _document;
    private readonly TagList _data;
    private readonly ReadOnlySpan<char> _source;
    private readonly SanitizerPolicy _policy;

    private char[] _buf;
    private int _len;

    private Frame[] _frames;
    private int _frameCount;
    private int _openTables;    // written <table> elements that are open: the parts of a table are kept only in one
    private int[] _openByKey;   // per HtmlTag, the written elements that are open and not ended early

    // How many open elements EndImplied looks through for the one to end. The parser nests at most 512 deep;
    // the bound keeps markup nested deeper than that, or open elements that are not nested (thousands of
    // unclosed <font>), from making the pass quadratic.
    private const int MaxImpliedScan = 1024;

    private struct Frame
    {
        public int End, InnerEnd;
        public int Key;
        public string Name;
        public bool Closed;  // its end tag is already written: a tag the browser ends there was written inside it
    }

    public SanitizerWriter(HtmlParser document, SanitizerPolicy policy, int capacity)
    {
        _document = document;
        _data = document.Data;
        _source = document.Source;
        _policy = policy;
        _buf = ArrayPool<char>.Shared.Rent(Math.Clamp(capacity, 256, 1 << 20));
        _frames = ArrayPool<Frame>.Shared.Rent(32);
        _openByKey = ArrayPool<int>.Shared.Rent(ParsingCore.TagNames.Length);
        Array.Clear(_openByKey);
    }

    public readonly int Length => _len;

    public override readonly string ToString() => _len == 0 ? string.Empty : new string(_buf, 0, _len);

    public void Dispose()
    {
        ArrayPool<char>.Shared.Return(_buf);
        ArrayPool<Frame>.Shared.Return(_frames);
        ArrayPool<int>.Shared.Return(_openByKey);
        _buf = [];
        _frames = [];
        _openByKey = [];
    }

    /// <summary>The whole document.</summary>
    public void WriteDocument() => Write(0, 0, int.MaxValue, null, null);

    /// <summary>One tag with its content.</summary>
    public void WriteTag(int index) => Write(index, _data.Node(index).Start, _data.Node(index).End, null, null);

    /// <summary>
    /// <c>Source[start..end]</c>: <paramref name="first"/> is the first tag that starts in it. Tags marked in
    /// <paramref name="excluded"/> are left out with their content; a kept tag with a name in
    /// <paramref name="renames"/> is written by that name (it must be an allowed one).
    /// </summary>
    public void Write(int first, int start, int end, bool[]? excluded, string?[]? renames)
    {
        var data = _data;
        int count = data.Count;
        if (end >= _source.Length) end = _source.Length - 1;
        if (start > end) return;

        int baseFrames = _frameCount;
        int pos = start;

        for (int i = first; i < count; i++)
        {
            ref readonly var node = ref data.Node(i);
            int tagStart = node.Start;
            if (tagStart > end) break;

            while (_frameCount > baseFrames && _frames[_frameCount - 1].End < tagStart) pos = Close(pos);
            Text(pos, tagStart);

            bool container = node.InnerStart > 0 && node.InnerEnd > 0;
            pos = container ? node.InnerStart : node.End + 1;

            int key = node.Key;
            bool skip;
            if (excluded != null && excluded[i] || !node.IsElement)
            {
                skip = true;
            }
            else if ((key >= 0 ? _policy.Allowed[key] : _policy.AllowsCustom(node.CustomName!)) && (_openTables > 0 || !IsTablePart(key)))
            {
                string name = renames?[i] ?? (key >= 0 ? ParsingCore.TagNames[key] : node.CustomName!);
                int nameKey = key >= 0 && renames?[i] is { } renamed && ParsingCore.TryGetTag(renamed, out var renamedTag) ? (int)renamedTag : key;
                EndImplied(nameKey, baseFrames);
                OpenTag(name, key, in node);

                bool isVoid = key >= 0 && ParsingCore.VoidTags[key];
                if (isVoid)
                {
                    skip = true; // nothing is written inside a void element
                }
                else if (container)
                {
                    Push(node.End, node.InnerEnd, name, nameKey);
                    if (nameKey == (int)HtmlTag.Table) _openTables++;
                    skip = false;
                }
                else
                {
                    // not closed and with no content (<p> at the end of the page, <div/>): closed right away
                    Append("</");
                    Append(name);
                    Append('>');
                    skip = true;
                }
            }
            else
            {
                // left out; its content stays unless it is one of the tags removed with it
                skip = key >= 0 ? _policy.Removed[key] : _policy.RemovedTags.Contains(node.CustomName!);
            }

            if (skip)
            {
                int nodeEnd = node.End;
                pos = Math.Max(pos, nodeEnd + 1);
                while (i + 1 < count && data.Node(i + 1).Start <= nodeEnd) i++;
            }
        }

        while (_frameCount > baseFrames) pos = Close(pos);
        Text(pos, end + 1);
    }

    /// <summary>
    /// A part of a table: outside a table a browser drops these tags (and keeps their text), so they are written only
    /// inside a table that is written.
    /// </summary>
    private static bool IsTablePart(int key) => key is (int)HtmlTag.Tr or (int)HtmlTag.Td or (int)HtmlTag.Th or (int)HtmlTag.Tbody
        or (int)HtmlTag.Thead or (int)HtmlTag.Tfoot or (int)HtmlTag.Caption or (int)HtmlTag.Col or (int)HtmlTag.Colgroup;

    /// <summary>
    /// Writes the end tags that a browser puts before a tag of <paramref name="key"/>: a block ends an open &lt;p&gt;,
    /// an &lt;li&gt; the open &lt;li&gt;, with the elements inside them. The parser ended them already, unless a tag
    /// between them that stops the rule (a &lt;td&gt; out of a table, a &lt;button&gt;) is left out of the result: then
    /// they are ended here, as a browser that reads the result will end them.
    /// </summary>
    private void EndImplied(int key, int baseFrames)
    {
        ParsingCore.ImpliedEnds(key, out var pTargets, out var pStops, out var targets, out var stops, out var pKeys, out var keys);
        if (pTargets != null && AnyOpen(pKeys!)) EndImplied(pTargets, pStops!, baseFrames);
        if (targets != null && AnyOpen(keys!)) EndImplied(targets, stops!, baseFrames);
    }

    /// <summary>Whether an element of one of these tags is open: mostly none is, and the stack is not looked through.</summary>
    private readonly bool AnyOpen(int[] keys)
    {
        foreach (int k in keys)
        {
            if (_openByKey[k] > 0) return true;
        }
        return false;
    }

    private void EndImplied(bool[] targets, bool[] stops, int baseFrames)
    {
        int last = Math.Max(baseFrames, _frameCount - MaxImpliedScan);
        for (int j = _frameCount - 1; j >= last; j--)
        {
            ref var frame = ref _frames[j];
            if (frame.Closed || frame.Key < 0) continue;
            if (targets[frame.Key])
            {
                for (int k = _frameCount - 1; k >= j; k--)
                {
                    ref var inner = ref _frames[k];
                    if (inner.Closed) continue;
                    inner.Closed = true;
                    if (inner.Key >= 0) _openByKey[inner.Key]--;
                    if (inner.Key == (int)HtmlTag.Table) _openTables--;
                    Append("</");
                    Append(inner.Name);
                    Append('>');
                }
                return;
            }
            if (stops[frame.Key]) return;
        }
    }

    private void Push(int end, int innerEnd, string name, int key)
    {
        if (_frameCount == _frames.Length)
        {
            var bigger = ArrayPool<Frame>.Shared.Rent(_frameCount * 2);
            _frames.AsSpan(0, _frameCount).CopyTo(bigger);
            ArrayPool<Frame>.Shared.Return(_frames);
            _frames = bigger;
        }
        _frames[_frameCount++] = new Frame { End = end, InnerEnd = innerEnd, Name = name, Key = key };
        if (key >= 0) _openByKey[key]++;
    }

    private int Close(int pos)
    {
        ref var frame = ref _frames[--_frameCount];
        Text(pos, frame.InnerEnd + 1);
        if (!frame.Closed)
        {
            if (frame.Key >= 0) _openByKey[frame.Key]--;
            if (frame.Key == (int)HtmlTag.Table) _openTables--;
            Append("</");
            Append(frame.Name);
            Append('>');
        }
        return Math.Max(pos, frame.End + 1);
    }

    // ----- text -----

    /// <summary>
    /// <c>Source[from..to)</c> as text: what is left of markup in it (end tags, a tag the parser did not record,
    /// a tag cut off at the end) is dropped, character references are decoded, and '&amp;', '&lt;', '&gt;' and
    /// U+00A0 are written as references.
    /// </summary>
    private void Text(int from, int to)
    {
        if (to > _source.Length) to = _source.Length;
        if (to <= from) return;

        var text = _source.Slice(from, to - from);
        EnsureCapacity(text.Length + 16);
        Span<char> decoded = stackalloc char[2];

        int i = 0;
        while (i < text.Length)
        {
            int special = text.Slice(i).IndexOfAny(TextSpecial);
            if (special < 0)
            {
                Append(text.Slice(i));
                return;
            }
            Append(text.Slice(i, special));
            i += special;

            char c = text[i];
            if (c == '<')
            {
                if (HtmlToTextCore.IsMarkupStart(text, i))
                {
                    int close = text.Slice(i + 1).IndexOf('>');
                    if (close < 0) return; // an unfinished tag: the rest is markup
                    i += close + 2;
                    continue;
                }
                Append("&lt;");
            }
            else if (c == '&')
            {
                int consumed = HtmlDecodeCore.DecodeReference(text.Slice(i + 1), decoded, out int written);
                if (consumed > 0)
                {
                    for (int k = 0; k < written; k++) AppendEscaped(decoded[k]);
                    i += consumed + 1;
                    continue;
                }
                Append("&amp;");
            }
            else if (c == '>')
            {
                Append("&gt;");
            }
            else if (c == '\r')
            {
                // a browser reads CR LF and a lone CR as LF
                if (i + 1 >= text.Length || text[i + 1] != '\n') Append('\n');
            }
            else
            {
                Append("&nbsp;");
            }
            i++;
        }
    }

    private static readonly SearchValues<char> TextSpecial = SearchValues.Create("<&>\r ");

    private void AppendEscaped(char c)
    {
        switch (c)
        {
            case '&': Append("&amp;"); break;
            case '<': Append("&lt;"); break;
            case '>': Append("&gt;"); break;
            case '"': Append("&quot;"); break;
            case ' ': Append("&nbsp;"); break;
            case '\0': Append('�'); break;
            default: Append(c); break;
        }
    }

    // ----- tags and attributes -----

    private void OpenTag(string name, int key, in ParsingCore.TagNode node)
    {
        Append('<');
        Append(name);

        int openEnd = node.InnerStart > 0 && node.InnerEnd > 0 ? node.InnerStart - 1 : node.End;
        var scanner = new AttributeScanner(_source, node.Start, openEnd);

        bool isLink = key == (int)HtmlTag.A;
        bool isImage = key == (int)HtmlTag.Img;
        bool hasHref = false;

        // The names written, to write each one once: a browser keeps the first of the same name.
        Span<int> written = stackalloc int[32];
        int writtenCount = 0;

        string? lazySource = isImage && _policy.LazyImages ? LazySource(node.Start, openEnd) : null;

        while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
        {
            var rawName = _source.Slice(nameStart, nameLength);
            if (!IsAttributeName(rawName)) continue;
            if (rawName.Length >= 2 && (rawName[0] | 0x20) == 'o' && (rawName[1] | 0x20) == 'n') continue; // event handlers
            if (rawName.Equals("srcdoc", StringComparison.OrdinalIgnoreCase)) continue;
            if (isLink && _policy.LinkRel != null && rawName.Equals("rel", StringComparison.OrdinalIgnoreCase)) continue;

            bool data = rawName.Length > 5 && rawName.StartsWith("data-", StringComparison.OrdinalIgnoreCase);
            if (!(data && _policy.DataAttributes) && !_policy.Attributes.Contains(rawName)) continue;

            if (Seen(rawName, written, writtenCount)) continue;

            var rawValue = valueStart >= 0 ? _source.Slice(valueStart, valueLength) : default;
            string? value;
            if (isImage && lazySource != null && rawName.Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                value = CheckUrl(lazySource, imageSource: true);
                if (value == null) continue;
            }
            else if (rawName.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                value = valueStart < 0 ? null : SanitizeStyle(Decode(rawValue));
                if (string.IsNullOrEmpty(value)) continue;
            }
            else if (_policy.UrlAttributes.Contains(rawName))
            {
                if (valueStart < 0) continue;
                value = rawName.Equals("srcset", StringComparison.OrdinalIgnoreCase)
                    ? CheckSrcset(Decode(rawValue))
                    : CheckUrl(Decode(rawValue), imageSource: isImage && rawName.Equals("src", StringComparison.OrdinalIgnoreCase));
                if (value == null) continue;
                if (isLink && rawName.Equals("href", StringComparison.OrdinalIgnoreCase)) hasHref = true;
            }
            else
            {
                value = valueStart < 0 ? null : Decode(rawValue);
            }

            if (writtenCount < written.Length) written[writtenCount++] = _len + 1;
            WriteAttribute(rawName, value);
        }

        if (lazySource != null && !Seen("src", written, writtenCount) && CheckUrl(lazySource, imageSource: true) is { } lazy)
        {
            WriteAttribute("src", lazy);
        }

        if (isLink && hasHref && _policy.LinkRel != null) WriteAttribute("rel", _policy.LinkRel);

        Append('>');
    }

    /// <summary>
    /// Whether an attribute of this name is already written in this tag: <paramref name="written"/> holds where
    /// the name of each one starts in the result (names are written in lower case).
    /// </summary>
    private readonly bool Seen(ReadOnlySpan<char> name, Span<int> written, int count)
    {
        for (int k = 0; k < count; k++)
        {
            int p = written[k];
            if (p + name.Length > _len) continue;
            if (MemoryExtensions.Equals(_buf.AsSpan(p, name.Length), name, StringComparison.OrdinalIgnoreCase)
                && (p + name.Length == _len || _buf[p + name.Length] is '=' or ' '))
            {
                return true;
            }
        }
        return false;
    }

    private void WriteAttribute(ReadOnlySpan<char> name, string? value)
    {
        Append(' ');
        EnsureCapacity(name.Length);
        foreach (char c in name) _buf[_len++] = char.ToLowerInvariant(c);

        if (value == null) return;

        Append("=\"");
        EnsureCapacity(value.Length + 8);
        int i = 0;
        while (i < value.Length)
        {
            int special = value.AsSpan(i).IndexOfAny(AttributeSpecial);
            if (special < 0)
            {
                Append(value.AsSpan(i));
                break;
            }
            Append(value.AsSpan(i, special));
            AppendEscaped(value[i + special]);
            i += special + 1;
        }
        Append('"');
    }

    private static readonly SearchValues<char> AttributeSpecial = SearchValues.Create("&<>\" \0");

    private static bool IsAttributeName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || !char.IsAsciiLetter(name[0])) return false;
        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.')) return false;
        }
        return true;
    }

    private static string Decode(ReadOnlySpan<char> raw)
    {
        return HtmlDecodeCore.NeedsDecoding(raw) ? HtmlDecodeCore.HtmlDecode(raw, inAttribute: true) : raw.ToString();
    }

    /// <summary>
    /// The address of a lazy image (data-src, data-original, ...) when its src is missing or a placeholder
    /// (a data: URL, a "blank" or "loading" picture). Null when the image has its own.
    /// </summary>
    private readonly string? LazySource(int tagStart, int openEnd)
    {
        var scanner = new AttributeScanner(_source, tagStart, openEnd);
        string? src = null, lazy = null;
        while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
        {
            if (valueStart < 0 || valueLength == 0) continue;
            var name = _source.Slice(nameStart, nameLength);
            var value = _source.Slice(valueStart, valueLength);
            if (name.Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                src ??= Decode(value);
            }
            else if (lazy == null && (name.Equals("data-src", StringComparison.OrdinalIgnoreCase)
                || name.Equals("data-original", StringComparison.OrdinalIgnoreCase)
                || name.Equals("data-lazy-src", StringComparison.OrdinalIgnoreCase)
                || name.Equals("data-lazy", StringComparison.OrdinalIgnoreCase)
                || name.Equals("data-url", StringComparison.OrdinalIgnoreCase)
                || name.Equals("data-hi-res-src", StringComparison.OrdinalIgnoreCase)))
            {
                var decoded = Decode(value).Trim();
                if (decoded.Length > 0 && !decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) lazy = decoded;
            }
        }

        if (lazy == null) return null;
        if (string.IsNullOrWhiteSpace(src)) return lazy;

        var s = src.AsSpan().Trim();
        bool placeholder = s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || s.Contains("blank", StringComparison.OrdinalIgnoreCase)
            || s.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
            || s.Contains("loading", StringComparison.OrdinalIgnoreCase)
            || s.Contains("lazy", StringComparison.OrdinalIgnoreCase)
            || s.Contains("spacer", StringComparison.OrdinalIgnoreCase)
            || s.Contains("pixel", StringComparison.OrdinalIgnoreCase);
        return placeholder ? lazy : null;
    }

    // ----- URLs -----

    /// <summary>
    /// The URL to write, or null to leave the attribute out: a URL with a scheme must have an allowed one;
    /// a relative URL is kept, made absolute when the options ask for it and the page has an address.
    /// </summary>
    private string? CheckUrl(string decoded, bool imageSource)
    {
        // As a browser reads a URL: no leading and trailing C0 controls and spaces, no tabs and line breaks anywhere.
        var url = TrimControls(decoded.AsSpan());
        string value;
        if (url.IndexOfAny('\t', '\n', '\r') >= 0)
        {
            Span<char> clean = url.Length <= 512 ? stackalloc char[url.Length] : new char[url.Length];
            int n = 0;
            foreach (char c in url)
            {
                if (c is not ('\t' or '\n' or '\r')) clean[n++] = c;
            }
            value = new string(clean.Slice(0, n));
        }
        else
        {
            value = url.Length == decoded.Length ? decoded : url.ToString();
        }

        if (value.Length == 0) return value;

        switch (Scheme(value, out int colon))
        {
            case SchemeKind.Invalid:
                return null;

            case SchemeKind.Present:
                var scheme = value.AsSpan(0, colon);
                if (_policy.Schemes.Contains(scheme)) return value;
                if (imageSource && _policy.DataImages && scheme.Equals("data", StringComparison.OrdinalIgnoreCase) && IsDataImage(value)) return value;
                return null;

            default:
                return _policy.AbsoluteUrls ? Absolute(value) : value;
        }
    }

    private static ReadOnlySpan<char> TrimControls(ReadOnlySpan<char> s)
    {
        int start = 0, end = s.Length;
        while (start < end && s[start] <= ' ') start++;
        while (end > start && s[end - 1] <= ' ') end--;
        return s.Slice(start, end - start);
    }

    private enum SchemeKind { Relative, Present, Invalid }

    /// <summary>
    /// A scheme is letters, digits, '+', '-' and '.' after a letter, up to a ':' that comes before any '/', '?' or '#'.
    /// A ':' there after anything else makes the URL invalid here (left out), so no browser can read a scheme in it.
    /// </summary>
    private static SchemeKind Scheme(string url, out int colon)
    {
        colon = -1;
        bool plain = char.IsAsciiLetter(url[0]);
        for (int i = 0; i < url.Length; i++)
        {
            char c = url[i];
            if (c == ':')
            {
                colon = i;
                return plain && i > 0 ? SchemeKind.Present : SchemeKind.Invalid;
            }
            if (c is '/' or '?' or '#') return SchemeKind.Relative;
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')) plain = false;
        }
        return SchemeKind.Relative;
    }

    private static bool IsDataImage(string url)
    {
        var rest = url.AsSpan(5);
        foreach (var type in DataImageTypes)
        {
            if (rest.StartsWith(type, StringComparison.OrdinalIgnoreCase) && rest.Length > type.Length && rest[type.Length] is ';' or ',')
            {
                return true;
            }
        }
        return false;
    }

    private static readonly string[] DataImageTypes =
        ["image/png", "image/gif", "image/jpeg", "image/jpg", "image/webp", "image/avif", "image/bmp", "image/x-icon", "image/vnd.microsoft.icon"];

    private readonly string Absolute(string relative)
    {
        if (_document.PageUri is not { } baseUri) return relative;

        if ((Uri.TryCreate(relative, UriKind.Relative, out var relativeUri) && Uri.TryCreate(baseUri, relativeUri, out var combined))
            || Uri.TryCreate(baseUri, relative, out combined))
        {
            var absolute = combined.ToString();
            // made from the page's own address, so its scheme is the page's: still checked, as every other one is
            return Scheme(absolute, out int colon) == SchemeKind.Present && _policy.Schemes.Contains(absolute.AsSpan(0, colon))
                ? absolute
                : relative;
        }
        return relative;
    }

    /// <summary>The candidates of a srcset with an allowed URL, joined with ", "; null when none is left.</summary>
    private string? CheckSrcset(string srcset)
    {
        var parts = new List<string>();
        var s = srcset.AsSpan();
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
            if (i >= s.Length) break;

            int urlStart = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            var url = s.Slice(urlStart, i - urlStart);
            var descriptor = ReadOnlySpan<char>.Empty;
            if (url.EndsWith(','))
            {
                url = url.TrimEnd(',');
            }
            else
            {
                int descriptorStart = i;
                while (i < s.Length && s[i] != ',') i++;
                descriptor = s.Slice(descriptorStart, i - descriptorStart).Trim();
            }

            if (url.IsEmpty) continue;
            // a data: URL holds commas: its candidate cannot be split from the next one safely
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return parts.Count > 0 ? string.Join(", ", parts) : null;

            if (CheckUrl(url.ToString(), imageSource: false) is { } checkedUrl && checkedUrl.Length > 0)
            {
                parts.Add(descriptor.IsEmpty ? checkedUrl : string.Concat(checkedUrl, " ", descriptor));
            }
        }
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    // ----- CSS -----

    /// <summary>
    /// The declarations of a style attribute with an allowed property and a plain value, as "name: value; ...".
    /// A value is plain when it has no backslash (no CSS escapes), no comment, no '&lt;', no ';' in quotes, its
    /// quotes are paired, and it calls no function that loads or runs something (url, image, expression, ...).
    /// </summary>
    private string? SanitizeStyle(string css)
    {
        if (css.Length == 0) return null;

        string? result = null;
        foreach (var range in css.AsSpan().Split(';'))
        {
            var declaration = css.AsSpan(range);
            int colon = declaration.IndexOf(':');
            if (colon <= 0) continue;

            var name = declaration.Slice(0, colon).Trim();
            var value = declaration.Slice(colon + 1).Trim();
            if (name.IsEmpty || value.IsEmpty || !IsCssName(name) || !_policy.CssProperties.Contains(name) || !IsPlainCssValue(value)) continue;

            result = result == null
                ? string.Concat(name.ToString().ToLowerInvariant(), ": ", value)
                : string.Concat(result, "; ", name.ToString().ToLowerInvariant(), ": ", value.ToString());
        }
        return result;
    }

    private static bool IsCssName(ReadOnlySpan<char> name)
    {
        foreach (char c in name)
        {
            if (!(char.IsAsciiLetter(c) || c == '-')) return false;
        }
        return true;
    }

    private static bool IsPlainCssValue(ReadOnlySpan<char> value)
    {
        int singles = 0, doubles = 0;
        foreach (char c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is ' ' or '#' or '%' or '.' or ',' or '(' or ')' or '-' or '+' or '!' or '/' or '_')
            {
                continue;
            }
            if (c == '\'') { singles++; continue; }
            if (c == '"') { doubles++; continue; }
            if (c > 127 && char.IsLetter(c)) continue; // a font name in another script
            return false;
        }
        if ((singles & 1) != 0 || (doubles & 1) != 0) return false;
        if (value.Contains("/*", StringComparison.Ordinal)) return false;

        foreach (var function in ForbiddenCss)
        {
            if (value.Contains(function, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static readonly string[] ForbiddenCss =
        ["url", "image", "expression", "element(", "paint(", "binding", "behavior", "javascript", "vbscript", "src("];

    // ----- the buffer -----

    private void EnsureCapacity(int more)
    {
        if (_len + more <= _buf.Length) return;

        var bigger = ArrayPool<char>.Shared.Rent(Math.Max(_buf.Length * 2, _len + more));
        _buf.AsSpan(0, _len).CopyTo(bigger);
        ArrayPool<char>.Shared.Return(_buf);
        _buf = bigger;
    }

    private void Append(char c)
    {
        if (_len == _buf.Length) EnsureCapacity(1);
        _buf[_len++] = c;
    }

    private void Append(ReadOnlySpan<char> s)
    {
        EnsureCapacity(s.Length);
        s.CopyTo(_buf.AsSpan(_len));
        _len += s.Length;
    }

    /// <summary>Writes raw markup the caller made (used by the article extractor for its wrapper).</summary>
    public void AppendRaw(string markup) => Append(markup);
}
