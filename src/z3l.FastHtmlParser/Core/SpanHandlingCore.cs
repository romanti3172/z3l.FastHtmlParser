
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

    internal static ReadOnlySpan<char> SpanAttributeByName(ParserTag? tag, string attribute)
    {
        if (tag == null || string.IsNullOrEmpty(attribute)) return default;

        var source = SpanSource(tag);
        var sourceLen = source.Length;
        var attrLen = attribute.Length;
        var searchOffset = 0;

        while (searchOffset < sourceLen)
        {
            var relativeIndex = source[searchOffset..].IndexOf(attribute, StringComparison.OrdinalIgnoreCase);
            if (relativeIndex == -1) break;

            var index = searchOffset + relativeIndex;

            var isStartOk = index == 0 || char.IsWhiteSpace(source[index - 1]);

            if (isStartOk && index + attrLen < sourceLen)
            {
                var nextIdx = index + attrLen;

                while (nextIdx < sourceLen && char.IsWhiteSpace(source[nextIdx]))
                {
                    nextIdx++;
                }

                if (nextIdx < sourceLen && source[nextIdx] == '=')
                {
                    nextIdx++;

                    while (nextIdx < sourceLen && char.IsWhiteSpace(source[nextIdx]))
                    {
                        nextIdx++;
                    }

                    if (nextIdx < sourceLen)
                    {
                        char quote = source[nextIdx];

                        if (quote == '\'' || quote == '"')
                        {
                            nextIdx++;
                            var closingQuoteRelative = source[nextIdx..].IndexOf(quote);

                            if (closingQuoteRelative != -1)
                            {
                                return source.Slice(nextIdx, closingQuoteRelative);
                            }

                            searchOffset = nextIdx;
                            continue;
                        }
                        else if (quote != '=')
                        {
                            var valStart = nextIdx;
                            while (nextIdx < sourceLen)
                            {
                                var c = source[nextIdx];
                                if (char.IsWhiteSpace(c) || c == '=' || c == '\'' || c == '"')
                                {
                                    break;
                                }
                                nextIdx++;
                            }
                            return source[valStart..nextIdx];
                        }
                    }
                }

                searchOffset = index + attrLen;
            }
            else
            {
                searchOffset = index + 1;
            }
        }

        return default;
    }

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
