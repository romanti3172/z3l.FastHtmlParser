using z3l.FastHtmlParser.Core.Readability;

namespace z3l.FastHtmlParser;

/// <summary>
/// The main content of a page, by the method of Mozilla's Readability (the reader view of Firefox): the article
/// without the menus, sidebars, comments, ads and footers around it, with its title, author, date, site and image.
/// </summary>
/// <remarks>
/// The page is not changed: the parts that are not the article are marked in the tag list, its text is counted in one
/// pass, and the article is written as clean HTML by the sanitizer (see <see cref="ReadableArticle.Content"/>), from
/// which <see cref="ReadableArticle.Markdown"/> and <see cref="ReadableArticle.TextContent"/> are made.
/// Give the document the URL of the page (<c>new HtmlParser(html, url)</c>) to get absolute links and images.
/// </remarks>
/// <example>
/// <code>
/// var doc = new HtmlParser(html, "https://example.com/news/1");
/// if (doc.IsProbablyReadable() &amp;&amp; doc.Article() is { } article)
/// {
///     Console.WriteLine($"{article.Title} by {article.Byline}, {article.PublishedTime}");
///     File.WriteAllText("article.md", article.Markdown);
/// }
/// </code>
/// </example>
public static class ParserTagArticleExtensions
{
    /// <summary>
    /// The article of the page. Null for a null document and for a page in which no text was found.
    /// </summary>
    public static ReadableArticle? Article(this HtmlParser? document)
    {
        return document == null ? null : ArticleExtractor.Extract(document);
    }

    /// <summary>
    /// A quick test of whether the page has an article to take: enough paragraphs of text that are shown and are not
    /// in parts that look like sidebars or comments (Readability's isProbablyReaderable). Much faster than
    /// <see cref="Article"/>: use it to pass over lists, search results and other pages without an article.
    /// </summary>
    /// <param name="document">The page.</param>
    /// <param name="minContentLength">A paragraph counts when it has at least this many chars of text.</param>
    /// <param name="minScore">The page is readable when the paragraphs give more than this; each one gives the square root of its length less <paramref name="minContentLength"/>.</param>
    public static bool IsProbablyReadable(this HtmlParser? document, int minContentLength = 140, double minScore = 20)
    {
        return document != null && ArticleExtractor.IsProbablyReadable(document, minContentLength, minScore);
    }
}
