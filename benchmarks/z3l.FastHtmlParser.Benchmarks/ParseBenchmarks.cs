using BenchmarkDotNet.Attributes;
using HtmlAgilityPack;

namespace z3l.FastHtmlParser.Benchmarks;

/// <summary>Parsing a page into each library's queryable form.</summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    private readonly AngleSharp.Html.Parser.HtmlParser _angleSharp = new();
    private string _html = "";

    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = "";

    public static IEnumerable<string> Pages => Corpus.Names;

    [GlobalSetup]
    public void Setup() => _html = Corpus.Get(Page);

    [Benchmark(Baseline = true)]
    public object FastHtmlParser() => new HtmlParser(_html);

    [Benchmark]
    public object AngleSharp() => _angleSharp.ParseDocument(_html);

    [Benchmark]
    public object HtmlAgilityPack()
    {
        var document = new HtmlDocument();
        document.LoadHtml(_html);
        return document;
    }
}
