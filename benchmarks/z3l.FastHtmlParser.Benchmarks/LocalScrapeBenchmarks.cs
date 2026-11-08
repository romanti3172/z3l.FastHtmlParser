using System.Text;
using AngleSharp.Dom;
using BenchmarkDotNet.Attributes;
using HtmlAgilityPack;

namespace z3l.FastHtmlParser.Benchmarks;

/// <summary>
/// A production scraping task on a real Amazon search results page: every result row → ASIN, currency, price,
/// name, rating, Prime. The page is third-party content and is not in the repository: save one as
/// benchmarks/local/amazon*.html to run this (with no such file there is nothing to run).
/// The setup checks that all three libraries return exactly the same records.
/// </summary>
[MemoryDiagnoser]
public class LocalScrapeBenchmarks
{
    public record Product(string Asin, string Currency, string Price, string Name, string Rating, bool Prime);

    private readonly AngleSharp.Html.Parser.HtmlParser _angleSharp = new();
    private string _html = "";

    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = "";

    public static IEnumerable<string> Pages => Corpus.LocalPages("amazon");

    [GlobalSetup]
    public void Setup()
    {
        _html = Corpus.Get(Page);

        var a = Amazon_FastHtmlParser();
        var b = Amazon_AngleSharp();
        var c = Amazon_HtmlAgilityPack();
        if (a.Count == 0 || !a.SequenceEqual(b) || !a.SequenceEqual(c))
        {
            throw new InvalidOperationException($"The libraries extracted different data:\n{string.Join("\n", a)}\n---\n{string.Join("\n", b)}\n---\n{string.Join("\n", c)}");
        }
    }

    [Benchmark(Baseline = true)]
    public List<Product> Amazon_FastHtmlParser()
    {
        var result = new List<Product>(64);
        foreach (var row in new HtmlParser(_html).FindAll(HtmlTag.Div, cls: "s-result-item"))
        {
            var asin = row.AttributeByName("data-asin");
            if (string.IsNullOrEmpty(asin)) continue;

            var price = row.Find(HtmlTag.Span, cls: "a-price");
            var star = row.Find(HtmlTag.I, cls: "a-icon-star-mini") ?? row.Find(HtmlTag.I, cls: "a-icon-star-small");

            result.Add(new Product(
                asin,
                price.Find(HtmlTag.Span, cls: "a-price-symbol").Text() ?? "",
                Price(price.Find(HtmlTag.Span, cls: "a-price-whole").Text(), price.Find(HtmlTag.Span, cls: "a-price-fraction").Text()),
                row.Find(HtmlTag.H2).Text() ?? "",
                Rating(star.Find(HtmlTag.Span, cls: "a-icon-alt").Text()),
                row.Find(new TagQuery(HtmlTag.I) { AttributeName = "aria-label", AttributeValue = "Amazon Prime" }) != null));
        }
        return result;
    }

    [Benchmark]
    public List<Product> Amazon_AngleSharp()
    {
        var result = new List<Product>(64);
        foreach (var row in _angleSharp.ParseDocument(_html).QuerySelectorAll("div.s-result-item"))
        {
            var asin = row.GetAttribute("data-asin");
            if (string.IsNullOrEmpty(asin)) continue;

            var price = row.QuerySelector("span.a-price");
            var star = row.QuerySelector("i.a-icon-star-mini") ?? row.QuerySelector("i.a-icon-star-small");

            result.Add(new Product(
                asin,
                Clean(price?.QuerySelector("span.a-price-symbol")?.TextContent),
                Price(Clean(price?.QuerySelector("span.a-price-whole")?.TextContent), Clean(price?.QuerySelector("span.a-price-fraction")?.TextContent)),
                Clean(row.QuerySelector("h2")?.TextContent),
                Rating(Clean(star?.QuerySelector("span.a-icon-alt")?.TextContent)),
                row.QuerySelector("i[aria-label='Amazon Prime']") != null));
        }
        return result;
    }

    [Benchmark]
    public List<Product> Amazon_HtmlAgilityPack()
    {
        var document = new HtmlDocument();
        document.LoadHtml(_html);

        var result = new List<Product>(64);
        foreach (var row in document.DocumentNode.SelectNodes("//div[contains(concat(' ', @class, ' '), ' s-result-item ')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var asin = row.GetAttributeValue("data-asin", "");
            if (string.IsNullOrEmpty(asin)) continue;

            var price = row.SelectSingleNode(".//span[contains(concat(' ', @class, ' '), ' a-price ')]");
            var star = row.SelectSingleNode(".//i[contains(concat(' ', @class, ' '), ' a-icon-star-mini ')]")
                ?? row.SelectSingleNode(".//i[contains(concat(' ', @class, ' '), ' a-icon-star-small ')]");

            result.Add(new Product(
                asin,
                Text(price?.SelectSingleNode(".//span[contains(concat(' ', @class, ' '), ' a-price-symbol ')]")),
                Price(Text(price?.SelectSingleNode(".//span[contains(concat(' ', @class, ' '), ' a-price-whole ')]")),
                      Text(price?.SelectSingleNode(".//span[contains(concat(' ', @class, ' '), ' a-price-fraction ')]"))),
                Text(row.SelectSingleNode(".//h2")),
                Rating(Text(star?.SelectSingleNode(".//span[contains(concat(' ', @class, ' '), ' a-icon-alt ')]"))),
                row.SelectSingleNode(".//i[@aria-label='Amazon Prime']") != null));
        }
        return result;

        static string Text(HtmlNode? node) => Clean(node == null ? null : HtmlEntity.DeEntitize(node.InnerText));
    }

    // "1,309." + "00" → "1309.00", as in the production parser
    private static string Price(string? whole, string? fraction)
    {
        if (string.IsNullOrEmpty(whole)) return "";
        var digits = new StringBuilder(whole.Length + 3);
        foreach (var c in whole) if (char.IsAsciiDigit(c)) digits.Append(c);
        return string.IsNullOrEmpty(fraction) ? digits.ToString() : digits.Append('.').Append(fraction).ToString();
    }

    // "4.2 out of 5 stars" → "4.2"
    private static string Rating(string? alt)
    {
        if (string.IsNullOrEmpty(alt)) return "";
        int space = alt.IndexOf(' ');
        return space < 0 ? alt : alt[..space];
    }

    // whitespace collapsed and trimmed, as Text() returns it
    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
