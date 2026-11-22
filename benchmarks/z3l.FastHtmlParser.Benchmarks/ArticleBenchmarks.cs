using BenchmarkDotNet.Attributes;

namespace z3l.FastHtmlParser.Benchmarks;

/// <summary>
/// The article of a page, by the method of Mozilla's Readability: from the HTML string to the article with its
/// metadata, clean HTML and text. SmartReader is the port of Readability to .NET, on AngleSharp.
/// </summary>
/// <remarks>
/// The pages are articles saved in benchmarks/local with names that start with "article-" (third-party content,
/// not in the repository): for example the test pages of Mozilla Readability (test/test-pages/*/source.html).
/// Without them the books page stands in, which has no article to speak of.
/// </remarks>
[MemoryDiagnoser]
public class ArticleBenchmarks
{
    private const string Url = "https://example.com/news/2026/11/article.html";
    private string _html = "";

    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = "";

    public static IEnumerable<string> Pages => Corpus.LocalPages("article-") is var local && local.Any() ? local : [Corpus.Books];

    [GlobalSetup]
    public void Setup()
    {
        _html = Corpus.Get(Page);

        // both have to find the article: an empty result is not a faster one
        int ours = FastHtmlParser().Length;
        int theirs = SmartReader().Length;
        if (ours == 0 || theirs == 0 || ours < theirs / 3 || theirs < ours / 3)
        {
            throw new InvalidOperationException($"{Page}: {ours} chars of text here, {theirs} by SmartReader");
        }
    }

    [Benchmark(Baseline = true)]
    public string FastHtmlParser()
    {
        var article = new HtmlParser(_html, Url).Article();
        return article == null ? "" : article.Title + article.TextContent;
    }

    [Benchmark]
    public string SmartReader()
    {
        // the constructor with the text: ParseArticle(url, text) would take the text for a user agent and download the page
        var article = new global::SmartReader.Reader(Url, _html).GetArticle();
        return article.IsReadable ? article.Title + article.TextContent : "";
    }
}
