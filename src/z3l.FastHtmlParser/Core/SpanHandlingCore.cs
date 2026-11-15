using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser.Core;
internal static class SpanHandlingCore
{
    internal static bool IsName(this ParserTag? tag, ReadOnlySpan<char> name)
    {
        if (tag == null) return default;

        var enumName = tag.Tag;
        if (enumName == null)
        {
            return MemoryExtensions.Equals(name, tag.CustomName, StringComparison.OrdinalIgnoreCase);
        }

        // Same result as a TagMap lookup, but a direct (length-first) compare instead of hashing.
        return MemoryExtensions.Equals(name, ParsingCore.TagNames[(int)enumName.GetValueOrDefault()], StringComparison.OrdinalIgnoreCase);
    }

    internal static ReadOnlySpan<char> SpanSource(ParserTag? item)
    {
        if (item == null) return default;

        var start = item.Start;
        var end = item.End;
        var innerStart = item.InnerStart;

        var pageSource = item.Document.PageSource.AsSpan();
        int length;

        if (innerStart > 0 && item.InnerEnd > 0)
        {
            length = innerStart - start - 2;
        }
        else if (pageSource[end - 1] == '/')
        {
            length = end - start - 2;
        }
        else
        {
            length = end - start - 1;
        }

        return pageSource.Slice(start + 1, length).Trim();
    }

    internal static ReadOnlySpan<char> SpanFullSource(ParserTag? item)
    {
        if (item == null) return default;

        if (item.InnerStart <= 0 || item.InnerEnd <= 0)
        {
            return default;
        }

        var start = item.Start;
        var end = item.End;

        return item.Document.PageSource.AsSpan()
            .Slice(start, end - start + 1)
            .Trim();
    }

    internal static ReadOnlySpan<char> SpanBody(ParserTag? item)
    {
        if (item == null) return default;

        var innerStart = item.InnerStart;
        var innerEnd = item.InnerEnd;

        if (innerStart < 0 || innerEnd <= 0)
        {
            return default;
        }

        return item.Document.PageSource.AsSpan()
            .Slice(innerStart, innerEnd - innerStart + 1)
            .Trim();
    }

    /// <summary>
    /// The raw value of the first attribute with the given name (case-insensitive), as a browser reads the tag:
    /// quoted or unquoted (<c>href=/p?a=1</c>), empty for an attribute without a value or a missing one.
    /// </summary>
    internal static ReadOnlySpan<char> SpanAttributeByName(ParserTag? tag, string attribute)
    {
        // a comment has no attributes, whatever its text looks like
        if (tag == null || string.IsNullOrEmpty(attribute) || tag.Key == (int)HtmlTag.Comment) return default;

        var source = tag.Document.PageSource.AsSpan();
        int end = AttributeScanner.OpenTagEnd(tag, source.Length);
        var open = source.Slice(tag.Start, end - tag.Start); // "<name attributes", without the closing '>'

        // The first place where the name stands as a word of its own. Most tags have none
        // ("id" in class="grid" is not one): vectorized searches settle that.
        int position = NextWord(open, attribute, 1);
        if (position < 0) return default;

        // That word is the attribute unless it is inside the value of another one (title="see id=5", x= id).
        // Nearly always that is ruled out at once (the word is the first one after the tag name: "<div class=")
        // or by a look at the chars before it, and its value is read in place.
        bool firstWord = tag.Key >= 0 && position == ParsingCore.TagNames[tag.Key].Length + 2;
        if (firstWord || IsNameForSure(open, position))
        {
            AttributeScanner.ReadValue(source, tag.Start + position + attribute.Length, end, out var start, out var length);
            return start < 0 ? default : source.Slice(start, length);
        }

        // Otherwise the attributes are read one by one from the start of the tag, up to the one that starts at that word.
        var scanner = new AttributeScanner(tag);
        while (scanner.Next(out var nameStart, out var nameLength, out var valueStart, out var valueLength))
        {
            int name = nameStart - tag.Start;

            // a word the scanner has passed was inside a value: the next one takes its place
            while (position < name)
            {
                position = NextWord(open, attribute, position + 1);
                if (position < 0) return default;
            }

            if (position == name && nameLength == attribute.Length)
            {
                return valueStart < 0 ? default : source.Slice(valueStart, valueLength);
            }
        }

        return default;
    }

