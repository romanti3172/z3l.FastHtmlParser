using System.Buffers;
using System.Runtime.InteropServices;

namespace z3l.FastHtmlParser.Core;

internal static class ParsingCore
{
    // Names indexed by the enum value: static strings, no per-call ToString()/ToLower().
    // NB: declared before SpanLookup, static fields are initialized in textual order.
    internal static readonly string[] TagNames = HtmlNames.BuildTagNames();
    internal static readonly string[] AttributeNames = HtmlNames.BuildAttributeNames();

    // Tag name → HtmlTag, case-insensitive, queried with spans (no string per lookup).
    // A plain Dictionary with IgnoreCaseNameComparer: ~2 ms to build on first use
    // (a FrozenDictionary took ~16 ms to JIT) and ~30% faster to query.
    private static readonly Dictionary<string, HtmlTag>.AlternateLookup<ReadOnlySpan<char>> SpanLookup = BuildTagLookup();

    /// <summary>The <see cref="HtmlTag"/> for a tag name (case-insensitive), if it is one of the known names.</summary>
    internal static bool TryGetTag(ReadOnlySpan<char> name, out HtmlTag tag) => SpanLookup.TryGetValue(name, out tag);

    private static Dictionary<string, HtmlTag>.AlternateLookup<ReadOnlySpan<char>> BuildTagLookup()
    {
        var map = new Dictionary<string, HtmlTag>(TagNames.Length, IgnoreCaseNameComparer.Instance);
        for (int i = 0; i < TagNames.Length; i++)
        {
            map.Add(TagNames[i], (HtmlTag)i);
        }
        return map.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    // Per-tag data needed while parsing, kept in a pooled struct array:
    // matching a closing tag scans it without touching ParserTag objects.
    private struct TagState
    {
        public int Key;     // (int)HtmlTag, or -1 for a custom tag name
        public int Depth;
        public bool Open;   // InnerEnd == 0

        // Doubly linked list of open tags (in document order): a closing tag
        // scans only open candidates instead of every tag before it.
        public int PrevOpen;
        public int NextOpen;
    }

    // parsing

    internal static List<ParserTag> ParsePagesource(HtmlParser parent, ReadOnlySpan<char> pagesource)
    {
        var tagStartPosition = 0;
        var isTagStarted = false;
        var pgl = pagesource.Length;

        // Every tag starts with its own '<', so this is an exact upper bound:
        // tags are collected in pooled arrays and the result list is allocated once with the exact size.
        int maxTags = pagesource.Count('<');
        if (maxTags == 0) return [];

        ParserTag[] tags = ArrayPool<ParserTag>.Shared.Rent(maxTags);
        TagState[] states = ArrayPool<TagState>.Shared.Rent(maxTags);
        int[] openTagsStack = ArrayPool<int>.Shared.Rent(64);
        int count = 0;
        int stackTop = 0;
        int lastOpen = -1;
        Dictionary<string, string>? customNames = null;

        // An XML document (RSS, Atom, sitemap, XHTML) is parsed by XML rules: elements are closed only by an end tag or "/>"
        // (an RSS <link> has content), and no element has raw-text content.
        bool isXml = pagesource.TrimStart().TrimStart('﻿').StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);

        try
        {
            int i = 0;
            while (i < pgl)
            {
                if (!isTagStarted)
                {
                    // Outside a tag only '<' matters: jump to it (vectorized).
                    int lt = pagesource.Slice(i).IndexOf('<');
                    if (lt < 0) break;

                    i += lt;

                    if (pagesource.Slice(i + 1).StartsWith("!--"))
                    {
                        // <!-- ... -->: one "!--" tag; its content is not markup, it does not nest anything.
                        int commentEnd = FindCommentEnd(pagesource, i + 4);
                        var depth = stackTop > 0 ? states[openTagsStack[stackTop - 1]].Depth + 1 : 0;

                        tags[count] = new ParserTag(parent, count, HtmlTag.Comment, null, i, commentEnd, commentEnd + 1, depth);
                        states[count] = new TagState { Key = (int)HtmlTag.Comment, Depth = depth, Open = false, PrevOpen = -1, NextOpen = -1 };
                        count++;

                        i = commentEnd + 1;
                        continue;
                    }

                    isTagStarted = true;
                    tagStartPosition = i + 1;
                    i++;
                    continue;
                }

                // Inside a tag only '<', '=' and '>' matter.
                int next = pagesource.Slice(i).IndexOfAny('<', '=', '>');
                if (next < 0) break;

                i += next;
                char currentChar = pagesource[i];

                if (currentChar == '<')
                {
                    // An unfinished tag: start over at this '<' (it may also start a comment).
                    isTagStarted = false;
                    continue;
                }
                else if (currentChar == '=')
                {
                    // значение атрибута в кавычках может содержать '<' и '>' (например data-x="<p>...</p>"),
                    // поэтому перепрыгиваем его целиком до закрывающей кавычки
                    var q = i + 1;
                    while (q < pgl && char.IsWhiteSpace(pagesource[q])) q++;
                    if (q < pgl && (pagesource[q] == '"' || pagesource[q] == '\''))
                    {
                        var closeQuote = pagesource[(q + 1)..].IndexOf(pagesource[q]);
                        if (closeQuote >= 0) i = q + 1 + closeQuote;
                    }
                }
                else // '>'
                {
                    // Обработка закрывающегося тега
                    if (pagesource[tagStartPosition] == '/' && tagStartPosition + 1 != i - tagStartPosition - 1)
                    {
                        var closingTagSpan = pagesource.Slice(tagStartPosition + 1, i - tagStartPosition - 1).Trim();
                        if (!closingTagSpan.IsEmpty)
                        {
                            // Resolve the closing name once, then compare ints instead of a dictionary lookup per candidate.
                            int closingKey = SpanLookup.TryGetValue(closingTagSpan, out HtmlTag closingTag) ? (int)closingTag : -1;

                            for (int j = lastOpen; j >= 0; j = states[j].PrevOpen)
                            {
                                ref TagState state = ref states[j];

                                bool isMatch = state.Key >= 0
                                    ? state.Key == closingKey
                                    : MemoryExtensions.Equals(closingTagSpan, tags[j].CustomName, StringComparison.OrdinalIgnoreCase);

                                if (isMatch)
                                {
                                    var innerEnd = tagStartPosition - 2;
                                    tags[j].InnerEnd = innerEnd;
                                    tags[j].End = i;
                                    state.Open = innerEnd == 0;

                                    if (!state.Open)
                                    {
                                        // unlink from the open list
                                        int prev = state.PrevOpen, nextOpen = state.NextOpen;
                                        if (prev >= 0) states[prev].NextOpen = nextOpen;
                                        if (nextOpen >= 0) states[nextOpen].PrevOpen = prev;
                                        else lastOpen = prev;
                                    }

                                    // Синхронизация кастомного стека
                                    while (stackTop > 0 && openTagsStack[stackTop - 1] >= j)
                                    {
                                        stackTop--;
                                    }

                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        var tagsource = pagesource[tagStartPosition..i];
                        var tagNameSpan = ExtractTagName(tagsource);

                        HtmlTag? htmlTagEnum;
                        string? htmlTagString;
                        int key;
                        if (SpanLookup.TryGetValue(tagNameSpan, out HtmlTag knownTag))
                        {
                            htmlTagEnum = knownTag;
                            htmlTagString = null;
                            key = (int)knownTag;
                        }
                        else
                        {
                            // Custom names (comments "!--", "!doctype", svg elements, ...): one string per distinct name per document.
                            htmlTagEnum = null;
                            htmlTagString = InternLowerName(tagNameSpan, ref customNames);
                            key = -1;
                        }

                        // Расчет уровней на лету с кастомным стеком
                        var level = stackTop > 0 ? states[openTagsStack[stackTop - 1]].Depth + 1 : 0;

                        tags[count] = new ParserTag(
                            parent,
                            count,                          // index
                            htmlTagEnum,                    // HtmlTag name
                            htmlTagString,                  // name
                            tagStartPosition - 1,           // PositionStart
                            i,                              // PositionEnd
                            i + 1,                          // PositionInnerStart
                            level
                           );
                        // Void elements (<br>, <img>, <meta>, ...) and declarations (<!doctype>) never contain anything:
                        // they do not nest the following tags and no end tag is matched to them.
                        bool isVoid = !isXml && key >= 0 && VoidTags[key];

                        if (isVoid)
                        {
                            states[count] = new TagState { Key = key, Depth = level, Open = false, PrevOpen = -1, NextOpen = -1 };
                        }
                        else
                        {
                            states[count] = new TagState { Key = key, Depth = level, Open = true, PrevOpen = lastOpen, NextOpen = -1 };
                            if (lastOpen >= 0) states[lastOpen].NextOpen = count;
                            lastOpen = count;
                        }
                        count++;

                        // Проверка на самозакрытие без аллокаций
                        var trimmedSource = tagsource.TrimEnd();
                        bool isSelfClosing = isVoid || (trimmedSource.Length > 0 && trimmedSource[^1] == '/');

                        if (!isSelfClosing)
                        {
                            if (stackTop == openTagsStack.Length)
                            {
                                var bigger = ArrayPool<int>.Shared.Rent(stackTop * 2);
                                openTagsStack.AsSpan().CopyTo(bigger);
                                ArrayPool<int>.Shared.Return(openTagsStack);
                                openTagsStack = bigger;
                            }
                            openTagsStack[stackTop++] = count - 1;

                            // <script>, <style>, <textarea>, <title>, ...: the content is text, not markup;
                            // continue right before the matching end tag.
                            if (!isXml && key >= 0 && RawTextEnds[key] is { } rawTextEnd)
                            {
                                i = FindRawTextEnd(pagesource, i + 1, rawTextEnd) - 1;
                            }
                        }
                    }

                    isTagStarted = false;
                }

                i++;
            }

            var result = new List<ParserTag>(count);
            CollectionsMarshal.SetCount(result, count);
            tags.AsSpan(0, count).CopyTo(CollectionsMarshal.AsSpan(result));

            // Финализация незакрытых/одиночных тегов
            for (int k = 0; k < count; k++)
            {
                if (tags[k].InnerEnd == 0)
                {
                    tags[k].InnerStart = 0;
                }
            }

            return result;
        }
        finally
        {
            Array.Clear(tags, 0, count); // do not keep the tags alive through the pool
            ArrayPool<ParserTag>.Shared.Return(tags);
            ArrayPool<TagState>.Shared.Return(states);
            ArrayPool<int>.Shared.Return(openTagsStack);
        }
    }

    // HTML5 void elements (incl. obsolete ones the parsing rules also treat as void) and declarations.
    private static readonly bool[] VoidTags = BuildVoidTags();

    private static bool[] BuildVoidTags()
    {
        var isVoid = new bool[TagNames.Length];
        foreach (var tag in new[]
        {
            HtmlTag.Area, HtmlTag.Base, HtmlTag.Br, HtmlTag.Col, HtmlTag.Embed, HtmlTag.Hr, HtmlTag.Img, HtmlTag.Input,
            HtmlTag.Link, HtmlTag.Meta, HtmlTag.Source, HtmlTag.Track, HtmlTag.Wbr,
            HtmlTag.Basefont, HtmlTag.Bgsound, HtmlTag.Frame, HtmlTag.Keygen, HtmlTag.Param, HtmlTag.Isindex,
            HtmlTag.Doctype, HtmlTag.XmlDeclaration,
        })
        {
            isVoid[(int)tag] = true;
        }
        return isVoid;
    }

    // "</script", "</style", ... for the elements whose content is raw text (HTML5 RAWTEXT / RCDATA).
    private static readonly string?[] RawTextEnds = BuildRawTextEnds();

    private static string?[] BuildRawTextEnds()
    {
        var ends = new string?[TagNames.Length];
        foreach (var tag in new[] { HtmlTag.Script, HtmlTag.Style, HtmlTag.Textarea, HtmlTag.Title, HtmlTag.Xmp, HtmlTag.Iframe, HtmlTag.Noembed, HtmlTag.Noframes })
        {
            ends[(int)tag] = "</" + TagNames[(int)tag];
        }
        return ends;
    }

    /// <summary>Position of the '&lt;' of the end tag (e.g. "&lt;/script"), or the end of the source if there is none.</summary>
    internal static int FindRawTextEnd(ReadOnlySpan<char> source, int from, string endTag)
    {
        while (from < source.Length)
        {
            int found = source.Slice(from).IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;

            int position = from + found;
            int after = position + endTag.Length;

            // "</script>", "</script >", "</script/>" end it; "</scripts" does not.
            if (after >= source.Length || source[after] is '>' or '/' || char.IsWhiteSpace(source[after]))
            {
                return position;
            }

            from = after;
        }

        return source.Length;
    }

    /// <summary>
    /// Position of the '&gt;' that ends a comment whose content starts at <paramref name="contentStart"/> (right after "&lt;!--").
    /// As in HTML5: "&lt;!--&gt;" and "&lt;!---&gt;" are empty comments, "--&gt;" or "--!&gt;" end a comment, otherwise it runs to the end.
    /// </summary>
    internal static int FindCommentEnd(ReadOnlySpan<char> source, int contentStart)
    {
        var content = source.Slice(Math.Min(contentStart, source.Length));

        if (content.StartsWith('>')) return contentStart;
        if (content.StartsWith("->")) return contentStart + 1;

        for (int from = 0; from < content.Length;)
        {
            int dashes = content.Slice(from).IndexOf("--");
            if (dashes < 0) break;

            int position = from + dashes + 2;
            if (position < content.Length && content[position] == '>') return contentStart + position;
            if (position + 1 < content.Length && content[position] == '!' && content[position + 1] == '>') return contentStart + position + 1;

            from = from + dashes + 1;
        }

        return source.Length - 1;
    }

    private static string InternLowerName(ReadOnlySpan<char> name, ref Dictionary<string, string>? cache)
    {
        if (name.IsEmpty) return string.Empty;

        Span<char> lower = name.Length <= 128 ? stackalloc char[128] : new char[name.Length];
        lower = lower.Slice(0, name.Length);
        name.ToLowerInvariant(lower);

        cache ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var lookup = cache.GetAlternateLookup<ReadOnlySpan<char>>();
        if (!lookup.TryGetValue(lower, out var interned))
        {
            interned = lower.ToString();
            cache.Add(interned, interned);
        }

        return interned;
    }

    private static ReadOnlySpan<char> ExtractTagName(ReadOnlySpan<char> tagsource)
    {
        tagsource = tagsource.Trim();

        if (tagsource.StartsWith("!--"))
        {
            return tagsource[..3];
        }

        var index = 0;
        var sourcelen = tagsource.Length;
        while (index < sourcelen && !char.IsWhiteSpace(tagsource[index]))
        {
            index++;
        }

        return tagsource[..index];
    }

    // ----- HTML -> text with line breaks -----

    private enum LineBreakKind : byte
    {
        None,           // inline tag (b, span, a, ...): removed, words are not split
        Space,          // table cell: word separator
        Line,           // <br> and block elements (div, li, tr, ...): new line
        Paragraph,      // p, h1-h6: new paragraph (blank line)
        SkipContent,    // script, style, noscript: contents are not text
    }

    private static readonly LineBreakKind[] LineBreakKinds = BuildLineBreakKinds();

    private static LineBreakKind[] BuildLineBreakKinds()
    {
        var kinds = new LineBreakKind[TagNames.Length];

        foreach (var tag in new[] { HtmlTag.P, HtmlTag.H1, HtmlTag.H2, HtmlTag.H3, HtmlTag.H4, HtmlTag.H5, HtmlTag.H6 })
            kinds[(int)tag] = LineBreakKind.Paragraph;

        foreach (var tag in new[]
        {
            HtmlTag.Br, HtmlTag.Div, HtmlTag.Li, HtmlTag.Ul, HtmlTag.Ol, HtmlTag.Dl, HtmlTag.Dt, HtmlTag.Dd,
            HtmlTag.Table, HtmlTag.Caption, HtmlTag.Tr, HtmlTag.Thead, HtmlTag.Tbody, HtmlTag.Tfoot,
            HtmlTag.Section, HtmlTag.Article, HtmlTag.Aside, HtmlTag.Nav, HtmlTag.Header, HtmlTag.Footer, HtmlTag.Main,
            HtmlTag.Address, HtmlTag.Blockquote, HtmlTag.Pre, HtmlTag.Hr, HtmlTag.Figure, HtmlTag.Figcaption,
            HtmlTag.Form, HtmlTag.Fieldset, HtmlTag.Legend, HtmlTag.Details, HtmlTag.Summary, HtmlTag.Dialog,
            HtmlTag.Option, HtmlTag.Html, HtmlTag.Head, HtmlTag.Body, HtmlTag.Title,
            HtmlTag.Hgroup, HtmlTag.Search, HtmlTag.Menu, HtmlTag.Center, HtmlTag.Dir, HtmlTag.Listing,
            HtmlTag.Xmp, HtmlTag.Plaintext, HtmlTag.Multicol, HtmlTag.Frameset, HtmlTag.Marquee,
        })
            kinds[(int)tag] = LineBreakKind.Line;

        kinds[(int)HtmlTag.Td] = LineBreakKind.Space;
        kinds[(int)HtmlTag.Th] = LineBreakKind.Space;

        foreach (var tag in new[] { HtmlTag.Script, HtmlTag.Style, HtmlTag.Noscript, HtmlTag.Template, HtmlTag.Noframes, HtmlTag.Noembed })
            kinds[(int)tag] = LineBreakKind.SkipContent;

        return kinds;
    }

    // "</script", "</style", ... built once for the SkipContent tags.
    private static readonly string?[] SkipContentEnds = BuildSkipContentEnds();

    /// <summary>
    /// For the inside of an opening tag (<c>script type="x"</c>, without '&lt;' and '&gt;'): the end tag ("&lt;/script")
    /// when the element's content is never visible text (script, style, noscript, template, noframes, noembed), otherwise null.
    /// </summary>
    internal static string? InvisibleContentEnd(ReadOnlySpan<char> tag)
    {
        // Called for every tag while extracting text, so no dictionary: the six names are compared directly.
        // All of them start with 's', 't' or 'n'.
        if (tag.IsEmpty || (tag[0] | 0x20) is not ('s' or 't' or 'n')) return null;

        int nameLength = 1;
        while (nameLength < tag.Length && nameLength <= 8 && char.IsAsciiLetter(tag[nameLength])) nameLength++;
        if (nameLength < tag.Length && !(tag[nameLength] is '/' || char.IsWhiteSpace(tag[nameLength]))) return null;

        var name = tag.Slice(0, nameLength);
        string? end = nameLength switch
        {
            5 when name.Equals("style", StringComparison.OrdinalIgnoreCase) => "</style",
            6 when name.Equals("script", StringComparison.OrdinalIgnoreCase) => "</script",
            7 when name.Equals("noembed", StringComparison.OrdinalIgnoreCase) => "</noembed",
            8 when name.Equals("noscript", StringComparison.OrdinalIgnoreCase) => "</noscript",
            8 when name.Equals("template", StringComparison.OrdinalIgnoreCase) => "</template",
            8 when name.Equals("noframes", StringComparison.OrdinalIgnoreCase) => "</noframes",
            _ => null,
        };

        if (end != null && tag.TrimEnd().EndsWith('/')) return null; // <script src="x" />: no content
        return end;
    }

    private static string?[] BuildSkipContentEnds()
    {
        var ends = new string?[TagNames.Length];
        for (int i = 0; i < ends.Length; i++)
        {
            if (LineBreakKinds[i] == LineBreakKind.SkipContent) ends[i] = "</" + TagNames[i];
        }
        return ends;
    }

    /// <summary>
    /// HTML → plain text that keeps the visible line structure, in a single pass:
    /// <list type="bullet">
    /// <item>&lt;br&gt; → new line (each one counts: &lt;br&gt;&lt;br&gt; gives a blank line);</item>
    /// <item>p, h1–h6 → new paragraph (blank line);</item>
    /// <item>other block elements (div, li, tr, dt/dd, blockquote, ...) → new line; td/th → space;</item>
    /// <item>other tags are removed without splitting words; script/style/noscript contents and comments are dropped;</item>
    /// <item>source whitespace, including newlines, is collapsed to one space (as a browser does), lines are trimmed,
    /// at most one blank line in a row; entities are decoded (&amp;nbsp; counts as whitespace).</item>
    /// </list>
    /// Tag names are case-insensitive and may have attributes or a trailing '/'. Lines are joined with <see cref="Environment.NewLine"/>.
    /// </summary>
    internal static string? ClearTagsLineBreaks(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : ClearTagsLineBreaks(value.AsSpan());
    }

    /// <inheritdoc cref="ClearTagsLineBreaks(string?)"/>
    internal static string? ClearTagsLineBreaks(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return null;

        // Output never exceeds 2x the input: a line break is written only before a text char
        // and needs a tag (>= 3 chars) in the input.
        char[] buffer = ArrayPool<char>.Shared.Rent(value.Length * 2);
        try
        {
            var writer = new LineBreakWriter(buffer, Environment.NewLine);
            Span<char> decoded = stackalloc char[2];
            int i = 0;

            while (i < value.Length)
            {
                char c = value[i];

                if (c == '&')
                {
                    int consumed = HtmlDecodeCore.DecodeReference(value.Slice(i + 1), decoded, out int written);
                    if (consumed > 0)
                    {
                        // decoded chars go through the same whitespace/text logic (&nbsp; collapses, &lt; is plain text)
                        for (int k = 0; k < written; k++) writer.Char(decoded[k]);
                        i += 1 + consumed;
                        continue;
                    }

                    writer.Char(c);
                    i++;
                    continue;
                }

                if (c != '<')
                {
                    writer.Char(c);
                    i++;
                    continue;
                }

                ReadOnlySpan<char> rest = value.Slice(i);

                if (rest.StartsWith("<!--"))
                {
                    int commentEnd = rest.Slice(4).IndexOf("-->");
                    if (commentEnd < 0) break;
                    i += 4 + commentEnd + 3;
                    continue;
                }

                int close = rest.IndexOf('>');
                if (close < 0) break; // unterminated tag: the rest is markup

                ReadOnlySpan<char> tag = rest.Slice(1, close - 1);
                i += close + 1;

                bool isClosing = !tag.IsEmpty && tag[0] == '/';
                ReadOnlySpan<char> name = isClosing ? tag.Slice(1) : tag;
                int nameLength = name.IndexOfAny(TagNameEnd);
                if (nameLength >= 0) name = name.Slice(0, nameLength);

                if (!SpanLookup.TryGetValue(name, out HtmlTag knownTag)) continue; // unknown tag: just removed

                switch (LineBreakKinds[(int)knownTag])
                {
                    case LineBreakKind.Line:
                        if (knownTag == HtmlTag.Br) writer.LineBreak(); // every <br> counts
                        else writer.Break(1);
                        break;

                    case LineBreakKind.Paragraph:
                        writer.Break(2);
                        break;

                    case LineBreakKind.Space:
                        writer.Char(' ');
                        break;

                    case LineBreakKind.SkipContent:
                        if (!isClosing && !tag.TrimEnd().EndsWith('/'))
                        {
                            // jump to the closing tag; it is removed on the next iteration
                            int end = value.Slice(i).IndexOf(SkipContentEnds[(int)knownTag], StringComparison.OrdinalIgnoreCase);
                            i = end < 0 ? value.Length : i + end;
                        }
                        break;
                }
            }

            return writer.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static readonly SearchValues<char> TagNameEnd = SearchValues.Create(" \t\r\n\f/");

    /// <summary>Writes text, collapsing whitespace and turning requested breaks into trimmed lines.</summary>
    private ref struct LineBreakWriter(Span<char> buffer, string newLine)
    {
        private readonly Span<char> _buffer = buffer;
        private int _length;
        private int _pendingBreaks;   // new lines requested since the last text char
        private bool _pendingSpace;

        public void Char(char c)
        {
            if (char.IsWhiteSpace(c))
            {
                _pendingSpace = true;
                return;
            }

            if (_length > 0) // nothing before the first text char: leading breaks/spaces are trimmed
            {
                if (_pendingBreaks > 0)
                {
                    for (int n = Math.Min(_pendingBreaks, 2); n > 0; n--) // at most one blank line
                    {
                        newLine.CopyTo(_buffer.Slice(_length));
                        _length += newLine.Length;
                    }
                }
                else if (_pendingSpace)
                {
                    _buffer[_length++] = ' ';
                }
            }

            _pendingBreaks = 0;
            _pendingSpace = false;
            _buffer[_length++] = c;
        }

        /// <summary>Block boundary: at least <paramref name="count"/> new lines before the next text.</summary>
        public void Break(int count) => _pendingBreaks = Math.Max(_pendingBreaks, count);

        /// <summary>&lt;br&gt;: one more new line.</summary>
        public void LineBreak() => _pendingBreaks++;

        public override readonly string ToString() => _length == 0 ? string.Empty : new string(_buffer.Slice(0, _length));
    }
}
