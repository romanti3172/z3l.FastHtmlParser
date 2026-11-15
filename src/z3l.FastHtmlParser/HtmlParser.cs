using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
        if (!string.IsNullOrEmpty(pageUrl)) _pageUrl = pageUrl;

        if (!string.IsNullOrEmpty(pageSource))
        {
            _pageSource = pageSource;
            _data = ParsingCore.ParsePagesource(this, pageSource);
        }
        else
        {
            _data = [];
        }
    }

    private readonly List<ParserTag> _data;
    private string _pageSource = string.Empty;
    private string _pageUrl = string.Empty;

    /// <summary>The parsed source text.</summary>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    public string PageSource
    {
        get
        {
            ThrowIfDisposed();
            return _pageSource;
        }
    }

    /// <summary>URL of the page, used to resolve relative links.</summary>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    public string PageUrl
    {
        get
        {
            ThrowIfDisposed();
            return _pageUrl;
        }
    }

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

    // Every search, navigation and text method of the document and of its tags reads the tag list or the source
    // through these properties, so after Dispose all of them fail the same way.
    internal List<ParserTag> Data
    {
        get
        {
            ThrowIfDisposed();
            return _data;
        }
    }

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
        return Data.GetEnumerator();
    }

    // IDisposable

    private bool _disposed;

    /// <summary>
    /// Releases the tag list and the source text early. Disposing is optional: the document holds no unmanaged resources.
    /// Afterwards the document and the tags taken from it cannot be used: their properties and methods
    /// throw <see cref="ObjectDisposedException"/>.
    /// </summary>
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
            _data.Clear();
            _pageSource = string.Empty;
            _pageUrl = string.Empty;
        }
    }

    // On the path of every tag read: one inlined field test, the throw is kept out of line.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed) ThrowDisposed();
    }

    [DoesNotReturn]
    private void ThrowDisposed() => throw new ObjectDisposedException(GetType().FullName);
}