    /// <summary>
    /// The next place at or after <paramref name="from"/> where the attribute name stands as a word of its own:
    /// every attribute with that name starts at one. -1 if there is none.
    /// </summary>
    private static int NextWord(ReadOnlySpan<char> open, string attribute, int from)
    {
        while (from < open.Length)
        {
            int found = open.Slice(from).IndexOf(attribute, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;

            from += found;
            int after = from + attribute.Length;
            if (StartsName(open[from - 1]) && (after == open.Length || EndsName(open[after]))) return from;

            from++;
        }

        return -1;
    }

    /// <summary>
    /// Whether the word at <paramref name="position"/> of an opening tag is an attribute name, judged by the chars
    /// before it alone. False means "not sure": the attributes are then read one by one.
    /// </summary>
    private static bool IsNameForSure(ReadOnlySpan<char> open, int position)
    {
        // After whitespace, and not as the unquoted value in "x= word".
        int last = position - 1;
        if (!char.IsWhiteSpace(open[last])) return false;
        while (last > 0 && char.IsWhiteSpace(open[last])) last--;
        if (open[last] == '=') return false;

        // Not inside a quoted value. Such a value starts with a quote right after '=' and runs up to the next quote
        // of the same kind, so the word is inside one only if no quote of that kind stands between the two.
        // It is enough that the last quote of each kind before the word does not open a value:
        // then every opening quote before it has its closing quote before the word as well.
        var before = open.Slice(0, last + 1);
        int doubleQuote = -1, singleQuote = -1;
        if (before.Length <= 32)
        {
            // usually "<div": a plain loop, the vectorized searches cost more than they save here
            for (int k = before.Length - 1; k > 0 && (doubleQuote < 0 || singleQuote < 0); k--)
            {
                if (before[k] == '"') { if (doubleQuote < 0) doubleQuote = k; }
                else if (before[k] == '\'' && singleQuote < 0) singleQuote = k;
            }
        }
        else
        {
            doubleQuote = before.LastIndexOf('"');
            singleQuote = before.LastIndexOf('\'');
        }

        return !OpensValue(before, doubleQuote) && !OpensValue(before, singleQuote);
    }

    /// <summary>Whether the quote at <paramref name="quote"/> (or -1: none) stands right after an '='.</summary>
    private static bool OpensValue(ReadOnlySpan<char> before, int quote)
    {
        if (quote < 0) return false;

        int k = quote - 1; // >= 0: the tag starts with '<'
        while (k > 0 && char.IsWhiteSpace(before[k])) k--;
        return before[k] == '=';
    }

    // What an attribute name can follow and what ends it, as AttributeScanner reads a tag.
    private static bool StartsName(char before) => char.IsWhiteSpace(before) || before is '"' or '\'' or '/' or '=';

    private static bool EndsName(char after) => char.IsWhiteSpace(after) || after is '=' or '/' or '>';

    internal static ReadOnlySpan<char> SpanAttributeByName(ParserTag? tag, HtmlAttribute attribute)
    {
        return SpanAttributeByName(tag, ParsingCore.AttributeNames[(int)attribute]);
    }

    internal static ReadOnlySpan<char> SpanAttributeId(ParserTag? tag)
    {
        return SpanAttributeByName(tag, HtmlAttribute.Id);
    }

    // Decoded attribute values (HTML5 attribute rules). Without '&' the value is copied as is.

    internal static string DecodeAttribute(ReadOnlySpan<char> raw) => HtmlDecodeCore.HtmlDecode(raw, inAttribute: true);

    internal static string AttributeValue(ParserTag? tag, string attribute) => DecodeAttribute(SpanAttributeByName(tag, attribute));

    internal static string AttributeValue(ParserTag? tag, HtmlAttribute attribute) => DecodeAttribute(SpanAttributeByName(tag, attribute));

    /// <summary>Compares the decoded value; no allocation unless the raw value contains '&amp;'.</summary>
    internal static bool AttributeEquals(ReadOnlySpan<char> raw, string? value)
    {
        return raw.Contains('&')
            ? string.Equals(DecodeAttribute(raw), value, StringComparison.Ordinal)
            : MemoryExtensions.Equals(raw, value, StringComparison.Ordinal);
    }

    /// <summary>Whether the decoded value starts with <paramref name="value"/>; no allocation unless the raw value contains '&amp;'.</summary>
    internal static bool AttributeStartsWith(ReadOnlySpan<char> raw, string value)
    {
        return raw.Contains('&')
            ? DecodeAttribute(raw).StartsWith(value, StringComparison.Ordinal)
            : raw.StartsWith(value, StringComparison.Ordinal);
    }

    internal static ReadOnlySpan<char> CopyFrom(ReadOnlySpan<char> value, string fromText)
    {
        if (fromText == null) return value;

        var index = value.IndexOf(fromText.AsSpan(), StringComparison.Ordinal);
        if (index > -1)
        {
            return value[(index + fromText.Length)..];
        }

        return [];
    }
    internal static ReadOnlySpan<char> CopyFrom(ReadOnlySpan<char> value, char fromChar)
    {
        var index = value.IndexOf(fromChar);
        if (index > -1)
        {
            return value[(index + 1)..];
        }

        return [];
    }

    internal static int SpanIndexOf(ReadOnlySpan<char> span, ReadOnlySpan<char> value, StringComparison comparisonType, int startIndex)
    {
        if (startIndex > span.Length) return -1;

        var indexInSlice = span[startIndex..].IndexOf(value, comparisonType);
        return indexInSlice == -1 ? -1 : startIndex + indexInSlice;
    }

    internal static int SpanIndexOf(ReadOnlySpan<char> span, char value, int startIndex)
    {
        if (startIndex > span.Length) return -1;

        var indexInSlice = span[startIndex..].IndexOf(value);
        return indexInSlice == -1 ? -1 : startIndex + indexInSlice;
    }
}
