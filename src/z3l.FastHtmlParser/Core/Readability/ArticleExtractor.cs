using System.Text.RegularExpressions;
using z3l.FastHtmlParser.Core.Sanitizer;
using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser.Core.Readability;

/// <summary>
/// Finds the main content of a page by the method of Mozilla's Readability (the reader view of Firefox),
/// on the tag list as the parser recorded it: nothing is removed or moved, what Readability removes is marked.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Scripts, styles, forms controls, menus, hidden elements and, by their class and id, the parts that are
/// unlikely to be content (sidebars, comments, footers, ads) are marked as left out.</item>
/// <item>The text of every element is counted once, in one pass: its length, commas and the text of its links.</item>
/// <item>Each paragraph (p, pre, td, a div with no blocks in it, the loose text of a div) of 25 chars and more gives
/// points to its parent, half of them to the grandparent and less to the next three ancestors; an ancestor starts
/// with points for its tag and its class and id. The points of an element are lowered by the share of link text.</item>
/// <item>The element with most points, moved up the tree where its ancestors are as good, is the article,
/// together with those of its siblings that are good enough.</item>
/// <item>In the article, the parts that look like lists of links, forms, share buttons or ads are left out, and
/// the rest is written by the sanitizer with the attributes of content only.</item>
/// <item>When the article has less than 500 chars of text, it is looked for again with fewer rules, and the
/// longest one is taken.</item>
/// </list>
/// </remarks>
internal sealed partial class ArticleExtractor
{
    private const int FlagStripUnlikelys = 1, FlagWeightClasses = 2, FlagCleanConditionally = 4;
    private const int CharThreshold = 500;
    private const int TopCandidates = 5;

    // A byline, a title heading, an ad label are short: an element with more markup than this is none of them, and its
    // text is not read (on deeply nested markup reading it for every element would take quadratic time).
    private const int ShortElementChars = 4096;

    private readonly HtmlParser _document;
    private readonly TagList _data;
    private readonly int _count;

    // read once from the attributes
    private readonly string?[] _class;
    private readonly string?[] _id;
    private readonly byte[] _attributes;
    private const byte Hidden = 1, UnlikelyRole = 2, AuthorMark = 4, HashLink = 8, Presentation = 16, ItempropName = 32, ModalDialog = 64;
    private const byte NotDataTable = 1, HasSummary = 2;
    private readonly byte[] _tableHints;

    // the tag of an element is in a <table> or in <code> / <pre>
    private readonly byte[] _ancestors;
    private const byte InTable = 1, InCode = 2, InFigure = 4;

    private ArticleMetadata _metadata;
    private bool[]? _dataTables; // of the current attempt
    private string? _pageByline; // the byline found in the page by the first attempt that found one

    private ArticleExtractor(HtmlParser document)
    {
        _document = document;
        _data = document.Data;
        _count = _data.Count;
        _class = new string?[_count];
        _id = new string?[_count];
        _attributes = new byte[_count];
        _ancestors = new byte[_count];
        _tableHints = new byte[_count];
        ReadAttributes();
    }

    internal static ReadableArticle? Extract(HtmlParser document)
    {
        if (document.Data.Count == 0) return null;

        var extractor = new ArticleExtractor(document);
        return extractor.Run();
    }

    /// <summary>Readability's isProbablyReaderable: enough shown paragraphs of text outside the parts that look like sidebars.</summary>
    internal static bool IsProbablyReadable(HtmlParser document, int minContentLength, double minScore)
    {
        var data = document.Data;
        int count = data.Count;
        var withBr = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            ref readonly var node = ref data.Node(i);
            if (node.Key == (int)HtmlTag.Br && node.ParentIndex >= 0 && data.Node(node.ParentIndex).Key == (int)HtmlTag.Div) withBr.Add(node.ParentIndex);
        }

