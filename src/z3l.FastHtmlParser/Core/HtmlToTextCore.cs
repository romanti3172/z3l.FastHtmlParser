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

    /// <summary>
    /// Removes tags, comments and invisible content (script, style, ...), collapses whitespace runs into one ' ' and trims.
    /// Text is copied char by char (words are short, a vectorized search does not pay off there),
    /// tag contents are skipped with a vectorized jump to the next '&gt;'.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)] // one compiled copy for all callers: inlined into Text() it ran 2-3x slower
    internal static int StripTags(ReadOnlySpan<char> value, Span<char> destination)
    {
        int dstIdx = 0;
        bool prevWhitespace = true; // left trim

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '<' && IsMarkupStart(value, i))
            {
                int last = SkipMarkup(value, i);
                if (last < 0) break;
                i = last;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (!prevWhitespace)
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

        // Right trim: whitespace is collapsed, so there is at most one trailing space.
        if (dstIdx > 0 && destination[dstIdx - 1] == ' ')
        {
            dstIdx--;
        }

        return dstIdx;
    }

    internal static string? ClearTags(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return null;

        // Rent a temporary buffer from the pool to avoid heap allocations
        char[] buffer = ArrayPool<char>.Shared.Rent(value.Length);
        try
        {
            int length = StripTags(value, buffer);
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
