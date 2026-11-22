namespace z3l.FastHtmlParser;

/// <summary>
/// The main content of a page, as <c>Article()</c> found it: the text of the article without the menus, sidebars,
/// comments, ads and footers around it, and what the page says about it (title, author, date, site, image).
/// </summary>
/// <example>
/// <code>
/// var article = new HtmlParser(html, url).Article();
/// if (article != null)
/// {
///     Console.WriteLine(article.Title);
///     Console.WriteLine(article.Markdown);     // the article for a language model
/// }
/// </code>
/// </example>
public sealed class ReadableArticle
{
    private readonly string? _pageUrl;
    private string? _text;
    private string? _markdown;

    internal ReadableArticle(string content, string? pageUrl)
    {
        Content = content;
        _pageUrl = pageUrl;
    }

    /// <summary>
    /// The title: from the page's metadata (JSON-LD, Open Graph, Twitter, Dublin Core), otherwise its
    /// <c>&lt;title&gt;</c> without the site name, or its only <c>&lt;h1&gt;</c>. Null when the page has none.
    /// </summary>
    public string? Title { get; internal init; }

    /// <summary>The author: from the metadata, otherwise from a byline in the page ("By John Smith"). Null when not found.</summary>
    public string? Byline { get; internal init; }

    /// <summary>A short description: from the metadata, otherwise the first paragraph of the article.</summary>
    public string? Excerpt { get; internal init; }

    /// <summary>The name of the site (<c>og:site_name</c>, the publisher in JSON-LD). Null when not found.</summary>
    public string? SiteName { get; internal init; }

    /// <summary>
    /// The publication time as the page gives it (<c>datePublished</c>, <c>article:published_time</c>), usually
    /// ISO 8601: <c>DateTimeOffset.TryParse</c> reads it. Null when not found.
    /// </summary>
    public string? PublishedTime { get; internal init; }

    /// <summary>The main image (<c>og:image</c>, <c>twitter:image</c>, the image in JSON-LD), absolute when the page has a URL. Null when not found.</summary>
    public string? Image { get; internal init; }

    /// <summary>The language of the page (<c>&lt;html lang&gt;</c>). Null when not given.</summary>
    public string? Language { get; internal init; }

    /// <summary>
    /// The article as clean HTML: paragraphs, headings, lists, tables, links, images, quotes and code, without
    /// scripts, styles, classes, ids, forms and the parts that are not the article. Links and images are absolute
    /// when the page has a URL. It is safe to show: it is written by the sanitizer (<c>Sanitize()</c>).
    /// </summary>
    public string Content { get; }

    /// <summary>The text of the article, with its paragraphs and lines.</summary>
    public string TextContent => _text ??= HtmlText.ToTextWithLineBreaks(Content) ?? string.Empty;

    /// <summary>The article as Markdown: headings, lists, links, images, tables, code blocks and quotes.</summary>
    public string Markdown => _markdown ??= new HtmlParser(Content, _pageUrl).Markdown();

    /// <summary>The length of <see cref="TextContent"/>.</summary>
    public int Length => TextContent.Length;
}
