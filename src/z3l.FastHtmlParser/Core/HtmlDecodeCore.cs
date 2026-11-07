using System.Runtime.CompilerServices;
using System.Text;

namespace z3l.FastHtmlParser.Core;

/// <summary>
/// HTML5 character reference decoder (WHATWG "character reference state").
/// Understands all 2125 named references (incl. multi-code-point and astral ones),
/// the 106 legacy names without a trailing ';' (&amp;copy, &amp;nbsp, ...),
/// numeric references with or without ';', and the spec replacements
/// (0, surrogates, &gt; U+10FFFF → U+FFFD; 0x80–0x9F → Windows-1252).
/// </summary>
internal static class HtmlDecodeCore
{
    /// <param name="input">Text to decode.</param>
    /// <param name="inAttribute">
    /// Attribute-value rules: a legacy name without ';' followed by '=' or an alphanumeric
    /// is left as is (so "?a=1&amp;copy=2" stays intact).
    /// </param>
    public static string HtmlDecode(ReadOnlySpan<char> input, bool inAttribute = false)
    {
        int amp = input.IndexOf('&');
        if (amp < 0)
        {
            return input.ToString();
        }

        // The decoded text is never longer than the input.
        DefaultInterpolatedStringHandler output = input.Length <= 256
            ? new DefaultInterpolatedStringHandler(0, 0, null, stackalloc char[256])
            : new DefaultInterpolatedStringHandler(input.Length, 0);

        Span<char> runeChars = stackalloc char[2];
        int pos = 0;

        while (amp >= 0)
        {
            output.AppendFormatted(input.Slice(pos, amp - pos));

            ReadOnlySpan<char> rest = input.Slice(amp + 1);
            int consumed;

            if (!rest.IsEmpty && rest[0] == '#')
            {
                consumed = MatchNumeric(rest, out uint codePoint);
                if (consumed > 0)
                {
                    int written = new Rune(codePoint).EncodeToUtf16(runeChars);
                    output.AppendFormatted(runeChars.Slice(0, written));
                }
            }
            else
            {
                consumed = MatchNamed(rest, inAttribute, out ReadOnlySpan<char> value);
                if (consumed > 0)
                {
                    output.AppendFormatted(value);
                }
            }

            if (consumed == 0)
            {
                // Not a character reference: keep '&' and continue right after it.
                output.AppendLiteral("&");
            }

            pos = amp + 1 + consumed;
            int next = input.Slice(pos).IndexOf('&');
            amp = next < 0 ? -1 : pos + next;
        }

        output.AppendFormatted(input.Slice(pos));
        return output.ToStringAndClear();
    }

    /// <summary>
    /// Decodes a single character reference. <paramref name="afterAmp"/> is the text right after '&amp;'.
    /// Writes the decoded chars (at most 2) to <paramref name="destination"/> and returns the number
    /// of chars consumed after '&amp;'; 0 = not a reference.
    /// </summary>
    internal static int DecodeReference(ReadOnlySpan<char> afterAmp, Span<char> destination, out int written)
    {
        written = 0;

        if (!afterAmp.IsEmpty && afterAmp[0] == '#')
        {
            int consumed = MatchNumeric(afterAmp, out uint codePoint);
            if (consumed > 0)
            {
                written = new Rune(codePoint).EncodeToUtf16(destination);
            }
            return consumed;
        }
        else
        {
            int consumed = MatchNamed(afterAmp, inAttribute: false, out ReadOnlySpan<char> value);
            if (consumed > 0)
            {
                value.CopyTo(destination);
                written = value.Length;
            }
            return consumed;
        }
    }

