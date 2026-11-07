using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>Checks of a single tag: name, class, id, attributes, text, or a whole <see cref="TagQuery"/>.</summary>
public static class ParserTagMatchExtensions
{
    /// <summary>Whether the tag matches every criterion of <paramref name="query"/>.</summary>
    public static bool Is(this ParserTag? tag, in TagQuery query)
    {
        return tag != null && query.Matches(tag);
    }

    /// <summary>Whether the tag has the given name and, when they are not null, the class, id and visible text.</summary>
    public static bool Is(this ParserTag? tag, HtmlTag name, string? cls = null, string? id = null, string? text = null)
    {
        return tag.Is(new TagQuery(name) { Class = cls, Id = id, Text = text });
    }

    /// <summary>
    /// Whether the tag has the given name (case-insensitive; null: any) and, when they are not null,
    /// the class, id and visible text.
    /// </summary>
    public static bool Is(this ParserTag? tag, string? name, string? cls = null, string? id = null, string? text = null)
    {
        return tag.Is(new TagQuery(name) { Class = cls, Id = id, Text = text });
    }

    /// <summary>Whether the tag has the given name.</summary>
    public static bool IsName(this ParserTag? tag, HtmlTag name)
    {
        return tag != null && tag.Key == (int)name;
    }

    /// <summary>Whether the tag has the given name (case-insensitive).</summary>
    public static bool IsName(this ParserTag? tag, string name)
    {
        return tag.IsName(name.AsSpan());
    }

    /// <summary>Whether the decoded attribute value equals the given value.</summary>
    public static bool IsAttribute(this ParserTag? tag, string attributeName, string attributeValue)
    {
        return SpanHandlingCore.AttributeEquals(SpanHandlingCore.SpanAttributeByName(tag, attributeName), attributeValue);
    }

    /// <summary>Whether the decoded attribute value equals the given value.</summary>
    public static bool IsAttribute(this ParserTag? tag, HtmlAttribute attributeName, string attributeValue)
    {
        return SpanHandlingCore.AttributeEquals(SpanHandlingCore.SpanAttributeByName(tag, attributeName), attributeValue);
    }

    /// <summary>Whether the tag has the given id.</summary>
    public static bool IsId(this ParserTag? tag, string? id)
    {
        if (string.IsNullOrEmpty(id)) return default;

        return tag.IsAttribute(HtmlAttribute.Id, id);
    }

    /// <summary>Whether the tag has the class (one of its space-separated classes, case-insensitive).</summary>
    public static bool IsClass(this ParserTag? tag, string className)
    {
        if (tag == null || string.IsNullOrEmpty(className)) return false;

        var rawClass = SpanHandlingCore.SpanAttributeByName(tag, HtmlAttribute.Class);
        var classValue = rawClass.Contains('&') ? SpanHandlingCore.DecodeAttribute(rawClass).AsSpan() : rawClass;
        if (classValue.IsEmpty) return false;

        var attrLen = classValue.Length;
        var classLen = className.Length;

        if (attrLen == classLen)
        {
            return MemoryExtensions.Equals(classValue, className, StringComparison.OrdinalIgnoreCase);
        }

        if (classLen > attrLen) return false;

        var currentSpan = classValue;
        int accumulatedIndex = 0;

        while (true)
        {
            var index = currentSpan.IndexOf(className, StringComparison.OrdinalIgnoreCase);
            if (index == -1) break;

            var absIndex = accumulatedIndex + index;

            var isStartOk = absIndex == 0 || char.IsWhiteSpace(classValue[absIndex - 1]);
            if (isStartOk)
            {
                var endIdx = absIndex + classLen;
                var isEndOk = endIdx == attrLen || char.IsWhiteSpace(classValue[endIdx]);

                if (isEndOk) return true;
            }

            var nextStart = index + classLen;
            if (nextStart >= currentSpan.Length) break;

            currentSpan = currentSpan[nextStart..];
            accumulatedIndex += nextStart;
        }

        return false;
    }

    /// <summary>
    /// Whether the visible text equals <paramref name="expected"/>: tags, comments and the content of script, style,
    /// noscript, template, noframes, noembed removed, whitespace collapsed, entities decoded.
    /// Compares on the fly without allocations and stops at the first mismatch.
    /// </summary>
    public static bool IsText(this ParserTag? tag, string expected)
    {
        var value = SpanHandlingCore.SpanBody(tag);

        if (value.IsEmpty || string.IsNullOrEmpty(expected)) return false;

        int matched = 0;
        bool prevWhitespace = true;   // left trim
        bool pendingSpace = false;    // a collapsed space counts only if more text follows (right trim)

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '<')
            {
                // tags, comments and invisible content (script, style, ...) are not text
                int last = HtmlToTextCore.SkipMarkup(value, i);
                if (last < 0) break;
                i = last;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (!prevWhitespace)
                {
                    pendingSpace = true;
                    prevWhitespace = true;
                }
            }
            else
            {
                // Text with entities must be decoded before comparing: take the full path.
                if (c == '&')
                {
                    return string.Equals(HtmlToTextCore.ClearTags(value), expected, StringComparison.Ordinal);
                }

                if (pendingSpace)
                {
                    if (matched >= expected.Length || expected[matched] != ' ') return false;
                    matched++;
                    pendingSpace = false;
                }

                if (matched >= expected.Length || expected[matched] != c) return false;
                matched++;
                prevWhitespace = false;
            }
        }

        return matched == expected.Length;
    }

    /// <summary>Whether the inner HTML equals the given value.</summary>
    public static bool IsBody(this ParserTag? tag, string body)
    {
        return MemoryExtensions.Equals(SpanHandlingCore.SpanBody(tag), body, StringComparison.Ordinal);
    }

    /// <summary>Whether the opening tag source (without '&lt;' and '&gt;') equals the given value.</summary>
    public static bool IsSource(this ParserTag? tag, string source)
    {
        return MemoryExtensions.Equals(SpanHandlingCore.SpanSource(tag), source, StringComparison.Ordinal);
    }
}
