using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

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

    // Attribute name → its static lower-case string, case-insensitive, queried with spans.
    private static readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> AttributeNameLookup = BuildAttributeNameLookup();

    /// <summary>
    /// The lower-case name of an attribute: the static string for a known name (no allocation), a new one otherwise.
    /// </summary>
    internal static string LowerAttributeName(ReadOnlySpan<char> name)
    {
        if (AttributeNameLookup.TryGetValue(name, out var known)) return known;

        return string.Create(name.Length, name, static (chars, n) => n.ToLowerInvariant(chars));
    }

    private static Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> BuildAttributeNameLookup()
    {
        var map = new Dictionary<string, string>(AttributeNames.Length, IgnoreCaseNameComparer.Instance);
        foreach (var name in AttributeNames)
        {
            map.TryAdd(name, name);
        }
        return map.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    // Per-tag data needed while parsing, kept in a pooled struct array:
    // matching a closing tag scans it without touching ParserTag objects.
    private struct TagState
    {
        public int Key;     // (int)HtmlTag, or -1 for a custom tag name
        public int Depth;
        public bool SelfClosed; // <x/>: stays in the open list (a later </x> ends it), but does not nest anything

        // The open tags as a linked list in document order: an end tag scans only open candidates
        // instead of every tag before it. Tags leave the list only as its tail (TruncateOpenList).
        public int PrevOpen;
    }

    // Nesting deeper than this is flattened, as browsers do (Blink: 512): a deeper tag does not nest the
    // following ones. It bounds every scan of the stack of open tags, whatever the markup.
    private const int MaxDepth = 512;

    // The parts scanned again with the optional-end-tag rules (Repair) may add up to this many times
    // the length of the source; past that the tags are just ended by their enclosing end tags.
    private const int RepairBudgetFactor = 8;

    // parsing
    //
    // A page is scanned once in FastMode: tags are matched to their end tags and nothing else.
    // Well-formed markup needs nothing more. Where the HTML5 optional-end-tag rules matter (<li>one<li>two</ul>),
    // tags are left open inside an element: when its end tag (or the end of the document) finds them on the stack
    // and one of them is a rule target (li, p, td, tr, option, ...), only that part is scanned again in RulesMode,
    // from the outermost such tag up to that end tag. Everything before it was parsed right and is kept.

    private interface IScanMode { static abstract bool Rules { get; } }
    private struct FastMode : IScanMode { public static bool Rules => false; }
    private struct RulesMode : IScanMode { public static bool Rules => true; }

    /// <summary>A tag as the scan records it; the ParserTag objects are made from these once the scan is done.</summary>
    internal struct TagNode(int parentIndex, int key, string? customName, int start, int end, int innerStart, int depth)
    {
        public int Start = start, End = end, InnerStart = innerStart, InnerEnd, ParentIndex = parentIndex;
        private readonly int _keyDepth = (key & 0xFFFF) | (depth << 16); // 32 bytes a tag: the depth is at most MaxDepth + 1
        public string? CustomName = customName;

        /// <summary>(int)HtmlTag, or -1 for a custom name.</summary>
        public readonly int Key => (short)_keyDepth;
        public readonly int Depth => _keyDepth >>> 16;

        /// <summary>An element: not a comment, doctype, XML declaration or other "!..." / "?..." markup.</summary>
        public readonly bool IsElement => Key >= 0
            ? Key != (int)HtmlTag.Comment && Key != (int)HtmlTag.Doctype && Key != (int)HtmlTag.XmlDeclaration
            : !string.IsNullOrEmpty(CustomName) && CustomName[0] != '!' && CustomName[0] != '?';
    }

    // Everything a scan reads and updates; a scan copies the hot fields into locals and writes them back.
    private ref struct ParseState
    {
        public HtmlParser Parent;
        public ReadOnlySpan<char> Source;
        public bool IsXml;
        public TagNode[] Tags;
        public TagState[] States;
        public int[] Stack;                 // indexes of the open tags that contain the following ones
        public int[]? OpenByKey;            // per HtmlTag, how many are on the stack: kept only in RulesMode
        public int[] OpenInList;            // per HtmlTag, how many are in the open list: an end tag with none is skipped
        public Dictionary<string, int>? CustomNames; // lower-case custom name (one string per name per document) → how many are in the open list
        public int Count, StackTop, LastOpen;
        public int OpenP;                   // <p> tags on the stack, kept in both modes
        public long RepairBudget;           // chars that may still be scanned again by Repair
    }

    internal static TagList ParsePagesource(HtmlParser parent, ReadOnlySpan<char> pagesource)
    {
        // Every tag starts with its own '<', so this is an exact upper bound:
        // tags are collected in pooled arrays and the result list is allocated once with the exact size.
        int maxTags = pagesource.Count('<');
        if (maxTags == 0) return TagList.Empty(parent);

        var st = new ParseState
        {
            Parent = parent,
            Source = pagesource,
            // An XML document (RSS, Atom, sitemap, XHTML) is parsed by XML rules: elements are closed only by an end tag or "/>"
            // (an RSS <link> has content), and no element has raw-text content.
            IsXml = pagesource.TrimStart().TrimStart('﻿').StartsWith("<?xml", StringComparison.OrdinalIgnoreCase),
            Tags = ArrayPool<TagNode>.Shared.Rent(maxTags),
            States = ArrayPool<TagState>.Shared.Rent(maxTags),
            Stack = ArrayPool<int>.Shared.Rent(64),
            OpenInList = ArrayPool<int>.Shared.Rent(TagNames.Length),
            LastOpen = -1,
            RepairBudget = RepairBudgetFactor * (long)pagesource.Length,
        };
        Array.Clear(st.OpenInList, 0, TagNames.Length);

        try
        {
            Scan<FastMode>(ref st, 0, pagesource.Length, out bool isTagStarted, out int tagStartPosition);

            // The end of the document ends every tag still open (a truncated page keeps its text),
            // right before an unfinished tag if the source stops inside one.
            int regionEnd = isTagStarted ? tagStartPosition - 1 : pagesource.Length;
            if (!st.IsXml && FirstMisnested(st.States, st.Stack, 0, st.StackTop) is var s && s >= 0)
            {
                Repair(ref st, s, regionEnd);
            }
            TruncateOpenList(st.Tags, st.States, 0, regionEnd - 1, st.OpenInList, st.CustomNames, ref st.LastOpen);
            st.StackTop = 0;

            int count = st.Count;
            var tags = st.Tags;
            for (int k = 0; k < count; k++)
            {
                // Финализация незакрытых/одиночных тегов
                if (tags[k].InnerEnd == 0) tags[k].InnerStart = 0;
            }

            // the document keeps the tags in arrays of the exact size: there are fewer tags than '<' (text, scripts, comments)
            return new TagList(parent, tags.AsSpan(0, count));
        }
        finally
        {
            Array.Clear(st.Tags, 0, st.Count); // do not keep the names alive through the pool
            ArrayPool<TagNode>.Shared.Return(st.Tags);
            ArrayPool<TagState>.Shared.Return(st.States);
            ArrayPool<int>.Shared.Return(st.Stack);
            ArrayPool<int>.Shared.Return(st.OpenInList);
            if (st.OpenByKey != null) ArrayPool<int>.Shared.Return(st.OpenByKey);
        }
    }

    /// <summary>
    /// Scans <c>Source[from..to)</c> (<paramref name="from"/> is outside a tag), appending tags at <c>Count</c>.
    /// Tags still open at <paramref name="to"/> stay on the stack.
    /// </summary>
    private static void Scan<TMode>(ref ParseState st, int from, int to, out bool isTagStarted, out int tagStartPosition)
        where TMode : struct, IScanMode
    {
        var pagesource = st.Source.Slice(0, to);
        var pgl = to;
        var parent = st.Parent;
        bool isXml = st.IsXml;
        var tags = st.Tags;
        var states = st.States;
        var openTagsStack = st.Stack;
        int[]? openByKey = st.OpenByKey;
        int[] openInList = st.OpenInList;
        int count = st.Count;
        int stackTop = st.StackTop;
        int lastOpen = st.LastOpen;
        int openP = st.OpenP;

        tagStartPosition = 0;
        isTagStarted = false;

        int i = from;
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
                    int commentParent = stackTop > 0 ? openTagsStack[stackTop - 1] : -1;
                    var depth = commentParent >= 0 ? states[commentParent].Depth + 1 : 0;

                    tags[count] = new TagNode(commentParent, (int)HtmlTag.Comment, null, i, commentEnd, commentEnd + 1, depth);
                    states[count] = new TagState { Key = (int)HtmlTag.Comment, Depth = depth, PrevOpen = -1 };
                    count++;

                    i = commentEnd + 1;
                    continue;
                }

                // As in HTML5, '<' starts markup only before a letter, '/', '!' or '?': "a < b" is text.
                if (!HtmlToTextCore.IsMarkupStart(pagesource, i))
                {
                    i++;
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
                if (pagesource[tagStartPosition] == '/')
                {
                    var closingTagSpan = pagesource.Slice(tagStartPosition + 1, i - tagStartPosition - 1).Trim();
                    if (!closingTagSpan.IsEmpty)
                    {
                        // Resolve the closing name once, then compare ints (references, for a custom name) instead of
                        // a dictionary lookup per candidate.
                        int closingKey = SpanLookup.TryGetValue(closingTagSpan, out HtmlTag closingTag) ? (int)closingTag : -1;
                        string? closingName = null;
                        bool anyOpen;
                        bool repaired = false;

                    MatchEndTag:
                        // A name that no open tag has is a stray end tag: it is skipped right here, without scanning the open tags.
                        anyOpen = closingKey >= 0
                            ? openInList[closingKey] > 0
                            : TryGetOpenCustomName(st.CustomNames, closingTagSpan, out closingName);

                        for (int j = anyOpen ? lastOpen : -1; j >= 0; j = states[j].PrevOpen)
                        {
                            // a custom name is one string instance per document (InternLowerName)
                            bool isMatch = closingKey >= 0
                                ? states[j].Key == closingKey
                                : ReferenceEquals(tags[j].CustomName, closingName);

                            if (isMatch)
                            {
                                var innerEnd = tagStartPosition - 2;

                                // Tags left open inside it: if the optional-end-tag rules apply to one of them,
                                // scan its part again with the rules (FastMode only; RulesMode has applied them).
                                if (!TMode.Rules && !isXml && !repaired && stackTop > 0 && openTagsStack[stackTop - 1] > j)
                                {
                                    int above = stackTop - 1;
                                    while (above > 0 && openTagsStack[above - 1] > j) above--;

                                    if (FirstMisnested(states, openTagsStack, above, stackTop) is var target && target >= 0)
                                    {
                                        st.Count = count; st.StackTop = stackTop; st.LastOpen = lastOpen; st.OpenP = openP; st.Stack = openTagsStack;
                                        Repair(ref st, target, tagStartPosition - 1);
                                        count = st.Count; stackTop = st.StackTop; lastOpen = st.LastOpen; openP = st.OpenP; openTagsStack = st.Stack;
                                        openByKey = st.OpenByKey;

                                        // The rules may have ended the matched tag itself (<li><div><li>x</div>: the second <li>
                                        // ends the first one together with its <div>, as in a browser): the end tag is matched
                                        // again, to what is open now. Once: the part is not scanned a second time.
                                        repaired = true;
                                        goto MatchEndTag;
                                    }
                                }

                                tags[j].InnerEnd = innerEnd;
                                tags[j].End = i;

                                while (stackTop > 0 && openTagsStack[stackTop - 1] >= j)
                                {
                                    int poppedKey = states[openTagsStack[--stackTop]].Key;
                                    if (poppedKey == (int)HtmlTag.P) openP--;
                                    if (TMode.Rules && poppedKey >= 0) openByKey![poppedKey]--;
                                }

                                // It leaves the open list with everything opened after it. The tags opened inside it and
                                // never closed end right before this end tag, as in a browser; a self-closed one (<span/>)
                                // can no longer be ended by an end tag that comes after its parent's.
                                TruncateOpenList(tags, states, j, innerEnd, openInList, st.CustomNames, ref lastOpen);

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
                        htmlTagString = InternLowerName(tagNameSpan, ref st.CustomNames);
                        key = -1;
                    }

                    if (!isXml && key >= 0)
                    {
                        // Optional end tags (HTML5): <li> ends an open <li>, <td> an open <td>, ...
                        if (ImpliedEndRules[key] is { } rule)
                        {
                            if (TMode.Rules)
                            {
                                if (rule.AnyOpen(openByKey!))
                                {
                                    CloseInScope(rule, tags, states, openTagsStack, ref stackTop, openByKey, ref openP, tagStartPosition - 2,
                                        openInList, st.CustomNames, ref lastOpen);
                                }
                            }
                            // FastMode: the common case of a left-out end tag is caught right here, with no scan again:
                            // the innermost open tag is a target (<li>one<li>, </article><li>, <td>a<tr>).
                            else if (stackTop > 0 && states[openTagsStack[stackTop - 1]].Key is var topKey && topKey >= 0 && rule.Targets[topKey])
                            {
                                st.Count = count; st.StackTop = stackTop; st.LastOpen = lastOpen; st.OpenP = openP; st.Stack = openTagsStack;
                                ApplyRuleInFastMode(ref st, rule, tagStartPosition);
                                count = st.Count; stackTop = st.StackTop; lastOpen = st.LastOpen; openP = st.OpenP; openTagsStack = st.Stack;
                            }
                        }

                        // A block element ends an open <p>. In both modes: <p>a<div>b</div></p> is balanced,
                        // so the stack would not show it. Usually no <p> is open and this is one compare.
                        if (openP > 0 && ClosesP[key])
                        {
                            if (TMode.Rules)
                            {
                                CloseInScope(PRule, tags, states, openTagsStack, ref stackTop, openByKey, ref openP, tagStartPosition - 2,
                                    openInList, st.CustomNames, ref lastOpen);
                            }
                            else
                            {
                                st.Count = count; st.StackTop = stackTop; st.LastOpen = lastOpen; st.OpenP = openP; st.Stack = openTagsStack;
                                ApplyRuleInFastMode(ref st, PRule, tagStartPosition);
                                count = st.Count; stackTop = st.StackTop; lastOpen = st.LastOpen; openP = st.OpenP; openTagsStack = st.Stack;
                            }
                        }
                    }

                    // Расчет уровней на лету с кастомным стеком: the innermost open tag is the parent
                    int parentIndex = stackTop > 0 ? openTagsStack[stackTop - 1] : -1;
                    var level = parentIndex >= 0 ? states[parentIndex].Depth + 1 : 0;

                    tags[count] = new TagNode(parentIndex, key, htmlTagString, tagStartPosition - 1, i, i + 1, level);

                    // Void elements (<br>, <img>, <meta>, ...) never contain anything: they do not nest the following
                    // tags and no end tag is matched to them. Neither does any markup whose name starts with '!' or '?'
                    // (<!doctype>, <?xml ...?>, <?php ...?>, <![if IE]>, <!ELEMENT ...>), in HTML and in XML alike:
                    // a bogus comment in a browser, a declaration or processing instruction in XML.
                    // The name's first char is an ASCII letter, '!' or '?' (IsMarkupStart; '/' is an end tag),
                    // and both '!' and '?' sort before the letters: one compare.
                    bool isVoid = pagesource[tagStartPosition] <= '?' || (!isXml && key >= 0 && VoidTags[key]);

                    // Проверка на самозакрытие без аллокаций
                    var trimmedSource = tagsource.TrimEnd();
                    bool isSelfClosing = isVoid || (trimmedSource.Length > 0 && trimmedSource[^1] == '/');

                    if (isVoid)
                    {
                        states[count] = new TagState { Key = key, Depth = level, PrevOpen = -1 };
                    }
                    else
                    {
                        states[count] = new TagState { Key = key, Depth = level, SelfClosed = isSelfClosing, PrevOpen = lastOpen };
                        lastOpen = count;

                        // how many open tags have this name: an end tag with none is skipped without a scan
                        if (key >= 0) openInList[key]++;
                        else AdjustOpenCustomName(st.CustomNames, htmlTagString!, 1);
                    }

                    count++;

                    if (!isSelfClosing)
                    {
                        // Deeper than MaxDepth the tag is not nested: it stays in the open list for its end tag,
                        // like a self-closed one, but ends with its enclosing tag if it has none.
                        if (stackTop < MaxDepth)
                        {
                            if (stackTop == openTagsStack.Length)
                            {
                                var bigger = ArrayPool<int>.Shared.Rent(stackTop * 2);
                                openTagsStack.AsSpan().CopyTo(bigger);
                                ArrayPool<int>.Shared.Return(openTagsStack);
                                openTagsStack = bigger;
                            }
                            openTagsStack[stackTop++] = count - 1;
                            if (key == (int)HtmlTag.P) openP++;
                            if (TMode.Rules && key >= 0) openByKey![key]++;
                        }

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

        st.Count = count;
        st.StackTop = stackTop;
        st.LastOpen = lastOpen;
        st.OpenP = openP;
        st.Stack = openTagsStack;
    }

    // ----- implied end tags -----

    /// <summary>
    /// Among the tags left open at stack positions [<paramref name="from"/>, <paramref name="to"/>), the outermost one
    /// that a rule should have ended: a tag opened inside it has a rule that targets it (<c>&lt;li&gt;a&lt;span&gt;b&lt;li&gt;</c>,
    /// missed in FastMode because the <c>&lt;li&gt;</c> was not the innermost open tag). -1 if there is none:
    /// a tag that is just left open (the last <c>&lt;li&gt;</c> of a list) ends right where it is ended anyway.
    /// </summary>
    private static int FirstMisnested(TagState[] states, int[] stack, int from, int to)
    {
        for (int s = from; s < to - 1; s++)
        {
            int key = states[stack[s]].Key;
            if (key < 0 || !IsRuleTarget[key]) continue;

            for (int inner = s + 1; inner < to; inner++)
            {
                int innerKey = states[stack[inner]].Key;
                if (innerKey >= 0 && ImpliedEndRules[innerKey] is { } rule && rule.Targets[key]) return s;
            }
        }
        return -1;
    }

    /// <summary>
    /// Scans again with the optional-end-tag rules from the tag at stack position <paramref name="stackPosition"/>
    /// up to <paramref name="regionEnd"/>: the tags from it on are replaced, the ones before it are kept.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Repair(ref ParseState st, int stackPosition, int regionEnd)
    {
        var states = st.States;
        int first = st.Stack[stackPosition];
        int from = st.Tags[first].Start;

        // Nested repairs scan the same text again and again; past the budget the part is left as it is
        // (its tags are ended by the enclosing end tag), so that parsing stays linear on any markup.
        long cost = regionEnd - from;
        if (cost > st.RepairBudget) return;
        st.RepairBudget -= cost;

        // The tags from `first` on are replaced: they leave the open list (they are its tail).
        TruncateOpenList(st.Tags, states, first, -1, st.OpenInList, st.CustomNames, ref st.LastOpen);

        st.StackTop = stackPosition;
        st.Count = first;

        // Counters for the tags that stay on the stack.
        st.OpenByKey ??= ArrayPool<int>.Shared.Rent(TagNames.Length);
        Array.Clear(st.OpenByKey, 0, TagNames.Length);
        st.OpenP = 0;
        for (int s = 0; s < stackPosition; s++)
        {
            int key = states[st.Stack[s]].Key;
            if (key < 0) continue;
            st.OpenByKey[key]++;
            if (key == (int)HtmlTag.P) st.OpenP++;
        }

        Scan<RulesMode>(ref st, from, regionEnd, out _, out _);
    }

    /// <summary>
    /// Removes the tags from index <paramref name="first"/> on from the open list: it is in document order, so they are its tail.
    /// With <paramref name="end"/> &gt;= 0 the ones that have no end tag of their own end there (their content runs up to
    /// <paramref name="end"/>), as a browser ends them with the enclosing element; a self-closed tag (&lt;span/&gt;) stays
    /// without content. Every tag is removed once, so all the scans of the open list together are linear.
    /// </summary>
    private static void TruncateOpenList(TagNode[] tags, TagState[] states, int first, int end,
        int[] openInList, Dictionary<string, int>? customNames, ref int lastOpen)
    {
        int k = lastOpen;
        while (k >= first)
        {
            ref TagState state = ref states[k];
            ref var tag = ref tags[k];

            if (state.Key >= 0) openInList[state.Key]--;
            else AdjustOpenCustomName(customNames, tag.CustomName, -1);

            // InnerEnd != 0: the tag an end tag has just been matched to
            if (end >= 0 && !state.SelfClosed && tag.InnerEnd == 0)
            {
                // an empty tag right before the next one: "<li><li>" gives end == InnerStart - 1, an empty body
                tag.InnerEnd = end;
                tag.End = end;
            }

            k = state.PrevOpen;
        }

        lastOpen = k;
    }

    /// <summary>Counts an open tag with a custom name in (+1) or out (-1).</summary>
    private static void AdjustOpenCustomName(Dictionary<string, int>? names, string? name, int delta)
    {
        if (names == null || string.IsNullOrEmpty(name)) return;

        ref int open = ref CollectionsMarshal.GetValueRefOrNullRef(names, name);
        if (!Unsafe.IsNullRef(ref open)) open += delta;
    }

    /// <summary>
    /// For an end tag with a custom name: whether a tag with that name is open, and the name as the one string
    /// instance the open tags hold (so that candidates are compared by reference).
    /// </summary>
    private static bool TryGetOpenCustomName(Dictionary<string, int>? names, ReadOnlySpan<char> name, out string? interned)
    {
        interned = null;
        if (names == null) return false;

        Span<char> lower = name.Length <= 128 ? stackalloc char[128] : new char[name.Length];
        lower = lower.Slice(0, name.Length);
        name.ToLowerInvariant(lower);

        return names.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(lower, out interned, out int open) && open > 0;
    }

    /// <summary>The stack position of the outermost target of <paramref name="rule"/> below the first stop tag, or -1.</summary>
    private static int ScopeStart(ImpliedEndRule rule, TagState[] states, int[] stack, int stackTop)
    {
        int from = -1;
        for (int s = stackTop - 1; s >= 0; s--)
        {
            int k = states[stack[s]].Key;
            if (k < 0) continue;
            if (rule.Targets[k]) from = s;
            else if (rule.Stops[k]) break;
        }
        return from;
    }

    /// <summary>
    /// FastMode: applies <paramref name="rule"/> for the tag that starts at <paramref name="tagStartPosition"/>.
    /// If a tag it ends was itself misnested (<c>&lt;td&gt;&lt;span&gt;x&lt;td&gt;y&lt;tr&gt;</c>: the second &lt;td&gt; did not end
    /// the first one), that part is scanned again with the rules first.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyRuleInFastMode(ref ParseState st, ImpliedEndRule rule, int tagStartPosition)
    {
        int from = ScopeStart(rule, st.States, st.Stack, st.StackTop);
        if (from < 0) return;

        if (FirstMisnested(st.States, st.Stack, from, st.StackTop) is var misnested && misnested >= 0)
        {
            Repair(ref st, misnested, tagStartPosition - 1);
        }

        CloseInScope(rule, st.Tags, st.States, st.Stack, ref st.StackTop, null, ref st.OpenP, tagStartPosition - 2,
            st.OpenInList, st.CustomNames, ref st.LastOpen);
    }

    /// <summary>
    /// Scans the open tags from the innermost one up to the first stop tag and ends the outermost target found
    /// together with everything opened inside it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)] // rare: keeps the main loop small
    private static void CloseInScope(ImpliedEndRule rule, TagNode[] tags, TagState[] states, int[] stack, ref int stackTop,
        int[]? openByKey, ref int openP, int end, int[] openInList, Dictionary<string, int>? customNames, ref int lastOpen)
    {
        int from = ScopeStart(rule, states, stack, stackTop);
        if (from < 0) return;

        int first = stack[from];

        while (stackTop > from)
        {
            int key = states[stack[--stackTop]].Key;
            if (key == (int)HtmlTag.P) openP--;
            if (openByKey != null && key >= 0) openByKey[key]--;
        }

        TruncateOpenList(tags, states, first, end, openInList, customNames, ref lastOpen);
    }

    private sealed class ImpliedEndRule
    {
        public readonly bool[] Targets = new bool[TagNames.Length];
        public readonly bool[] Stops = new bool[TagNames.Length];
        public readonly int[] TargetKeys;

        public ImpliedEndRule(HtmlTag[] targets, HtmlTag[] stops)
        {
            foreach (var t in targets) Targets[(int)t] = true;
            foreach (var s in stops) Stops[(int)s] = true;
            TargetKeys = Array.ConvertAll(targets, t => (int)t);
        }

        /// <summary>Whether a target is open at all: in well-formed markup it is not, and the stack is not scanned.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyOpen(int[] openByKey)
        {
            foreach (var target in TargetKeys)
            {
                if (openByKey[target] > 0) return true;
            }
            return false;
        }
    }

    // Elements that limit the scope of the rules (HTML5 "has an element in scope").
    private static readonly HtmlTag[] ScopeStops =
        [HtmlTag.Html, HtmlTag.Body, HtmlTag.Table, HtmlTag.Caption, HtmlTag.Td, HtmlTag.Th, HtmlTag.Template,
         HtmlTag.Object, HtmlTag.Applet, HtmlTag.Marquee, HtmlTag.Button];

    // <p> is ended by the start of a block element (HTML5 "close a p element").
    private static readonly ImpliedEndRule PRule = new([HtmlTag.P], ScopeStops);

    // The tags whose start ends an open <p>.
    private static readonly bool[] ClosesP = BuildClosesP();

    private static bool[] BuildClosesP()
    {
        var closes = new bool[TagNames.Length];
        foreach (var tag in new[]
        {
            HtmlTag.Address, HtmlTag.Article, HtmlTag.Aside, HtmlTag.Blockquote, HtmlTag.Center, HtmlTag.Details,
            HtmlTag.Dialog, HtmlTag.Dir, HtmlTag.Div, HtmlTag.Dl, HtmlTag.Fieldset, HtmlTag.Figcaption, HtmlTag.Figure,
            HtmlTag.Footer, HtmlTag.Form, HtmlTag.H1, HtmlTag.H2, HtmlTag.H3, HtmlTag.H4, HtmlTag.H5, HtmlTag.H6,
            HtmlTag.Header, HtmlTag.Hgroup, HtmlTag.Hr, HtmlTag.Listing, HtmlTag.Main, HtmlTag.Menu, HtmlTag.Nav,
            HtmlTag.Ol, HtmlTag.P, HtmlTag.Pre, HtmlTag.Search, HtmlTag.Section, HtmlTag.Summary, HtmlTag.Table,
            HtmlTag.Ul, HtmlTag.Li, HtmlTag.Dd, HtmlTag.Dt, HtmlTag.Plaintext, HtmlTag.Xmp,
        })
        {
            closes[(int)tag] = true;
        }
        return closes;
    }

    // The tags whose start ends an open tag of the same kind (HTML5 optional end tags).
    private static readonly ImpliedEndRule?[] ImpliedEndRules = BuildImpliedEndRules();

    // The tags an optional-end-tag rule can end: left open in FastMode, they make that part be scanned again.
    // NB: after ImpliedEndRules, static fields are initialized in textual order.
    private static readonly bool[] IsRuleTarget = BuildIsRuleTarget();

    private static bool[] BuildIsRuleTarget()
    {
        var isTarget = new bool[TagNames.Length];
        foreach (var rule in ImpliedEndRules)
        {
            if (rule == null) continue;
            foreach (var key in rule.TargetKeys) isTarget[key] = true;
        }
        return isTarget;
    }

    private static ImpliedEndRule?[] BuildImpliedEndRules()
    {
        var rules = new ImpliedEndRule?[TagNames.Length];

        var li = new ImpliedEndRule([HtmlTag.Li], [.. ScopeStops, HtmlTag.Ul, HtmlTag.Ol, HtmlTag.Menu]);
        rules[(int)HtmlTag.Li] = li;

        var dtdd = new ImpliedEndRule([HtmlTag.Dt, HtmlTag.Dd], [.. ScopeStops, HtmlTag.Dl]);
        rules[(int)HtmlTag.Dt] = dtdd;
        rules[(int)HtmlTag.Dd] = dtdd;

        HtmlTag[] tableStops = [HtmlTag.Html, HtmlTag.Body, HtmlTag.Template, HtmlTag.Table];
        var cell = new ImpliedEndRule([HtmlTag.Td, HtmlTag.Th], [.. tableStops, HtmlTag.Tr, HtmlTag.Tbody, HtmlTag.Thead, HtmlTag.Tfoot]);
        rules[(int)HtmlTag.Td] = cell;
        rules[(int)HtmlTag.Th] = cell;
        rules[(int)HtmlTag.Tr] = new ImpliedEndRule([HtmlTag.Tr, HtmlTag.Td, HtmlTag.Th], [.. tableStops, HtmlTag.Tbody, HtmlTag.Thead, HtmlTag.Tfoot]);
        var section = new ImpliedEndRule([HtmlTag.Tbody, HtmlTag.Thead, HtmlTag.Tfoot, HtmlTag.Tr, HtmlTag.Td, HtmlTag.Th, HtmlTag.Caption], tableStops);
        rules[(int)HtmlTag.Tbody] = section;
        rules[(int)HtmlTag.Thead] = section;
        rules[(int)HtmlTag.Tfoot] = section;

        HtmlTag[] selectStops = [HtmlTag.Html, HtmlTag.Body, HtmlTag.Template, HtmlTag.Select, HtmlTag.Datalist];
        rules[(int)HtmlTag.Option] = new ImpliedEndRule([HtmlTag.Option], [.. selectStops, HtmlTag.Optgroup]);
        rules[(int)HtmlTag.Optgroup] = new ImpliedEndRule([HtmlTag.Option, HtmlTag.Optgroup], selectStops);

        return rules;
    }

    // HTML5 void elements (incl. obsolete ones the parsing rules also treat as void) and declarations
    // (the scan ends every "!..." / "?..." tag by its first char anyway, in XML too).
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

    /// <summary>Whether the content of the tag <paramref name="key"/> is raw text, not markup (script, style, textarea, title, ...).</summary>
    internal static bool IsRawText(int key) => key >= 0 && RawTextEnds[key] != null;

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

    /// <summary>The lower-case name as one string instance per document; the value counts the open tags with that name.</summary>
    private static string InternLowerName(ReadOnlySpan<char> name, ref Dictionary<string, int>? cache)
    {
        if (name.IsEmpty) return string.Empty;

        Span<char> lower = name.Length <= 128 ? stackalloc char[128] : new char[name.Length];
        lower = lower.Slice(0, name.Length);
        name.ToLowerInvariant(lower);

        cache ??= new Dictionary<string, int>(StringComparer.Ordinal);
        var lookup = cache.GetAlternateLookup<ReadOnlySpan<char>>();
        if (!lookup.TryGetValue(lower, out string? interned, out _))
        {
            interned = lower.ToString();
            cache.Add(interned, 0);
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

        // The name ends at whitespace or '/': <br/> is "br", as <br /> is.
        var index = 0;
        var sourcelen = tagsource.Length;
        while (index < sourcelen && !char.IsWhiteSpace(tagsource[index]) && tagsource[index] != '/')
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

    /// <summary>
    /// Whether the tag separates words in the one-line visible text (<c>Text()</c>, as jsoup's <c>text()</c>): &lt;br&gt;,
    /// block elements, p/h1–h6 and table cells, opening or closing. Inline and unknown tags do not ("a&lt;b&gt;b&lt;/b&gt;" is
    /// one word). The same tags as the line breaks of <c>TextLineBreaks()</c>.
    /// </summary>
    internal static bool SeparatesWords(HtmlTag tag) =>
        LineBreakKinds[(int)tag] is LineBreakKind.Line or LineBreakKind.Paragraph or LineBreakKind.Space;

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

                if (c != '<' || !HtmlToTextCore.IsMarkupStart(value, i))
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