    /// <summary>
    /// <paramref name="s"/> starts with '#'. Returns the number of chars consumed (0 = not a reference).
    /// </summary>
    private static int MatchNumeric(ReadOnlySpan<char> s, out uint codePoint)
    {
        int i = 1;
        bool hex = i < s.Length && (s[i] | 0x20) == 'x';
        if (hex)
        {
            i++;
        }

        int digitsStart = i;
        uint value = 0;

        for (; i < s.Length; i++)
        {
            char c = s[i];
            uint digit;

            if ((uint)(c - '0') <= 9)
            {
                digit = (uint)(c - '0');
            }
            else if (hex && (uint)((c | 0x20) - 'a') <= 5)
            {
                digit = (uint)((c | 0x20) - 'a' + 10);
            }
            else
            {
                break;
            }

            // Saturate just above the Unicode range: no overflow on long digit runs.
            value = value * (hex ? 16u : 10u) + digit;
            if (value > 0x10FFFF)
            {
                value = 0x110000;
            }
        }

        if (i == digitsStart)
        {
            // "&#" / "&#x" without digits is plain text.
            codePoint = 0;
            return 0;
        }

        if (i < s.Length && s[i] == ';')
        {
            i++;
        }

        if (value == 0 || value > 0x10FFFF || value - 0xD800 <= 0x7FF)
        {
            value = 0xFFFD;
        }
        else if (value - 0x80 <= 0x1F)
        {
            char mapped = Windows1252[(int)(value - 0x80)];
            if (mapped != '\0')
            {
                value = mapped;
            }
        }

        codePoint = value;
        return i;
    }

    /// <summary>
    /// <paramref name="s"/> is the text right after '&amp;'. Returns the number of chars consumed (0 = not a reference).
    /// </summary>
    private static int MatchNamed(ReadOnlySpan<char> s, bool inAttribute, out ReadOnlySpan<char> value)
    {
        // Entity names are ASCII alphanumerics only.
        int limit = Math.Min(s.Length, HtmlEntities.MaxNameLength + 1);
        int len = 0;
        while (len < limit && char.IsAsciiLetterOrDigit(s[len]))
        {
            len++;
        }

        if (len >= 2)
        {
            // Full match: "&name;".
            if (len < s.Length && s[len] == ';' && HtmlEntities.TryGet(s.Slice(0, len), out value, out _))
            {
                return len + 1;
            }

            // Longest legacy prefix without ';': "&copy2024" → "©2024", "&notit;" → "¬it;".
            for (int k = Math.Min(len, HtmlEntities.MaxLegacyNameLength); k >= 2; k--)
            {
                if (HtmlEntities.TryGet(s.Slice(0, k), out value, out bool legacy) && legacy)
                {
                    if (inAttribute && k < s.Length && (s[k] == '=' || char.IsAsciiLetterOrDigit(s[k])))
                    {
                        break;
                    }

                    return k;
                }
            }
        }

        value = default;
        return 0;
    }

    // Replacements for numeric references 0x80..0x9F; '\0' = keep the code point as is.
    private static ReadOnlySpan<char> Windows1252 =>
        "€\0‚ƒ„…†‡ˆ‰Š‹Œ\0Ž\0" +
        "\0‘’“”•–—˜™š›œ\0žŸ";
}

internal static partial class HtmlEntities
{
    // name → (entry index << 1) | legacy flag. A plain Dictionary<string, int>: built in ~2 ms on first use
    // (a FrozenDictionary needed ~15 ms more to JIT) and it is as fast or faster for these lookups.
    private static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> s_lookup = BuildLookup();

    private static Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> BuildLookup()
    {
        var map = new Dictionary<string, int>(Count, StringComparer.Ordinal);
        int index = 0;

        foreach (Range range in Names.AsSpan().Split(' '))
        {
            ReadOnlySpan<char> name = Names.AsSpan(range);
            bool legacy = name[0] == '+';
            if (legacy) name = name.Slice(1);

            map.Add(name.ToString(), (index << 1) | (legacy ? 1 : 0));
            index++;
        }

        return map.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Looks up a name (without '&amp;' and ';'); the value is a slice of the <c>Values</c> constant.</summary>
    public static bool TryGet(ReadOnlySpan<char> name, out ReadOnlySpan<char> value, out bool legacy)
    {
        if (!s_lookup.TryGetValue(name, out int entry))
        {
            value = default;
            legacy = false;
            return false;
        }

        int start = (entry >> 1) * 2;
        value = Values.AsSpan(start, Values[start + 1] == '\0' ? 1 : 2);
        legacy = (entry & 1) != 0;
        return true;
    }
}