        double score = 0;
        // The text of each candidate is read from its own markup: paragraphs nested in each other would read the
        // same text again and again. Past this many chars read the page counts as not readable.
        long budget = 4L * document.Source.Length + 65536;
        for (int i = 0; i < count; i++)
        {
            ref readonly var node = ref data.Node(i);
            int key = node.Key;
            if (!(key is (int)HtmlTag.P or (int)HtmlTag.Pre or (int)HtmlTag.Article || withBr.Contains(i))) continue;

            var tag = data[i];
            if (HasAttribute(document, in node, "hidden")) continue;
            if (tag.AttributeByName("aria-hidden").Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
                && !tag.AttributeByName("class").Contains("fallback-image", StringComparison.Ordinal)) continue;
            if (HiddenStyle().IsMatch(tag.AttributeByName("style"))) continue;

            var match = string.Concat(tag.AttributeByName("class"), " ", tag.AttributeByName("id"));
            if (Unlikely().IsMatch(match) && !MaybeCandidate().IsMatch(match)) continue;

            if (key == (int)HtmlTag.P)
            {
                bool inItem = false;
                for (int a = node.ParentIndex; a >= 0 && !inItem; a = data.Node(a).ParentIndex) inItem = data.Node(a).Key == (int)HtmlTag.Li;
                if (inItem) continue;
            }

            // textContent.trim(): the whitespace inside counts
            budget -= node.End - node.Start;
            if (budget < 0) return false;
            int length = RawTextLength(document, i);
            if (length < minContentLength) continue;

            score += Math.Sqrt(length - minContentLength);
            if (score > minScore) return true;
        }
        return false;
    }

    private static bool HasAttribute(HtmlParser document, in ParsingCore.TagNode node, string name)
    {
        int openEnd = node.InnerStart > 0 && node.InnerEnd > 0 ? node.InnerStart - 1 : node.End;
        var source = document.Source;
        var scanner = new AttributeScanner(source, node.Start, openEnd);
        while (scanner.Next(out int nameStart, out int nameLength, out _, out _))
        {
            if (source.Slice(nameStart, nameLength).Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ----- one attempt -----

    private struct Stats
    {
        public int Own, Text, Commas, Link;          // Link: the text of links, a "#" link counted at 0.3
        public int InlineText, InlineCommas, InlineLink; // the text that no block inside the element holds
        public int P, Img, Li, Input, Embed, Tr, Td, Th, Tables, HeadingText, ListText, TextishText;
        public int Words;                            // non-whitespace chars of the element's own text
        public int AllWords;                         // the same of all its text
        public int Elements;                         // child elements that are not left out
        public int MaxCells;                         // the most cells of a row in it
        public bool BlockInside, AnyBlock, DataHints, Video, DataTableInside;
        public bool Reached;                         // an element the pass went through (not inside a left-out one)
    }

    private sealed class Attempt
    {
        public string Content = string.Empty;
        public int TextLength;
        public string? Byline;
    }

    private ReadableArticle? Run()
    {
        _metadata = ReadMetadata();
        string? title = _metadata.Title ?? ArticleTitle();

        Attempt? best = null;
        int flags = FlagStripUnlikelys | FlagWeightClasses | FlagCleanConditionally;
        while (true)
        {
            var attempt = Grab(flags, title);
            if (best == null || attempt.TextLength > best.TextLength) best = attempt;
            if (attempt.TextLength >= CharThreshold) break;

            if ((flags & FlagStripUnlikelys) != 0) flags &= ~FlagStripUnlikelys;
            else if ((flags & FlagWeightClasses) != 0) flags &= ~FlagWeightClasses;
            else if ((flags & FlagCleanConditionally) != 0) flags &= ~FlagCleanConditionally;
            else break;
        }

        if (best.TextLength == 0) return null;

        string? excerpt = _metadata.Excerpt;
        if (string.IsNullOrWhiteSpace(excerpt))
        {
            excerpt = new HtmlParser(best.Content).Find(HtmlTag.P).Text();
        }

        return new ReadableArticle(best.Content, _document.PageUrl.Length > 0 ? _document.PageUrl : null)
        {
            Title = NullIfEmpty(title),
            Byline = NullIfEmpty(_metadata.Byline ?? _pageByline),
            Excerpt = NullIfEmpty(excerpt),
            SiteName = NullIfEmpty(_metadata.SiteName),
            PublishedTime = NullIfEmpty(_metadata.PublishedTime),
            Image = NullIfEmpty(_metadata.Image),
            Language = NullIfEmpty(Language()),
        };
    }

    private Attempt Grab(int flags, string? title)
    {
        var attempt = new Attempt();
        var excluded = Exclude(flags, title, out attempt.Byline);
        var stats = Count(excluded);

        int top = TopCandidate(flags, stats, out var score, out var initialized);
        var siblings = top >= 0 ? Siblings(top, flags, stats, score, initialized) : Fallback(stats);

        var clean = Clean(flags, excluded, stats);

        var policy = new SanitizerPolicy(ContentOptions) { LazyImages = true };
        int capacity = 0;
        foreach (int s in siblings) capacity += _data.Node(s).End - _data.Node(s).Start + 1;

        // Readability's renames: a div with no blocks in it is a paragraph, an h1 in the article an h2, and a part of the
        // article that is not a common block (a td, a form, a span) a div.
        var renames = new string?[_count];
        for (int i = 0; i < _count; i++)
        {
            ref readonly var s = ref stats[i];
            if (!s.Reached) continue;
            int key = _data.Node(i).Key;
            if (key == (int)HtmlTag.Div && !s.AnyBlock) renames[i] = "p";
            else if (key == (int)HtmlTag.H1) renames[i] = "h2";
        }
        foreach (int s in siblings)
        {
            int key = _data.Node(s).Key;
            bool common = key is (int)HtmlTag.Div or (int)HtmlTag.Article or (int)HtmlTag.Section or (int)HtmlTag.P
                or (int)HtmlTag.Ol or (int)HtmlTag.Ul or (int)HtmlTag.Pre or (int)HtmlTag.Blockquote or (int)HtmlTag.Table
                or (int)HtmlTag.Figure or (int)HtmlTag.Main or (int)HtmlTag.H1 or (int)HtmlTag.H2 or (int)HtmlTag.H3
                or (int)HtmlTag.H4 or (int)HtmlTag.H5 or (int)HtmlTag.H6 or (int)HtmlTag.Body or (int)HtmlTag.Html;
            if (!common) renames[s] = "div";
        }

        var writer = new SanitizerWriter(_document, policy, capacity / 2);
        try
        {
            foreach (int s in siblings)
            {
                if (clean[s]) continue;
                ref readonly var node = ref _data.Node(s);
                writer.Write(s, node.Start, node.End, clean, renames);
            }
            attempt.Content = writer.ToString();
        }
        finally
        {
            writer.Dispose();
        }

        attempt.TextLength = HtmlText.ToText(attempt.Content)?.Length ?? 0;
        return attempt;
    }

    // ----- the attributes -----

    private void ReadAttributes()
    {
        var source = _document.Source;
        for (int i = 0; i < _count; i++)
        {
            ref readonly var node = ref _data.Node(i);
            int parent = node.ParentIndex;
            byte inherited = parent >= 0 ? _ancestors[parent] : (byte)0;
            int pk = parent >= 0 ? _data.Node(parent).Key : -1;
            if (pk is (int)HtmlTag.Table) inherited |= InTable;
            if (pk is (int)HtmlTag.Code or (int)HtmlTag.Pre) inherited |= InCode;
            if (pk is (int)HtmlTag.Figure) inherited |= InFigure;
            _ancestors[i] = inherited;

            if (!node.IsElement) continue;

            int openEnd = node.InnerStart > 0 && node.InnerEnd > 0 ? node.InnerStart - 1 : node.End;
            var scanner = new AttributeScanner(source, node.Start, openEnd);
            byte flags = 0;
            bool modal = false, dialog = false;
            while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
            {
                var name = source.Slice(nameStart, nameLength);
                var value = valueStart >= 0 ? source.Slice(valueStart, valueLength) : default;
                if (name.Equals("class", StringComparison.OrdinalIgnoreCase)) _class[i] ??= Decode(value);
                else if (name.Equals("id", StringComparison.OrdinalIgnoreCase)) _id[i] ??= Decode(value);
                else if (name.Equals("hidden", StringComparison.OrdinalIgnoreCase)) flags |= Hidden;
                else if (name.Equals("aria-hidden", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) flags |= Hidden;
                }
                else if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    if (HiddenStyle().IsMatch(Decode(value))) flags |= Hidden;
                }
                else if (name.Equals("role", StringComparison.OrdinalIgnoreCase))
                {
                    var role = value.Trim();
                    foreach (var unlikely in UnlikelyRoles)
                    {
                        if (role.Equals(unlikely, StringComparison.OrdinalIgnoreCase)) flags |= UnlikelyRole;
                    }
                    if (role.Equals("presentation", StringComparison.OrdinalIgnoreCase)) flags |= Presentation;
                    if (role.Equals("dialog", StringComparison.OrdinalIgnoreCase)) dialog = true;
                }
                else if (name.Equals("aria-modal", StringComparison.OrdinalIgnoreCase))
                {
                    modal = value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (name.Equals("summary", StringComparison.OrdinalIgnoreCase))
                {
                    if (!value.Trim().IsEmpty) _tableHints[i] |= HasSummary;
                }
                else if (name.Equals("datatable", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Trim().Equals("0", StringComparison.Ordinal)) _tableHints[i] |= NotDataTable;
                }
                else if (name.Equals("rel", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Trim().Equals("author", StringComparison.OrdinalIgnoreCase)) flags |= AuthorMark;
                }
                else if (name.Equals("itemprop", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Contains("author", StringComparison.OrdinalIgnoreCase)) flags |= AuthorMark;
                    if (value.Contains("name", StringComparison.OrdinalIgnoreCase)) flags |= ItempropName;
                }
                else if (name.Equals("href", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.TrimStart().StartsWith('#')) flags |= HashLink;
                }
            }
            if (modal && dialog) flags |= ModalDialog;
            _attributes[i] = flags;
        }
    }

    private static string Decode(ReadOnlySpan<char> raw)
    {
        return HtmlDecodeCore.NeedsDecoding(raw) ? HtmlDecodeCore.HtmlDecode(raw, inAttribute: true) : raw.ToString();
    }

    private static readonly string[] UnlikelyRoles = ["menu", "menubar", "complementary", "navigation", "alert", "alertdialog", "dialog"];

    private string MatchString(int i)
    {
        var c = _class[i];
        var id = _id[i];
        return c == null ? id ?? string.Empty : id == null ? c : string.Concat(c, " ", id);
    }

    private int ClassWeight(int i, int flags)
    {
        if ((flags & FlagWeightClasses) == 0) return 0;

        int weight = 0;
        if (_class[i] is { Length: > 0 } c)
        {
            if (Negative().IsMatch(c)) weight -= 25;
            if (Positive().IsMatch(c)) weight += 25;
        }
        if (_id[i] is { Length: > 0 } id)
        {
            if (Negative().IsMatch(id)) weight -= 25;
            if (Positive().IsMatch(id)) weight += 25;
        }
        return weight;
    }

    // ----- what is left out -----

    private static readonly bool[] RemovedTags = BuildRemovedTags();

    private static bool[] BuildRemovedTags()
    {
        var removed = new bool[ParsingCore.TagNames.Length];
        foreach (var tag in new[]
        {
            HtmlTag.Script, HtmlTag.Style, HtmlTag.Noscript, HtmlTag.Template, HtmlTag.Title, HtmlTag.Meta, HtmlTag.Link,
            HtmlTag.Base, HtmlTag.Svg, HtmlTag.Iframe, HtmlTag.Object, HtmlTag.Embed, HtmlTag.Applet,
            HtmlTag.Canvas, HtmlTag.Audio, HtmlTag.Video, HtmlTag.Button, HtmlTag.Select, HtmlTag.Textarea, HtmlTag.Input,
            HtmlTag.Option, HtmlTag.Optgroup, HtmlTag.Datalist, HtmlTag.Dialog,
            HtmlTag.Frameset, HtmlTag.Frame, HtmlTag.Noembed, HtmlTag.Noframes, HtmlTag.Param, HtmlTag.Source,
            HtmlTag.Track, HtmlTag.Map, HtmlTag.Area,
        })
        {
            removed[(int)tag] = true;
        }
        return removed;
    }

    /// <summary>
    /// The tags left out of this attempt, with what is inside them; the byline found in the page, if it is used.
    /// </summary>
    private bool[] Exclude(int flags, string? title, out string? byline)
    {
        var excluded = new bool[_count];
        bool strip = (flags & FlagStripUnlikelys) != 0;
        bool lookForByline = _metadata.Byline == null && _pageByline == null;
        bool lookForTitle = !string.IsNullOrEmpty(title);
        byline = null;

        for (int i = 0; i < _count; i++)
        {
            ref readonly var node = ref _data.Node(i);
            int parent = node.ParentIndex;
            if (parent >= 0 && excluded[parent] || !node.IsElement)
            {
                excluded[i] = true;
                continue;
            }

            int key = node.Key;
            if (key >= 0 && RemovedTags[key] || (_attributes[i] & Hidden) != 0)
            {
                excluded[i] = true;
                continue;
            }
            if ((_attributes[i] & ModalDialog) != 0)
            {
                excluded[i] = true;
                continue;
            }
            if (key is (int)HtmlTag.Html or (int)HtmlTag.Head) continue;

            if (lookForByline && node.End - node.Start <= ShortElementChars
                && ((_attributes[i] & AuthorMark) != 0 || (_class[i] != null || _id[i] != null) && Byline().IsMatch(MatchString(i))))
            {
                // Readability measures textContent with its whitespace, only trimmed: a block laid out over
                // many indented lines is no byline
                int rawLength = RawTextLength(i);
                var text = rawLength is > 0 and < 100 ? _data[i].Text() : null;
                if (!string.IsNullOrEmpty(text))
                {
                    // a part of it marked as the name is the more exact byline
                    int end = _data.Node(i).End;
                    for (int k = i + 1; k < _count && _data.Node(k).Start <= end; k++)
                    {
                        if ((_attributes[k] & ItempropName) != 0 && _data[k].Text() is { Length: > 0 } itemName)
                        {
                            text = itemName;
                            break;
                        }
                    }
                    byline = text;
                    _pageByline = text; // found once: Readability keeps it for the next attempts
                    lookForByline = false;
                    excluded[i] = true;
                    continue;
                }
            }

            if (lookForTitle && key is (int)HtmlTag.H1 or (int)HtmlTag.H2 && node.End - node.Start <= ShortElementChars)
            {
                var heading = _data[i].Text();
                if (!string.IsNullOrEmpty(heading) && TextSimilarity(title!, heading) > 0.75)
                {
                    lookForTitle = false;
                    excluded[i] = true;
                    continue;
                }
            }

            if (strip && key != (int)HtmlTag.Body)
            {
                if ((_attributes[i] & UnlikelyRole) != 0)
                {
                    excluded[i] = true;
                    continue;
                }

                if (key != (int)HtmlTag.A && (_ancestors[i] & (InTable | InCode)) == 0 && (_class[i] != null || _id[i] != null))
                {
                    var match = MatchString(i);
                    if (Unlikely().IsMatch(match) && !MaybeCandidate().IsMatch(match))
                    {
                        excluded[i] = true;
                        continue;
                    }
                }
            }
        }
        return excluded;
    }

    // ----- the text -----

    /// <summary>The char of the character reference at <paramref name="i"/> ('&amp;'), moving past it; '&amp;' when there is none.</summary>
    private static char DecodeAt(ReadOnlySpan<char> text, ref int i)
    {
        Span<char> decoded = stackalloc char[2];
        int consumed = HtmlDecodeCore.DecodeReference(text.Slice(i + 1), decoded, out _);
        if (consumed == 0) return '&';
        i += consumed;
        return decoded[0];
    }

    /// <summary>
    /// The length of the text of an element as the DOM's textContent.trim() gives it: every char counts,
    /// whitespace too, a character reference as one; scripts, styles and comments give nothing.
    /// </summary>
    private int RawTextLength(int index) => RawTextLength(_document, index);

    private static int RawTextLength(HtmlParser document, int index)
    {
        var data = document.Data;
        int count = data.Count;
        ref readonly var node = ref data.Node(index);
        if (node.InnerStart <= 0 || node.InnerEnd <= 0) return 0;

        var source = document.Source;
        int end = Math.Min(node.InnerEnd + 1, source.Length);
        int n = 0, first = -1, last = 0;
        int pos = node.InnerStart;

        void Count(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<' && HtmlToTextCore.IsMarkupStart(text, i))
                {
                    int close = text.Slice(i + 1).IndexOf('>');
                    if (close < 0) return;
                    i += close + 1;
                    continue;
                }
                if (c == '&') c = DecodeAt(text, ref i);
                if (!char.IsWhiteSpace(c))
                {
                    if (first < 0) first = n;
                    last = n + 1;
                }
                n++;
            }
        }

        for (int k = index + 1; k < count; k++)
        {
            ref readonly var child = ref data.Node(k);
            if (child.Start >= end) break;
            if (child.Start > pos) Count(source.Slice(pos, child.Start - pos));

            bool hidden = !child.IsElement || child.Key is (int)HtmlTag.Script or (int)HtmlTag.Style or (int)HtmlTag.Noscript or (int)HtmlTag.Template;
            if (hidden)
            {
                pos = Math.Max(pos, child.End + 1);
                int childEnd = child.End;
                while (k + 1 < count && data.Node(k + 1).Start <= childEnd) k++;
            }
            else
            {
                pos = Math.Max(pos, child.InnerStart > 0 && child.InnerEnd > 0 ? child.InnerStart : child.End + 1);
            }
        }
        if (end > pos) Count(source.Slice(pos, end - pos));
        return first < 0 ? 0 : last - first;
    }

    /// <summary>
    /// The text of every element that is not left out, counted in one pass: each piece of text between two tags
    /// belongs to the innermost element open there, and then to all its ancestors.
    /// </summary>
    private Stats[] Count(bool[] excluded)
    {
        var stats = new Stats[_count];
        var stack = new int[64];
        int top = 0;
        int pos = 0;
        var source = _document.Source;

        for (int i = 0; i < _count; i++)
        {
            ref readonly var node = ref _data.Node(i);
            int tagStart = node.Start;

            while (top > 0 && _data.Node(stack[top - 1]).End < tagStart)
            {
                int open = stack[--top];
                ref readonly var openNode = ref _data.Node(open);
                Gap(source, pos, openNode.InnerEnd + 1, ref stats[open]);
                pos = Math.Max(pos, openNode.End + 1);
            }
            if (top > 0) Gap(source, pos, tagStart, ref stats[stack[top - 1]]);

            bool container = node.InnerStart > 0 && node.InnerEnd > 0;
            pos = container ? node.InnerStart : node.End + 1;

            if (excluded[i])
            {
                if (top > 0 && node.IsElement && node.Key is var key && key >= 0)
                {
                    ref var owner = ref stats[stack[top - 1]];
                    if (key is (int)HtmlTag.Input or (int)HtmlTag.Select or (int)HtmlTag.Textarea or (int)HtmlTag.Button) owner.Input++;
                    else if (key is (int)HtmlTag.Iframe or (int)HtmlTag.Object or (int)HtmlTag.Embed)
                    {
                        if (IsVideo(source, in node)) owner.Video = true;
                        else owner.Embed++;
                    }
                }

                int nodeEnd = node.End;
                pos = Math.Max(pos, nodeEnd + 1);
                while (i + 1 < _count && _data.Node(i + 1).Start <= nodeEnd) i++;
                continue;
            }

            stats[i].Reached = true;
            if (container && !(node.Key >= 0 && ParsingCore.VoidTags[node.Key]))
            {
                if (top == stack.Length) Array.Resize(ref stack, top * 2);
                stack[top++] = i;
            }
        }

        while (top > 0)
        {
            int open = stack[--top];
            ref readonly var openNode = ref _data.Node(open);
            Gap(source, pos, openNode.InnerEnd + 1, ref stats[open]);
            pos = Math.Max(pos, openNode.End + 1);
        }

        // From the last tag to the first: every element is done before its parent.
        for (int i = _count - 1; i >= 0; i--)
        {
            ref var s = ref stats[i];
            if (!s.Reached) continue;

            ref readonly var node = ref _data.Node(i);
            int key = node.Key;
            s.Text += s.Own;
            s.InlineText += s.Own;
            s.AllWords += s.Words;
            if (key == (int)HtmlTag.A) s.Link = (_attributes[i] & HashLink) != 0 ? (int)(s.Text * 0.3) : s.Text;

            int parent = node.ParentIndex;
            if (parent < 0 || !stats[parent].Reached) continue;

            ref var p = ref stats[parent];
            p.Text += s.Text;
            p.AllWords += s.AllWords;
            p.Commas += s.Commas;
            p.Link += s.Link;
            p.P += s.P + (key == (int)HtmlTag.P ? 1 : 0);
            p.Img += s.Img + (key == (int)HtmlTag.Img ? 1 : 0);
            p.Li += s.Li + (key == (int)HtmlTag.Li ? 1 : 0);
            p.Input += s.Input;
            p.Embed += s.Embed;
            p.Tr += s.Tr + (key == (int)HtmlTag.Tr ? 1 : 0);
            p.Td += s.Td + (key == (int)HtmlTag.Td ? 1 : 0);
            p.Th += s.Th + (key == (int)HtmlTag.Th ? 1 : 0);
            p.Tables += s.Tables + (key == (int)HtmlTag.Table ? 1 : 0);
            p.DataHints |= s.DataHints || key is (int)HtmlTag.Thead or (int)HtmlTag.Tfoot or (int)HtmlTag.Colgroup or (int)HtmlTag.Col or (int)HtmlTag.Th
                || key == (int)HtmlTag.Caption && s.Text > 0;
            p.HeadingText += s.HeadingText + (key is >= (int)HtmlTag.H1 and <= (int)HtmlTag.H6 ? s.Text : 0);
            p.ListText += key is (int)HtmlTag.Ul or (int)HtmlTag.Ol ? s.Text : s.ListText;
            p.TextishText += s.TextishText + (key >= 0 && TextishTags[key] ? s.Text : 0);
            p.Video |= s.Video;
            p.Elements++;
            p.MaxCells = Math.Max(p.MaxCells, key == (int)HtmlTag.Tr ? s.Td + s.Th : s.MaxCells);
            p.DataTableInside |= s.DataTableInside || key == (int)HtmlTag.Table && IsDataTable(i, in s);

            bool block = key >= 0 && BlockTags[key];
            p.BlockInside |= s.BlockInside || key >= 0 && DivToPTags[key];
            p.AnyBlock |= s.AnyBlock || key >= 0 && (BlockTags[key] || DivToPTags[key]);
            if (!block)
            {
                p.InlineText += s.InlineText;
                p.InlineCommas += s.InlineCommas;
                p.InlineLink += key == (int)HtmlTag.A ? s.Link : s.InlineLink;
            }
        }
        return stats;
    }

    /// <summary>Counts the text of <c>Source[from..to)</c>: whitespace runs as one char, a character reference as one.</summary>
    private static void Gap(ReadOnlySpan<char> source, int from, int to, ref Stats owner)
    {
        if (to > source.Length) to = source.Length;
        if (to <= from) return;

        var text = source.Slice(from, to - from);
        int length = 0, commas = 0, words = 0;
        bool space = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<' && HtmlToTextCore.IsMarkupStart(text, i))
            {
                int close = text.Slice(i + 1).IndexOf('>');
                if (close < 0) break;
                i += close + 1;
                continue;
            }
            if (c == '&') c = DecodeAt(text, ref i);
            if (char.IsWhiteSpace(c))
            {
                if (!space) length++;
                space = true;
                continue;
            }
            space = false;
            length++;
            words++;
            if (c is ',' or '،' or '﹐' or '︐' or '︑' or '⹁' or '⸴' or '⸲' or '，')
            {
                commas++;
            }
        }
        owner.Own += length;
        owner.Words += words;
        owner.Commas += commas;
        owner.InlineCommas += commas;
    }

    // The tags whose presence makes a div a container rather than a paragraph (Readability's DIV_TO_P_ELEMS).
    private static readonly bool[] DivToPTags = Flags(
        HtmlTag.Blockquote, HtmlTag.Dl, HtmlTag.Div, HtmlTag.Img, HtmlTag.Ol, HtmlTag.P, HtmlTag.Pre, HtmlTag.Table, HtmlTag.Ul);

    // The tags that end a run of loose text in a div.
    private static readonly bool[] BlockTags = Flags(
        HtmlTag.Address, HtmlTag.Article, HtmlTag.Aside, HtmlTag.Blockquote, HtmlTag.Details, HtmlTag.Dialog, HtmlTag.Dd,
        HtmlTag.Div, HtmlTag.Dl, HtmlTag.Dt, HtmlTag.Fieldset, HtmlTag.Figcaption, HtmlTag.Figure, HtmlTag.Footer,
        HtmlTag.Form, HtmlTag.H1, HtmlTag.H2, HtmlTag.H3, HtmlTag.H4, HtmlTag.H5, HtmlTag.H6, HtmlTag.Header,
        HtmlTag.Hgroup, HtmlTag.Hr, HtmlTag.Li, HtmlTag.Main, HtmlTag.Nav, HtmlTag.Ol, HtmlTag.P, HtmlTag.Pre,
        HtmlTag.Section, HtmlTag.Table, HtmlTag.Ul, HtmlTag.Tr, HtmlTag.Td, HtmlTag.Th, HtmlTag.Tbody, HtmlTag.Thead,
        HtmlTag.Tfoot, HtmlTag.Caption, HtmlTag.Img, HtmlTag.Body, HtmlTag.Html);

    // Readability's "textish" tags of _getTextDensity: span, li, td and DIV_TO_P_ELEMS.
    private static readonly bool[] TextishTags = Flags(
        HtmlTag.Span, HtmlTag.Li, HtmlTag.Td, HtmlTag.Blockquote, HtmlTag.Dl, HtmlTag.Div, HtmlTag.Img, HtmlTag.Ol, HtmlTag.P,
        HtmlTag.Pre, HtmlTag.Table, HtmlTag.Ul);

    private static bool IsVideo(ReadOnlySpan<char> source, in ParsingCore.TagNode node)
    {
        int openEnd = node.InnerStart > 0 && node.InnerEnd > 0 ? node.InnerStart - 1 : node.End;
        var scanner = new AttributeScanner(source, node.Start, openEnd);
        while (scanner.Next(out _, out _, out int valueStart, out int valueLength))
        {
            if (valueStart >= 0 && Videos().IsMatch(source.Slice(valueStart, valueLength))) return true;
        }
        if (node.Key == (int)HtmlTag.Object && node.InnerStart > 0 && node.InnerEnd >= node.InnerStart)
        {
            return Videos().IsMatch(source.Slice(node.InnerStart, node.InnerEnd - node.InnerStart + 1));
        }
        return false;
    }

    private static bool[] Flags(params HtmlTag[] tags)
    {
        var flags = new bool[ParsingCore.TagNames.Length];
        foreach (var tag in tags) flags[(int)tag] = true;
        return flags;
    }

    private static double LinkDensity(in Stats s) => s.Text == 0 ? 0 : (double)s.Link / s.Text;

    // ----- the candidates -----

    private int TopCandidate(int flags, Stats[] stats, out double[] score, out bool[] initialized)
    {
        score = new double[_count];
        initialized = new bool[_count];
        var merged = new bool[_count];

        for (int i = 0; i < _count; i++)
        {
            ref readonly var s = ref stats[i];
            if (!s.Reached) continue;

            ref readonly var node = ref _data.Node(i);
            int key = node.Key;
            if (merged[i]) continue;
            bool paragraph = key is (int)HtmlTag.P or (int)HtmlTag.Pre or (int)HtmlTag.Td or (int)HtmlTag.Section
                or (int)HtmlTag.H2 or (int)HtmlTag.H3 or (int)HtmlTag.H4 or (int)HtmlTag.H5 or (int)HtmlTag.H6
                || key == (int)HtmlTag.Div && !s.BlockInside;

            // A div with only a <p> in it and no text of its own is that paragraph (Readability puts the p in its place).
            if (key == (int)HtmlTag.Div && s.Elements == 1 && s.Words == 0 && LinkDensity(in s) < 0.25
                && OnlyChild(i, stats) is var only && only >= 0 && _data.Node(only).Key == (int)HtmlTag.P)
            {
                merged[only] = true;
                paragraph = true;
            }

            if (paragraph)
            {
                if (s.Text >= 25 && node.ParentIndex >= 0)
                {
                    Score(node.ParentIndex, 1 + s.Commas + 1 + Math.Min(s.Text / 100, 3), flags, score, initialized);
                }
            }
            else if (key == (int)HtmlTag.Div && s.InlineText >= 25)
            {
                // the loose text of a div with blocks in it: Readability puts it in a <p> of its own
                Score(i, 1 + s.InlineCommas + 1 + Math.Min(s.InlineText / 100, 3), flags, score, initialized);
            }
        }

        // the points of an element are lowered by the share of the text of its links
        var candidates = new List<int>();
        for (int i = 0; i < _count; i++)
        {
            if (!initialized[i]) continue;
            score[i] *= 1 - LinkDensity(in stats[i]);
            candidates.Add(i);
        }
        if (candidates.Count == 0) return -1;

        var scores = score;
        candidates.Sort((a, b) => scores[b].CompareTo(scores[a]) is var c && c != 0 ? c : a.CompareTo(b));
        int top = candidates[0];
        int topKey = _data.Node(top).Key;
        if (topKey is (int)HtmlTag.Body or (int)HtmlTag.Html) return -1;

        // When three of the other good candidates are inside one ancestor of the best one, that ancestor is the article.
        var alternatives = new List<int>();
        for (int k = 1; k < Math.Min(TopCandidates, candidates.Count); k++)
        {
            if (score[candidates[k]] / score[top] >= 0.75) alternatives.Add(candidates[k]);
        }
        if (alternatives.Count >= 3)
        {
            for (int ancestor = Parent(top); ancestor >= 0 && !IsBody(ancestor); ancestor = Parent(ancestor))
            {
                int inside = 0;
                foreach (int a in alternatives)
                {
                    if (IsAncestor(ancestor, a)) inside++;
                }
                if (inside >= 3)
                {
                    top = ancestor;
                    break;
                }
            }
        }
        if (!initialized[top]) Initialize(top, flags, score, initialized);

        // Up the tree while the parent has more points; an ancestor with much less ends the search.
        int parentOfTop = Parent(top);
        double lastScore = score[top];
        double threshold = lastScore / 3;
        while (parentOfTop >= 0 && !IsBody(parentOfTop))
        {
            if (!initialized[parentOfTop])
            {
                parentOfTop = Parent(parentOfTop);
                continue;
            }
            double parentScore = score[parentOfTop];
            if (parentScore < threshold) break;
            if (parentScore > lastScore)
            {
                top = parentOfTop;
                break;
            }
            lastScore = parentScore;
            parentOfTop = Parent(parentOfTop);
        }

        // An only child is its parent's content.
        for (int parent = Parent(top); parent >= 0 && !IsBody(parent) && ChildCount(parent, stats) == 1; parent = Parent(top))
        {
            top = parent;
        }
        if (!initialized[top]) Initialize(top, flags, score, initialized);
        return top;
    }

    private void Score(int start, int points, int flags, double[] score, bool[] initialized)
    {
        int ancestor = start;
        for (int level = 0; level < 5 && ancestor >= 0; level++, ancestor = Parent(ancestor))
        {
            // Readability skips the root element: its parent is the document
            if (_data.Node(ancestor).Key == (int)HtmlTag.Html) continue;

            if (!initialized[ancestor]) Initialize(ancestor, flags, score, initialized);
            double divider = level == 0 ? 1 : level == 1 ? 2 : level * 3;
            score[ancestor] += points / divider;
        }
    }

    private void Initialize(int i, int flags, double[] score, bool[] initialized)
    {
        initialized[i] = true;
        score[i] = _data.Node(i).Key switch
        {
            (int)HtmlTag.Div => 5,
            (int)HtmlTag.Pre or (int)HtmlTag.Td or (int)HtmlTag.Blockquote => 3,
            (int)HtmlTag.Address or (int)HtmlTag.Ol or (int)HtmlTag.Ul or (int)HtmlTag.Dl or (int)HtmlTag.Dd
                or (int)HtmlTag.Dt or (int)HtmlTag.Li or (int)HtmlTag.Form => -3,
            (int)HtmlTag.H1 or (int)HtmlTag.H2 or (int)HtmlTag.H3 or (int)HtmlTag.H4 or (int)HtmlTag.H5
                or (int)HtmlTag.H6 or (int)HtmlTag.Th => -5,
            _ => 0,
        };
        score[i] += ClassWeight(i, flags);
    }

    private int Parent(int i) => _data.Node(i).ParentIndex;

    private bool IsBody(int i) => _data.Node(i).Key is (int)HtmlTag.Body or (int)HtmlTag.Html;

    private bool IsAncestor(int ancestor, int i)
    {
        for (int p = Parent(i); p >= 0; p = Parent(p))
        {
            if (p == ancestor) return true;
        }
        return false;
    }

    private static int ChildCount(int parent, Stats[] stats) => stats[parent].Elements;

    private int OnlyChild(int parent, Stats[] stats)
    {
        int end = _data.Node(parent).End;
        for (int i = parent + 1; i < _count && _data.Node(i).Start <= end; i++)
        {
            if (_data.Node(i).ParentIndex == parent && stats[i].Reached) return i;
        }
        return -1;
    }

    /// <summary>The elements of an element, in order, that are not left out.</summary>
    private List<int> Children(int parent, Stats[] stats)
    {
        var children = new List<int>();
        int end = parent >= 0 ? _data.Node(parent).End : int.MaxValue;
        for (int i = parent + 1; i < _count; i++)
        {
            ref readonly var node = ref _data.Node(i);
            if (node.Start > end) break;
            if (node.ParentIndex == parent && stats[i].Reached) children.Add(i);
        }
        return children;
    }

    /// <summary>The best one and those of its siblings that are good enough.</summary>
    private List<int> Siblings(int top, int flags, Stats[] stats, double[] score, bool[] initialized)
    {
        int parent = Parent(top);
        if (parent < 0) return [top];

        var result = new List<int>();
        double threshold = Math.Max(10, score[top] * 0.2);
        string? topClass = _class[top];

        foreach (int sibling in Children(parent, stats))
        {
            if (sibling == top)
            {
                result.Add(sibling);
                continue;
            }

            double bonus = 0;
            if (!string.IsNullOrEmpty(topClass) && topClass == _class[sibling]) bonus += score[top] * 0.2;

            if (initialized[sibling] && score[sibling] + bonus >= threshold)
            {
                result.Add(sibling);
            }
            else if (_data.Node(sibling).Key == (int)HtmlTag.P)
            {
                ref readonly var s = ref stats[sibling];
                double density = LinkDensity(in s);
                if (s.Text > 80 && density < 0.25)
                {
                    result.Add(sibling);
                }
                else if (s.Text < 80 && s.Text > 0 && density == 0 && _data[sibling].Text() is { } text
                    && (text.Contains(". ", StringComparison.Ordinal) || text.EndsWith('.')))
                {
                    result.Add(sibling);
                }
            }
        }
        return result;
    }

    /// <summary>No candidate: the body is the article (or the whole page, when it has no body).</summary>
    private List<int> Fallback(Stats[] stats)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_data.Node(i).Key == (int)HtmlTag.Body && stats[i].Reached) return [i];
        }
        return Children(-1, stats);
    }

    // ----- cleaning the article -----

    /// <summary>The parts of the article that are left out of it: lists of links, forms, share buttons and the like.</summary>
    private bool[] Clean(int flags, bool[] excluded, Stats[] stats)
    {
        var clean = (bool[])excluded.Clone();
        bool conditionally = (flags & FlagCleanConditionally) != 0;

        _dataTables = new bool[_count];
        for (int i = 0; i < _count; i++)
        {
            if (stats[i].Reached && _data.Node(i).Key == (int)HtmlTag.Table) _dataTables[i] = IsDataTable(i, in stats[i]);
        }

        for (int i = 0; i < _count; i++)
        {
            ref readonly var s = ref stats[i];
            if (!s.Reached || clean[i]) continue;

            int parent = Parent(i);
            if (parent >= 0 && clean[parent])
            {
                clean[i] = true;
                continue;
            }

            int key = _data.Node(i).Key;

            // headings that look like the parts of a page around the article
            if (key is (int)HtmlTag.H1 or (int)HtmlTag.H2 && ClassWeight(i, flags) < 0)
            {
                clean[i] = true;
                continue;
            }

            if ((_class[i] != null || _id[i] != null) && s.Text < 500 && Share().IsMatch(MatchString(i)))
            {
                clean[i] = true;
                continue;
            }

            // what Readability's _clean removes from the article
            if (key is (int)HtmlTag.Aside or (int)HtmlTag.Footer)
            {
                clean[i] = true;
                continue;
            }

            // an empty paragraph
            if ((key == (int)HtmlTag.P || key == (int)HtmlTag.Div && !s.AnyBlock) && s.AllWords == 0 && s.Img == 0 && s.Embed == 0 && !s.Video)
            {
                clean[i] = true;
                continue;
            }

            if (conditionally && key is (int)HtmlTag.Form or (int)HtmlTag.Fieldset or (int)HtmlTag.Table or (int)HtmlTag.Ul or (int)HtmlTag.Div)
            {
                clean[i] = ShouldClean(i, key, flags, in s);
            }
        }
        return clean;
    }

    /// <summary>Readability's _cleanConditionally: whether an element of the article is more likely not a part of it.</summary>
    private bool ShouldClean(int i, int key, int flags, in Stats s)
    {
        bool isList = key is (int)HtmlTag.Ul or (int)HtmlTag.Ol || s.Text > 0 && (double)s.ListText / s.Text > 0.9;

        if (key == (int)HtmlTag.Table && _dataTables![i]) return false;
        if ((_ancestors[i] & InTable) != 0 && InDataTable(i)) return false;
        if ((_ancestors[i] & InCode) != 0) return false;
        if (s.DataTableInside) return false;

        int weight = ClassWeight(i, flags);
        if (weight < 0) return true;
        if (s.Commas >= 10) return false;
        if (s.Video) return false;

        if (s.Text < 40 && _data.Node(i).End - _data.Node(i).Start <= ShortElementChars
            && _data[i].Text() is { } text && (AdWords().IsMatch(text) || LoadingWords().IsMatch(text))) return true;

        int p = s.P, img = s.Img, li = s.Li - 100, input = s.Input, embed = s.Embed;
        double linkDensity = LinkDensity(in s);
        double headingDensity = s.Text == 0 ? 0 : (double)s.HeadingText / s.Text;
        double textDensity = s.Text == 0 ? 0 : (double)s.TextishText / s.Text;
        bool inFigure = (_ancestors[i] & InFigure) != 0;

        bool remove = (!inFigure && img > 1 && (double)p / img < 0.5)
            || (!isList && li > p)
            || (input > p / 3)
            || (!isList && !inFigure && headingDensity < 0.9 && s.Text < 25 && (img == 0 || img > 2) && linkDensity > 0)
            || (!isList && weight < 25 && linkDensity > 0.2)
            || (weight >= 25 && linkDensity > 0.5)
            || (embed == 1 && s.Text < 75 || embed > 1)
            || (img == 0 && textDensity == 0);

        // a list of images stays: every item holds one image
        if (remove && isList && s.Li == img && img > 0) return false;
        return remove;
    }

    /// <summary>Readability's _markDataTables: a table of data, not of the layout of the page.</summary>
    private bool IsDataTable(int i, in Stats s)
    {
        if ((_attributes[i] & Presentation) != 0 || (_tableHints[i] & NotDataTable) != 0) return false;
        if ((_tableHints[i] & HasSummary) != 0 || s.DataHints) return true;
        if (s.Tables > 0) return false; // a table with tables in it is the layout of a page

        int rows = s.Tr, columns = s.MaxCells;
        if (rows == 1 || columns == 1) return false;
        if (rows >= 10 || columns > 4) return true;
        return rows * columns > 10;
    }

    private bool InDataTable(int i)
    {
        for (int p = Parent(i); p >= 0; p = Parent(p))
        {
            if (_dataTables![p]) return true;
        }
        return false;
    }

    /// <summary>What the article keeps: the tags and attributes of content, no classes, ids or styles.</summary>
    private static readonly SanitizeOptions ContentOptions = new()
    {
        // the text of a formula stays, as text
        RemovedWithContent = new(SanitizeOptions.DefaultRemovedWithContent.Where(t => t != "math"), StringComparer.OrdinalIgnoreCase),
        AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "abbr", "address", "article", "b", "bdi", "bdo", "big", "blockquote", "br", "caption", "center", "cite",
            "code", "col", "colgroup", "data", "dd", "del", "details", "dfn", "div", "dl", "dt", "em", "figcaption",
            "figure", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "i", "img", "ins", "kbd", "li", "main", "mark",
            "ol", "p", "picture", "pre", "q", "rp", "rt", "ruby", "s", "samp", "section", "small", "source", "span",
            "strike", "strong", "sub", "summary", "sup", "table", "tbody", "td", "tfoot", "th", "thead", "time", "tr",
            "tt", "u", "ul", "var", "wbr",
        },
        AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            "abbr", "alt", "cite", "colspan", "datetime", "dir", "headers", "height", "href", "lang", "open", "reversed",
            "rowspan", "scope", "sizes", "span", "src", "srcset", "start", "title", "type", "value", "width",
        },
    };

    private string? Language()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_data.Node(i).Key == (int)HtmlTag.Html) return _data[i].AttributeByName("lang").Trim();
        }
        return null;
    }

    private static string? NullIfEmpty(string? value)
    {
        if (value == null) return null;
        value = value.Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// How much of <paramref name="b"/> is in <paramref name="a"/>, by words: 1 when every word of b is in a.
    /// </summary>
    private static double TextSimilarity(string a, string b)
    {
        var tokensA = new HashSet<string>(Words().Split(a.ToLowerInvariant()).Where(t => t.Length > 0));
        var tokensB = Words().Split(b.ToLowerInvariant()).Where(t => t.Length > 0).ToList();
        if (tokensA.Count == 0 || tokensB.Count == 0) return 0;

        var unique = tokensB.Where(t => !tokensA.Contains(t)).ToList();
        double distance = (double)string.Join(" ", unique).Length / string.Join(" ", tokensB).Length;
        return 1 - distance;
    }

    // ----- Readability's patterns -----

    [GeneratedRegex(@"-ad-|ai2html|banner|breadcrumbs|combx|comment|community|cover-wrap|disqus|extra|footer|gdpr|header|legends|menu|related|remark|replies|rss|shoutbox|sidebar|skyscraper|social|sponsor|supplemental|ad-break|agegate|pagination|pager|popup|yom-remote", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Unlikely();

    [GeneratedRegex(@"and|article|body|column|content|main|mathjax|shadow", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaybeCandidate();

    [GeneratedRegex(@"article|body|content|entry|hentry|h-entry|main|page|pagination|post|text|blog|story", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Positive();

    [GeneratedRegex(@"-ad-|hidden|^hid$| hid$| hid |^hid |banner|combx|comment|com-|contact|footer|gdpr|masthead|media|meta|outbrain|promo|related|scroll|share|shoutbox|sidebar|skyscraper|sponsor|shopping|tags|widget", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Negative();

    [GeneratedRegex(@"byline|author|dateline|writtenby|p-author", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Byline();

    [GeneratedRegex(@"(\b|_)(share|sharedaddy)(\b|_)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Share();

    [GeneratedRegex(@"display\s*:\s*none|visibility\s*:\s*hidden", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiddenStyle();

    [GeneratedRegex(@"\/\/(www\.)?((dailymotion|youtube|youtube-nocookie|player\.vimeo|v\.qq|bilibili|live.bilibili)\.com|(archive|upload\.wikimedia)\.org|player\.twitch\.tv)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Videos();

    [GeneratedRegex(@"^(ad(vertising|vertisement)?|pub(licité)?|werb(ung)?|广告|Реклама|Anuncio)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdWords();

    [GeneratedRegex(@"^((loading|正在加载|Загрузка|chargement|cargando)(…|\.\.\.)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoadingWords();

    [GeneratedRegex(@"\W+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
}
