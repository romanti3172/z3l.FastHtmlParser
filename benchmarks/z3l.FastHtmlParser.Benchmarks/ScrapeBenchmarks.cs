// HtmlAgilityPack returns null for a missing node; the setup verifies that every node is found.
#pragma warning disable CS8602, CS8604

using AngleSharp.Dom;
using BenchmarkDotNet.Attributes;
using HtmlAgilityPack;
using z3l.FastHtmlParser.Shortcuts;

namespace z3l.FastHtmlParser.Benchmarks;

/// <summary>
/// A real scraping task end to end: parse the page and extract the records.
/// The setup checks that all three libraries return exactly the same records.
/// </summary>
[MemoryDiagnoser]
public class ScrapeBenchmarks
{
    public record Book(string Title, string Price, string Rating);
    public record Quote(string Text, string Author, string Tags);

    private readonly AngleSharp.Html.Parser.HtmlParser _angleSharp = new();
    private string _books = "";
    private string _quotes = "";

    [GlobalSetup]
    public void Setup()
    {
        _books = Corpus.Get(Corpus.Books);
        _quotes = Corpus.Get(Corpus.Quotes);

        Same(Books_FastHtmlParser(), Books_AngleSharp(), Books_HtmlAgilityPack());
        Same(Quotes_FastHtmlParser(), Quotes_AngleSharp(), Quotes_HtmlAgilityPack());
    }

    private static void Same<T>(List<T> a, List<T> b, List<T> c)
    {
        if (a.Count == 0 || !a.SequenceEqual(b) || !a.SequenceEqual(c))
        {
            throw new InvalidOperationException($"The libraries extracted different data:\n{string.Join("\n", a)}\n---\n{string.Join("\n", b)}\n---\n{string.Join("\n", c)}");
        }
    }

    // books.toscrape.com: 20 books → title, price, rating

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Books")]
    public List<Book> Books_FastHtmlParser()
    {
        var result = new List<Book>(20);
        foreach (var pod in new HtmlParser(_books).FindAllByNameClass(HtmlTag.Article, "product_pod"))
        {
            result.Add(new Book(
                pod.FindByName(HtmlTag.H3).FindByName(HtmlTag.A).AttributeTitle(),
                pod.FindByNameClass(HtmlTag.P, "price_color").Text()!,
                LastWord(pod.FindByNameClass(HtmlTag.P, "star-rating").AttributeClass())));
        }
        return result;
    }

    [Benchmark]
    [BenchmarkCategory("Books")]
    public List<Book> Books_AngleSharp()
    {
        var result = new List<Book>(20);
        foreach (var pod in _angleSharp.ParseDocument(_books).QuerySelectorAll("article.product_pod"))
        {
            result.Add(new Book(
                pod.QuerySelector("h3 a")!.GetAttribute("title")!,
                pod.QuerySelector("p.price_color")!.TextContent,
                LastWord(pod.QuerySelector("p.star-rating")!.GetAttribute("class")!)));
        }
        return result;
    }

    [Benchmark]
    [BenchmarkCategory("Books")]
    public List<Book> Books_HtmlAgilityPack()
    {
        var document = new HtmlDocument();
        document.LoadHtml(_books);

        var result = new List<Book>(20);
        foreach (var pod in document.DocumentNode.SelectNodes("//article[contains(concat(' ', @class, ' '), ' product_pod ')]"))
        {
            result.Add(new Book(
                HtmlEntity.DeEntitize(pod.SelectSingleNode(".//h3/a").GetAttributeValue("title", "")),
                HtmlEntity.DeEntitize(pod.SelectSingleNode(".//p[contains(concat(' ', @class, ' '), ' price_color ')]").InnerText),
                LastWord(pod.SelectSingleNode(".//p[contains(concat(' ', @class, ' '), ' star-rating ')]").GetAttributeValue("class", ""))));
        }
        return result;
    }

    // quotes.toscrape.com: 10 quotes → text, author, tags

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Quotes")]
    public List<Quote> Quotes_FastHtmlParser()
    {
        var result = new List<Quote>(10);
        foreach (var quote in new HtmlParser(_quotes).FindAllByNameClass(HtmlTag.Div, "quote"))
        {
            result.Add(new Quote(
                quote.FindByNameClass(HtmlTag.Span, "text").Text()!,
                quote.FindByNameClass(HtmlTag.Small, "author").Text()!,
                string.Join(",", quote.FindAllByNameClass(HtmlTag.A, "tag").Select(a => a.Text()))));
        }
        return result;
    }

    [Benchmark]
    [BenchmarkCategory("Quotes")]
    public List<Quote> Quotes_AngleSharp()
    {
        var result = new List<Quote>(10);
        foreach (var quote in _angleSharp.ParseDocument(_quotes).QuerySelectorAll("div.quote"))
        {
            result.Add(new Quote(
                quote.QuerySelector("span.text")!.TextContent,
                quote.QuerySelector("small.author")!.TextContent,
                string.Join(",", quote.QuerySelectorAll("a.tag").Select(a => a.TextContent))));
        }
        return result;
    }

    [Benchmark]
    [BenchmarkCategory("Quotes")]
    public List<Quote> Quotes_HtmlAgilityPack()
    {
        var document = new HtmlDocument();
        document.LoadHtml(_quotes);

        var result = new List<Quote>(10);
        foreach (var quote in document.DocumentNode.SelectNodes("//div[contains(concat(' ', @class, ' '), ' quote ')]"))
        {
            result.Add(new Quote(
                HtmlEntity.DeEntitize(quote.SelectSingleNode(".//span[contains(concat(' ', @class, ' '), ' text ')]").InnerText),
                HtmlEntity.DeEntitize(quote.SelectSingleNode(".//small[contains(concat(' ', @class, ' '), ' author ')]").InnerText),
                string.Join(",", quote.SelectNodes(".//a[contains(concat(' ', @class, ' '), ' tag ')]").Select(a => HtmlEntity.DeEntitize(a.InnerText)))));
        }
        return result;
    }

    private static string LastWord(string value) => value[(value.LastIndexOf(' ') + 1)..];
}
