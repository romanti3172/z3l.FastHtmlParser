using System.Buffers;
using System.Runtime.CompilerServices;

namespace z3l.FastHtmlParser.Core;

internal static class HtmlToTextCore
{
    /// <summary>
    /// Whether the '&lt;' at <paramref name="i"/> starts markup: as in HTML5, only before an ASCII letter, '/', '!' or '?'.
    /// Otherwise ("a &lt; b", "&lt;3", a '&lt;' at the end) it is text.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsMarkupStart(ReadOnlySpan<char> value, int i)
    {
        if ((uint)(i + 1) >= (uint)value.Length) return false;
        char next = value[i + 1];
        return char.IsAsciiLetter(next) || next is '/' or '!' or '?';
    }

    /// <summary>
    /// <paramref name="value"/>[<paramref name="i"/>] is '&lt;'. Returns the index of the last char of what is not text there:
    /// a comment, a tag, or a tag together with invisible content (script, style, noscript, template, noframes, noembed)
    /// up to its end tag. -1: an unterminated tag, the rest is markup.
    /// </summary>
    internal static int SkipMarkup(ReadOnlySpan<char> value, int i)
    {
        var rest = value.Slice(i + 1);

        // a comment, even if it contains '>' or tags
        if (rest.StartsWith("!--")) return ParsingCore.FindCommentEnd(value, i + 4);

        // everything up to the next '>' is the tag (vectorized jump)
        int close = rest.IndexOf('>');
        if (close < 0) return -1;
        int tagEnd = i + 1 + close;

        // <script>...</script>: the code is not text; continue right before the end tag, which is skipped as a tag
        if (ParsingCore.InvisibleContentEnd(rest.Slice(0, close)) is { } endTag)
        {
            return ParsingCore.FindRawTextEnd(value, tagEnd + 1, endTag) - 1;
        }

        return tagEnd;
    }

    // char.IsWhiteSpace chars other than ' ': line breaks, tabs, no-break and other Unicode spaces.
    private static readonly SearchValues<char> OtherWhitespace = SearchValues.Create(
        "\t\n\v\f\r\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000");

