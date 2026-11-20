using BenchmarkDotNet.Attributes;

namespace z3l.FastHtmlParser.Benchmarks;

/// <summary>
/// A page as Markdown: from the HTML string to the Markdown string, parsing included.
/// ReverseMarkdown and Html2Markdown build an HtmlAgilityPack document first; html-to-markdown (Kreuzberg)
/// is a Rust library called through its .NET binding.
/// </summary>
[MemoryDiagnoser]
public class MarkdownBenchmarks
{
    private readonly ReverseMarkdown.Converter _reverseMarkdown = new(new ReverseMarkdown.Config
    {
        GithubFlavored = true,
        Formatting = { RemoveComments = true },
        Tags = { Unknown = global::ReverseMarkdown.Config.UnknownTagsOption.Bypass }, // the content of tags Markdown has nothing for
    });

    private readonly Html2Markdown.Converter _html2Markdown = new();
    private string _html = "";

    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = "";

    public static IEnumerable<string> Pages => [Corpus.Quotes, Corpus.Laptops, Corpus.Books, Corpus.BooksX10, .. Corpus.LocalPages("")];

    [GlobalSetup]
    public void Setup()
    {
        _html = Corpus.Get(Page);

        // every library has to give the text of the page: an empty or a cut result is not a faster one
        string expected = FastHtmlParser();
        foreach (var (name, markdown) in new[] { ("ReverseMarkdown", ReverseMarkdown()), ("Html2Markdown", Html2Markdown()), ("html-to-markdown", HtmlToMarkdownRust()) })
        {
            if (markdown.Length < expected.Length / 3)
            {
                throw new InvalidOperationException($"{name}: {markdown.Length} chars of Markdown for a page that gives {expected.Length}");
            }
        }
    }

    [Benchmark(Baseline = true)]
    public string FastHtmlParser() => new HtmlParser(_html).Markdown()!;

    [Benchmark]
    public string ReverseMarkdown() => _reverseMarkdown.Convert(_html);

    [Benchmark]
    public string Html2Markdown() => _html2Markdown.Convert(_html);

    [Benchmark(Description = "html-to-markdown (Rust)")]
    public string HtmlToMarkdownRust() => HtmlToMarkdown.HtmlToMarkdownConverter.Convert(_html, null).Content ?? "";
}
