using System.Collections;
using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>
/// A parsed HTML (or XML) document: a flat list of tags in document order.
/// Parsing happens once in the constructor; afterwards the document is read-only
/// and can be queried from several threads at the same time.
/// </summary>
/// <remarks>
/// Search and navigation methods are extension methods (<c>FindByName</c>, <c>FindAllByNameClass</c>,
/// <c>Text</c>, <c>AttributeHref</c>, ...). All of them accept <see langword="null"/> and return
/// <see langword="null"/> / empty results, so call chains never throw:
/// <code>
/// var price = doc.FindByNameClass(HtmlTag.Div, "card").FindByNameClass(HtmlTag.Span, "price").Text();
/// </code>
/// </remarks>
public class HtmlParser : IEnumerable<ParserTag>, ITagSearchable, IDisposable
{
    /// <summary>Parses <paramref name="pageSource"/>.</summary>
    /// <param name="pageSource">HTML or XML text.</param>
    /// <param name="pageUrl">URL of the page; relative links (<c>AttributeHref</c>, <c>AttributeSrc</c>) are resolved against it.</param>
    public HtmlParser(string? pageSource, string? pageUrl = default)
    {
        if (!string.IsNullOrEmpty(pageUrl)) PageUrl = pageUrl;

        if (!string.IsNullOrEmpty(pageSource))
        {
            PageSource = pageSource;
            Data = ParsingCore.ParsePagesource(this, PageSource);
        }
        else
        {
            Data = [];
        }
    }

    /// <summary>The parsed source text.</summary>
    public string PageSource { get; private set; } = string.Empty;

    /// <summary>URL of the page, used to resolve relative links.</summary>
    public string PageUrl { get; private set; } = string.Empty;

    // PageUrl parsed once, for resolving relative links (AttributeHref/AttributeSrc).
    private Uri? _pageUri;
    private bool _pageUriResolved;

    internal Uri? PageUri
    {
        get
        {
            if (!_pageUriResolved)
            {
                Uri.TryCreate(PageUrl, UriKind.Absolute, out _pageUri);
                _pageUriResolved = true;
            }
            return _pageUri;
        }
    }

    internal List<ParserTag> Data { get; }

    // ITagSearchable

    void ITagSearchable.GetScope(out List<ParserTag> tags, out int first, out int end)
    {
        tags = Data;
        first = 0;
        end = int.MaxValue; // the whole document
    }

    // IEnumerable

    /// <summary>Enumerates all tags of the document in document order.</summary>
    public IEnumerator<ParserTag> GetEnumerator()
    {
        return Data.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        ThrowIfDisposed();
        return Data.GetEnumerator();
    }

    // IDisposable

    private bool _disposed;

    /// <summary>Releases the tag list and the source text early.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the tag list and the source text.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        _disposed = true;

        if (disposing)
        {
            Data.Clear();
            PageSource = string.Empty;
            PageUrl = string.Empty;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
