using System.Buffers;

namespace z3l.FastHtmlParser.Core;

internal static class UriCore
{
    internal static string CorrectLink(ReadOnlySpan<char> link, HtmlParser parser, string? currentUrl)
    {
        // The page's own URL is parsed once per document instead of once per link.
        return currentUrl == null
            ? CorrectLink(link, parser.PageUrl, parser.PageUri)
            : CorrectLink(link, currentUrl);
    }

    /// <summary>
    /// Makes <paramref name="link"/> absolute (relative to <paramref name="currentPage"/>)
    /// and unescapes "%3f" → '?', "%3d" → '=', "&amp;amp;" → '&amp;'.
    /// An empty link returns <paramref name="currentPage"/>, as a browser does.
    /// </summary>
    internal static string CorrectLink(ReadOnlySpan<char> link, string currentPage, Uri? currentPageUri = null)
    {
        if (link.IsEmpty)
        {
            return currentPage;
        }

        string? processedLink = null;

        if (!string.IsNullOrEmpty(currentPage))
        {
            string linkString = link.ToString();

            if (!Uri.TryCreate(linkString, UriKind.Absolute, out _) &&
                (currentPageUri ?? (Uri.TryCreate(currentPage, UriKind.Absolute, out var parsed) ? parsed : null)) is { } baseUri)
            {
                processedLink = Uri.TryCreate(baseUri, linkString, out var combinedUri)
                    ? combinedUri.ToString()
                    : linkString;
            }
            else
            {
                processedLink = linkString;
            }
        }

        ReadOnlySpan<char> linkSpan = processedLink != null ? processedLink.AsSpan() : link;

        // Nothing to unescape: no extra copy.
        if (linkSpan.IndexOfAny('%', '&') == -1)
        {
            return processedLink ?? link.ToString();
        }

        char[]? rentedArray = null;
        Span<char> destination = linkSpan.Length <= 512
            ? stackalloc char[linkSpan.Length]
            : (rentedArray = ArrayPool<char>.Shared.Rent(linkSpan.Length));

        try
        {
            int destIndex = 0;
            ReadOnlySpan<char> current = linkSpan;

            while (!current.IsEmpty)
            {
                int nextTokenIdx = current.IndexOfAny('%', '&');

                if (nextTokenIdx == -1)
                {
                    current.CopyTo(destination.Slice(destIndex));
                    destIndex += current.Length;
                    break;
                }

                if (nextTokenIdx > 0)
                {
                    current.Slice(0, nextTokenIdx).CopyTo(destination.Slice(destIndex));
                    destIndex += nextTokenIdx;
                    current = current.Slice(nextTokenIdx);
                }

                if (current.StartsWith("%3f", StringComparison.OrdinalIgnoreCase))
                {
                    destination[destIndex++] = '?';
                    current = current.Slice(3);
                }
                else if (current.StartsWith("%3d", StringComparison.OrdinalIgnoreCase))
                {
                    destination[destIndex++] = '=';
                    current = current.Slice(3);
                }
                else if (current.StartsWith("&amp;", StringComparison.OrdinalIgnoreCase))
                {
                    destination[destIndex++] = '&';
                    current = current.Slice(5);
                }
                else
                {
                    destination[destIndex++] = current[0];
                    current = current.Slice(1);
                }
            }

            return new string(destination.Slice(0, destIndex));
        }
        finally
        {
            if (rentedArray != null)
            {
                ArrayPool<char>.Shared.Return(rentedArray);
            }
        }
    }
}
