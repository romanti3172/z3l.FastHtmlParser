using System.Text;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// <c>Sanitize()</c>, <c>Article()</c> and <c>IsProbablyReadable()</c> on hostile markup: linear time, nothing throws.
/// Before 1.8.1 the sanitizer looked through all the open elements at every block (6 s for 100,000 nested divs, 1 MB),
/// and the article extractor read the text of every nested heading, byline or small block.
/// </summary>
[TestClass]
public sealed class ArticleAndSanitizerHardeningTests
{
    private const int Many = 100_000;

    private static string Repeat(string s, int count) => new StringBuilder(s.Length * count).Insert(0, s, count).ToString();

    private static readonly Dictionary<string, Func<string>> Hostile = new()
    {
        ["deep divs"] = () => Repeat("<div>", Many) + "text, with commas" + Repeat("</div>", Many),
        ["deep divs with line breaks"] = () => Repeat("<div>x<br>", Many / 4) + Repeat("</div>", Many / 4),
        ["deep headings"] = () => "<title>A title of the page here</title>" + Repeat("<h2>", Many) + "x" + Repeat("</h2>", Many),
        ["deep bylines"] = () => Repeat("<div class=\"author\">", Many) + "x" + Repeat("</div>", Many),
        ["deep articles"] = () => Repeat("<article>", Many / 4) + Repeat("text of an article, long enough to count. ", 10) + Repeat("</article>", Many / 4),
        ["deep spans"] = () => Repeat("<span>", Many) + "x",
        ["unclosed paragraphs"] = () => Repeat("<p>a", Many),
        ["unclosed list items"] = () => "<ul>" + Repeat("<li>a", Many),
        ["unclosed cells"] = () => Repeat("<table><tr><td>a", Many / 4),
        ["cells out of tables"] = () => Repeat("<p><td><div>a", Many / 4),
        ["many paragraphs"] = () => "<div>" + Repeat("<div class=\"x\"><p>Some text, with commas, long enough to count as a paragraph.</p></div>", Many / 4) + "</div>",
    };

    public static IEnumerable<object[]> HostileMarkup => Hostile.Keys.Select(name => new object[] { name });

    [TestMethod]
    [DynamicData(nameof(HostileMarkup))]
    public void HostileMarkup_IsReadInLinearTime(string name)
    {
        var doc = new HtmlParser("<html><body>" + Hostile[name]() + "</body></html>");

        // Milliseconds when linear, well under the limit on a slow CI runner; the test does not wait for a quadratic run.
        var running = Task.Run(() => (doc.Sanitize(), doc.Article(), doc.IsProbablyReadable()));
        if (!running.Wait(TimeSpan.FromSeconds(10))) Assert.Fail($"not done in 10 s: {name}");
        var (sanitized, _, _) = running.Result;
        Assert.AreEqual(sanitized, HtmlText.Sanitize(sanitized), name);
    }
}
