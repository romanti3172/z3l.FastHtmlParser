using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        : this(pageSource.AsMemory(), pageSource, null, pageUrl)
    {
    }

    private HtmlParser(ReadOnlyMemory<char> source, string? text, IMemoryOwner<char>? owner, string? pageUrl)
    {
        if (!string.IsNullOrEmpty(pageUrl)) _pageUrl = pageUrl;

        _source = source;
        _text = text;
        _owner = owner;
        _data = source.IsEmpty ? TagList.Empty(this) : ParsingCore.ParsePagesource(this, source.Span);
    }

    /// <summary>
    /// Parses text that is already in memory (a <c>char[]</c>, a part of a larger buffer), without copying it into a string.
    /// </summary>
    /// <remarks>
    /// The document reads the text from <paramref name="source"/> for as long as it is used: the buffer must not be changed
    /// or returned to a pool until the document and its tags are no longer needed. To hand a pooled buffer over to the document,
    /// use <see cref="Parse(IMemoryOwner{char}, int, string?)"/>. <see cref="PageSource"/> makes a string of the text
    /// on its first use (no copy when <paramref name="source"/> is a whole string).
    /// </remarks>
    /// <param name="source">HTML or XML text.</param>
    /// <param name="pageUrl">URL of the page; relative links (<c>AttributeHref</c>, <c>AttributeSrc</c>) are resolved against it.</param>
    public static HtmlParser Parse(ReadOnlyMemory<char> source, string? pageUrl = default)
    {
        return new HtmlParser(source, null, null, pageUrl);
    }

    /// <summary>
    /// Parses the first <paramref name="length"/> chars of a buffer and takes ownership of it: <see cref="Dispose()"/>
    /// returns the buffer (disposes <paramref name="owner"/>). A rented buffer is usually longer than the text, hence the length.
    /// </summary>
    /// <example>
    /// <code>
    /// var owner = MemoryPool&lt;char&gt;.Shared.Rent(maxChars);
    /// int length = reader.Read(owner.Memory.Span);
    /// using var doc = HtmlParser.Parse(owner, length, url);
    /// </code>
    /// </example>
    /// <remarks>
    /// After a successful call the document owns the buffer: do not use or dispose it yourself. If the call throws,
    /// the buffer stays yours. Tags and results taken from the document must not be used after <see cref="Dispose()"/>:
    /// they throw <see cref="ObjectDisposedException"/>; strings already returned (<c>Text()</c>, attribute values) stay valid.
    /// </remarks>
    /// <param name="owner">The buffer holding the text.</param>
    /// <param name="length">Number of chars of text at the start of the buffer.</param>
    /// <param name="pageUrl">URL of the page; relative links (<c>AttributeHref</c>, <c>AttributeSrc</c>) are resolved against it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or longer than the buffer.</exception>
    public static HtmlParser Parse(IMemoryOwner<char> owner, int length, string? pageUrl = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var memory = owner.Memory;
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, memory.Length);

        return new HtmlParser(memory[..length], null, owner, pageUrl);
    }

    private readonly TagList _data;

    // Sibling links and positions for CSS selectors, built by the first selector that needs them.
    internal Core.Css.CssTree? CssTree;

    private ReadOnlyMemory<char> _source;
    private string? _text;               // the source as a string: given to the constructor, or made by PageSource
    private IMemoryOwner<char>? _owner;  // returned on Dispose
    private string _pageUrl = string.Empty;

    /// <summary>
    /// The parsed source text. For a document parsed from memory the string is made on the first use
    /// (the tags do not need it: they read the text in place).
    /// </summary>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    public string PageSource
    {
        get
        {
            ThrowIfDisposed();
            return _text ??= MakeText(_source); // a benign race: every thread gets an equal string
        }
    }

    private static string MakeText(ReadOnlyMemory<char> source)
    {
        // a whole string wrapped in memory: no copy
        if (MemoryMarshal.TryGetString(source, out var text, out int start, out int length) && start == 0 && length == text.Length)
        {
            return text;
        }
        return source.ToString();
    }

    /// <summary>The source text, read in place by every search, navigation and text method.</summary>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    internal ReadOnlySpan<char> Source
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfDisposed();

            // a string: AsSpan is cheaper than ReadOnlyMemory.Span, which checks the kind of its object
            var text = _text;
            return text != null ? text.AsSpan() : _source.Span;
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

    // What a plain relative link is put after to make it absolute: "https://example.com" for "/a/b" and
    // "https://example.com/dir/" for "a/b". Made once per document, by resolving two links in the usual way.
    private string? _pageSite;
    private string? _pageFolder;
    private bool _pagePrefixesResolved;

    /// <summary>
    /// The address of the site and of the folder of the page, for links that are a plain path (see
    /// <c>MarkdownConverter.ResolveUrl</c>); null when the page has no URL such links can be put after.
    /// </summary>
    internal void GetPagePrefixes(out string? site, out string? folder)
    {
        if (!_pagePrefixesResolved)
        {
            if (PageUri is { } pageUri)
            {
                _pageSite = Prefix("/x", pageUri);
                _pageFolder = Prefix("x", pageUri);
            }
            _pagePrefixesResolved = true; // a benign race: every thread makes the same strings
        }

        site = _pageSite;
        folder = _pageFolder;

        string? Prefix(string link, Uri pageUri)
        {
            string resolved = UriCore.CorrectLink(link, PageUrl, pageUri);
            return resolved.EndsWith(link, StringComparison.Ordinal) ? resolved.Substring(0, resolved.Length - link.Length) : null;
        }
    }

    // 0: not looked up yet, 1: no <pre> (nor listing, plaintext) in the page, 2: there is one. Most pages have none,
    // and then Text() does not look for one around each tag. A race only computes the same answer twice.
    private int _hasPre;

    /// <summary>Whether the page has a &lt;pre&gt;, &lt;listing&gt; or &lt;plaintext&gt;: their text keeps its whitespace.</summary>
    internal bool HasPre
    {
        get
        {
            if (_hasPre == 0)
            {
                var data = Data;
                bool found = false;
                for (int i = 0; i < data.Count && !found; i++)
                {
                    found = data.Node(i).Key is (int)HtmlTag.Pre or (int)HtmlTag.Listing or (int)HtmlTag.Plaintext;
                }
                _hasPre = found ? 2 : 1;
            }
            return _hasPre == 2;
        }
    }

    // Every search, navigation and text method of the document and of its tags reads the tag list or the source
    // through these properties, so after Dispose all of them fail the same way.
    internal TagList Data
    {
        get
        {
            ThrowIfDisposed();
            return _data;
        }
    }

    // ITagSearchable

    void ITagSearchable.GetScope(out TagList tags, out int first, out int end)
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
    /// Releases the tag list and the source text early. Disposing is optional: the document holds no unmanaged resources,
    /// except that a buffer handed over with <see cref="Parse(IMemoryOwner{char}, int, string?)"/> goes back to its pool only here.
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
            CssTree = null;
            _text = null;
            _source = default;
            _pageUrl = string.Empty;

            var owner = _owner;
            _owner = null;
            owner?.Dispose();
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
