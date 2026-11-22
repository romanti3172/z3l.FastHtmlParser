namespace z3l.FastHtmlParser;

/// <summary>
/// The main content of a page by the method of Mozilla's Readability (the reader view of Firefox): the article without
/// the menus, sidebars, comments, ads and footers around it, with its title, author, date, excerpt, site and image.
/// </summary>
/// <remarks>
/// The extractor is a part of z3l.FastHtmlParser, which comes with this package; this is its entry point for a string
/// of HTML. With the parser at hand: <c>new HtmlParser(html, url).Article()</c>.
/// </remarks>
/// <example>
/// <code>
/// var article = Readability.Parse(html, "https://example.com/news/1");
/// if (article != null)
/// {
///     Console.WriteLine($"{article.Title} by {article.Byline}, {article.PublishedTime}");
///     string markdown = article.Markdown;    // for a language model
///     string clean = article.Content;        // clean HTML, safe to show
/// }
/// </code>
/// </example>
public static class Readability
{
    /// <summary>
    /// The article of the page. Null for null or empty HTML and for a page in which no text was found.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <param name="url">The address of the page: with it the links and images of the article are absolute.</param>
    public static ReadableArticle? Parse(string? html, string? url = null)
    {
        return string.IsNullOrEmpty(html) ? null : new HtmlParser(html, url).Article();
    }

    /// <summary>
    /// A quick test of whether the page has an article to take: enough paragraphs of shown text outside the parts
    /// that look like sidebars or comments. Much faster than <see cref="Parse"/>: use it to pass over lists,
    /// search results and other pages without an article.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <param name="minContentLength">A paragraph counts when it has at least this many chars of text.</param>
    /// <param name="minScore">The page is readable when the paragraphs give more than this; each one gives the square root of its length less <paramref name="minContentLength"/>.</param>
    public static bool IsProbablyReadable(string? html, int minContentLength = 140, double minScore = 20)
    {
        return !string.IsNullOrEmpty(html) && new HtmlParser(html).IsProbablyReadable(minContentLength, minScore);
    }
}