    /// <summary>
    /// Whether trimmed text has whitespace to collapse: a line break, tab or other space than ' ', or two spaces in a row.
    /// Short texts in one scalar pass, long ones in two vectorized passes.
    /// </summary>
    internal static bool NeedsCollapse(ReadOnlySpan<char> text)
    {
        // Short texts (a title, a price, a name): one scalar pass is cheaper than a vectorized search per space.
        if (text.Length <= 64)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ')
                {
                    if (i + 1 < text.Length && char.IsWhiteSpace(text[i + 1])) return true;
                }
                else if ((c < ' ' || c >= '\u0085') && char.IsWhiteSpace(c)) return true;
            }
            return false;
        }

        // Long texts: two whole vectorized passes (restarting a search at every space between words costs more).
        return text.ContainsAny(OtherWhitespace) || text.IndexOf("  ") >= 0;
    }

    /// <summary>
    /// The markup at <paramref name="value"/>[<paramref name="i"/>] ('&lt;') is a tag that separates words in the one-line
    /// text: &lt;br&gt;, a block element or a table cell, opening or closing (<see cref="ParsingCore.SeparatesWords"/>).
    /// Comments, declarations, inline and unknown tags are not.
    /// </summary>
    internal static bool SeparatesWords(ReadOnlySpan<char> value, int i)
    {
        int start = i + 1;
        if (start < value.Length && value[start] == '/') start++;
        // The usual tags inside text have names of 1 or 2 letters: td, tr, li, p, br, h1 / a, b, i, em. Compared directly.
        int len = 0;
        while (start + len < value.Length && len < 3 && char.IsAsciiLetterOrDigit(value[start + len])) len++;
        if (len == 1) return (value[start] | 0x20) == 'p';
        if (len == 2)
        {
            char a = (char)(value[start] | 0x20), b = (char)(value[start + 1] | 0x20);
            return (a, b) switch
            {
                ('t', 'd' or 'r' or 'h') or ('l', 'i') or ('b', 'r') or ('u', 'l') or ('o', 'l') or ('d', 'l' or 't' or 'd') or ('h', 'r') => true,
                ('h', >= '1' and <= '6') => true,
                _ => false,
            };
        }
        // Longer: the name packed into a number, 6 bits a char, case-insensitive, and a binary search.
        ulong packed = 0;
        int length = 0;
        for (int k = start; k < value.Length; k++)
        {
            int code = value[k] switch
            {
                >= 'a' and <= 'z' and var l => l - 'a' + 1,
                >= 'A' and <= 'Z' and var u => u - 'A' + 1,
                >= '0' and <= '9' and var d => d - '0' + 27,
                _ => 0,
            };
            if (code == 0) break;
            if (++length > MaxSeparatorName) return false; // longer than every such tag
            packed = (packed << 6) | (uint)code;
        }
        return length > 0 && Array.BinarySearch(SeparatorNames, packed) >= 0;
    }

    private const int MaxSeparatorName = 10; // "blockquote", "figcaption"

    // The names of the tags that separate words, packed as in SeparatesWords, sorted for a binary search.
    private static readonly ulong[] SeparatorNames = BuildSeparatorNames();

    private static ulong[] BuildSeparatorNames()
    {
        var names = new List<ulong>();
        foreach (HtmlTag tag in Enum.GetValues<HtmlTag>())
        {
            if ((int)tag < 0 || (int)tag >= ParsingCore.TagNames.Length || !ParsingCore.SeparatesWords(tag)) continue;
            var name = ParsingCore.TagNames[(int)tag];
            if (name.Length > MaxSeparatorName) throw new InvalidOperationException("MaxSeparatorName is too small for " + name);
            ulong packed = 0;
            foreach (char c in name.ToLowerInvariant()) packed = (packed << 6) | (uint)(c <= '9' ? c - '0' + 27 : c - 'a' + 1);
            names.Add(packed);
        }
        names.Sort();
        return names.ToArray();
    }

    /// <summary>
    /// Text of a tag with no markup in it, whitespace collapsed to one space and trimmed, decoded: the leaf path of
    /// Text() when the text has line breaks or double spaces. Short texts are collapsed on the stack.
    /// </summary>
    internal static string CollapseText(ReadOnlySpan<char> text)
    {
        char[]? rented = null;
        Span<char> buffer = text.Length <= 512 ? stackalloc char[text.Length] : (rented = ArrayPool<char>.Shared.Rent(text.Length));
        try
        {
            int n = 0;
            bool prevWhitespace = true;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!prevWhitespace) { buffer[n++] = ' '; prevWhitespace = true; }
                }
                else
                {
                    buffer[n++] = c;
                    prevWhitespace = false;
                }
            }
            if (n > 0 && buffer[n - 1] == ' ') n--;
            var result = buffer[..n];
            return result.Contains('&') ? HtmlDecodeCore.HtmlDecode(result) : result.ToString();
        }
        finally
        {
            if (rented != null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Whether the markup at <paramref name="value"/>[<paramref name="i"/>] is a &lt;pre&gt;, &lt;listing&gt; or
    /// &lt;plaintext&gt; tag: +1 opening, -1 closing, 0 another one. Their text keeps its whitespace, as in jsoup.
    /// </summary>
    private static int PreTag(ReadOnlySpan<char> value, int i)
    {
        int start = i + 1, sign = 1;
        if (start < value.Length && value[start] == '/') { start++; sign = -1; }
        if (start >= value.Length || (value[start] | 0x20) is not ('p' or 'l')) return 0; // the usual tag: one compare
        int end = start;
        while (end < value.Length && char.IsAsciiLetter(value[end])) end++;
        var name = value[start..end];
        return name.Equals("pre", StringComparison.OrdinalIgnoreCase) || name.Equals("listing", StringComparison.OrdinalIgnoreCase)
            || name.Equals("plaintext", StringComparison.OrdinalIgnoreCase) ? sign : 0;
    }

    /// <summary>
    /// Removes tags, comments and invisible content (script, style, ...), collapses whitespace runs into one ' ' and trims:
    /// the visible text in one line, as jsoup's <c>text()</c>. &lt;br&gt;, block elements and table cells separate words
    /// ("a&lt;br&gt;b", "&lt;td&gt;a&lt;/td&gt;&lt;td&gt;b&lt;/td&gt;" → "a b"); inline tags do not ("a&lt;b&gt;b&lt;/b&gt;" → "ab").
    /// Inside &lt;pre&gt; (<paramref name="inPre"/>: the text itself is in one) whitespace is kept as written.
    /// Text is copied char by char (words are short, a vectorized search does not pay off there),
    /// tag contents are skipped with a vectorized jump to the next '&gt;'.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)] // one compiled copy for all callers: inlined into Text() it ran 2-3x slower
    internal static int StripTags(ReadOnlySpan<char> value, Span<char> destination, bool inPre = false)
    {
        int dstIdx = 0;
        bool prevWhitespace = true; // left trim
        int preDepth = inPre ? 1 : 0;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '<' && IsMarkupStart(value, i))
            {
                int last = SkipMarkup(value, i);
                if (last < 0) break;
                if (!inPre) preDepth = Math.Max(0, preDepth + PreTag(value, i));
                // Only after a word: next to whitespace the tag changes nothing (and costs no lookup).
                if (!prevWhitespace && SeparatesWords(value, i))
                {
                    destination[dstIdx++] = ' ';
                    prevWhitespace = true;
                }
                i = last;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (preDepth > 0)
                {
                    // as written, but not at the start
                    if (dstIdx > 0) destination[dstIdx++] = c;
                    prevWhitespace = true;
                }
                else if (!prevWhitespace)
                {
                    destination[dstIdx++] = ' ';
                    prevWhitespace = true;
                }
            }
            else
            {
                destination[dstIdx++] = c;
                prevWhitespace = false;
            }
        }

        // Right trim (inside <pre> there may be more than one trailing whitespace char).
        while (dstIdx > 0 && char.IsWhiteSpace(destination[dstIdx - 1]))
        {
            dstIdx--;
        }

        return dstIdx;
    }

    internal static string? ClearTags(ReadOnlySpan<char> value, bool inPre = false)
    {
        if (value.IsEmpty) return null;

        // Rent a temporary buffer from the pool to avoid heap allocations
        char[] buffer = ArrayPool<char>.Shared.Rent(value.Length);
        try
        {
            int length = StripTags(value, buffer, inPre);
            if (length == 0)
            {
                return string.Empty;
            }

            var stripped = buffer.AsSpan(0, length);

            // Only pay the HtmlDecode tax if an '&' was actually found
            return stripped.Contains('&')
                ? HtmlDecodeCore.HtmlDecode(stripped)
                : stripped.ToString();
        }
        finally
        {
            // Always return the rented array back to the pool
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
    internal static string? ClearTags(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        return ClearTags(value.AsSpan());
    }
}
