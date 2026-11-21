using System.Text;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// <c>Markdown()</c> on hostile, broken and random markup: it takes linear time, the result stays in proportion
/// to the page, nothing throws.
/// </summary>
[TestClass]
public sealed class MarkdownHardeningTests
{
    private const int Many = 100_000;

    private static string Repeat(string s, int count) => new StringBuilder(s.Length * count).Insert(0, s, count).ToString();

    /// <summary>Markup on which a naive conversion takes quadratic time, recursion as deep as the page, or megabytes of padding.</summary>
    public static IEnumerable<object[]> HostileMarkup =>
    [
        ["deep quotes", Repeat("<blockquote>", Many) + "x" + Repeat("</blockquote>", Many)],
        ["deep lists", Repeat("<ul><li>", Many) + "x"],
        ["deep lists with text", Repeat("<ol start=999999999><li>a", Many)],
        ["deep definitions", Repeat("<dl><dd>a", Many)],
        ["deep bold", Repeat("<b>", Many) + "x" + Repeat("</b>", Many)],
        ["deep divs", Repeat("<div>", Many) + "x" + Repeat("</div>", Many)],
        ["headings in headings", Repeat("<h1>", Many) + "x"],
        ["pairs over many blocks", "<b><i><del><q><sub><sup><a href=x>" + Repeat("<p>a</p>", Many)],
        ["unclosed links", Repeat("<a href=x>y", Many)],
        ["line breaks", Repeat("<br>", Many) + "x" + Repeat("<p><br>a<br></p>", Many)],
        ["stray end tags", Repeat("</div></p></b>", Many)],
        ["special chars", Repeat("&amp;*_[]`<~|\\#-+>1.", Many)],
        ["special chars at block starts", Repeat("<p># - + > 1. ===</p><li>---</li>", Many)],
        ["code spans", Repeat("<code>`</code><code>a|b</code>", Many)],
        ["long runs of backticks", "<code>" + Repeat("`", Many) + "</code><pre>" + Repeat("`", Many) + "\n" + Repeat("`\n", Many) + "</pre>"],
        ["pre in pre", Repeat("<pre>", Many) + "x"],
        ["a table per cell", Repeat("<table><tr><td>", Many / 4) + "x"],
        ["many rows", "<table>" + Repeat("<tr><td>a<td>b", Many) + "</table>"],
        ["wide cells", Repeat("<table><tr><td colspan=1000>a<td>b</table>", Many / 10)],
        ["wide and tall cells", "<table><tr>" + Repeat("<td rowspan=65534 colspan=1000>a", 50) + Repeat("<tr><td>c", Many) + "</table>"],
        ["a tall cell in every row", "<table>" + Repeat("<tr><td rowspan=65534>a<td>b", Many / 10) + "</table>"],
        ["rows of growing width", "<table>" + string.Concat(Enumerable.Range(1, 400).Select(n => "<tr>" + Repeat("<td>a", n))) + "</table>"],
        ["lists in cells", "<table><tr><td>a<td>" + Repeat("<ul><li>x", Many / 10) + "</table>"],
        ["captions", "<table>" + Repeat("<caption>c</caption>", Many) + "<tr><td>a<td>b</table>"],
        ["images", Repeat("<img src='a b(c' alt='[x]' title='\"t\"'>", Many)],
        ["long attribute values", "<a href=\"" + Repeat("a(", Many) + "\" title=\"" + Repeat("\"\\", Many) + "\">x</a>"],
    ];

    [TestMethod]
    [DynamicData(nameof(HostileMarkup))]
    public void HostileMarkup_IsConvertedInLinearTime(string name, string html)
    {
        var doc = new HtmlParser(html);

        // Milliseconds when linear, under a second on a slow CI runner; the test does not wait for a quadratic run
        // to finish. Both conversions are timed: a quadratic table scan once took 7 s here and hung the CI runner.
        var converting = Task.Run(() => (doc.Markdown()!, doc.Markdown()!));
        if (!converting.Wait(TimeSpan.FromSeconds(5))) Assert.Fail($"not converted twice in 5 s: {name}");
        var (markdown, again) = converting.Result;

        // What a page can grow by most is the prefix of nested quotes and lists, at most 64 chars before every line,
        // also before the blank line between two paragraphs: some 130 chars for "<p>x".
        Assert.IsLessThanOrEqualTo(40L * html.Length + 4096, (long)markdown.Length, $"{name}: {html.Length} chars of HTML gave {markdown.Length} of Markdown");
        Assert.AreEqual(markdown, again, name);
    }

    [TestMethod]
    public void DeepNesting_IsIndentedUpToALimit()
    {
        // 40 lists in each other: the first 32 are indented by two chars each, the rest stay at that depth
        string deep = HtmlText.ToMarkdown(Repeat("<ul><li>level", 40))!;
        string[] lines = deep.Split('\n');
        Assert.HasCount(40, lines);
        Assert.AreEqual("- level", lines[0]);
        Assert.AreEqual(new string(' ', 62) + "- level", lines[31]);
        Assert.AreEqual(new string(' ', 64) + "level", lines[32]);
        Assert.AreEqual(new string(' ', 64) + "level", lines[39]);

        // lists numbered from 999,999,999: eleven chars each, so five of them fit
        string wide = HtmlText.ToMarkdown(Repeat("<ol start=999999999><li>n", 8) + Repeat("<br>line", 3))!;
        Assert.IsTrue(wide.Split('\n').All(line => line.Length <= 64 + 16), wide);
        StringAssert.Contains(wide, new string(' ', 44) + "999999999. n");
    }

    [TestMethod]
    public void HostileMarkup_KeepsItsText()
    {
        // what is too deep to be indented or too wide to be a table is still there
        StringAssert.Contains(HtmlText.ToMarkdown(Repeat("<blockquote><ul><li>", 5000) + "the text"), "the text");
        StringAssert.Contains(HtmlText.ToMarkdown(Repeat("<table><tr><td colspan=1000>a<td>b</table>", 5000) + "the text"), "the text");

        string wide = HtmlText.ToMarkdown("<table><tr>" + Repeat("<td rowspan=65534 colspan=1000>a", 50) + Repeat("<tr><td>cell", 1000) + "</table>")!;
        Assert.AreEqual(1000, wide.Split("cell").Length - 1);
    }

    [TestMethod]
    public void Page_CutAnywhere_IsConverted()
    {
        // a page that was not loaded to its end, cut inside a tag, an attribute, an entity, a table
        foreach (var (path, _) in Fixtures.All)
        {
            string html = Fixtures.Read(path);
            var random = new Random(html.Length);

            for (int n = 0; n < 60; n++)
            {
                string cut = html.Substring(0, random.Next(html.Length));
                string markdown = HtmlText.ToMarkdown(cut) ?? "";
                Assert.IsFalse(markdown.StartsWith('\n') || markdown.EndsWith('\n') || markdown.EndsWith(' '), $"{path} cut at {cut.Length}");
            }
        }
    }

    private static readonly string[] Pieces =
    [
        "<div>", "</div>", "<p>", "</p>", "<h1>", "</h1>", "<h3>", "</h3>", "<blockquote>", "</blockquote>",
        "<ul>", "</ul>", "<ol>", "</ol>", "<li>", "</li>", "<dl>", "<dt>", "<dd>", "</dl>", "<pre>", "</pre>", "<hr>", "<br>",
        "<b>", "</b>", "<i>", "</i>", "<del>", "</del>", "<a href=\"/u\">", "<a href='x y(z'>", "</a>", "<code>", "</code>",
        "<span>", "</span>", "<q>", "</q>", "<sub>", "</sub>", "<img src=\"i.png\" alt=\"a]b\">", "<input type=checkbox>",
        "<table>", "</table>", "<tr>", "</tr>", "<td>", "</td>", "<th>", "<td colspan=3 rowspan=2>", "<caption>", "<thead>", "</thead>",
        "<script>", "</script>", "<!-- c -->", "<svg>", "</svg>", "<x-y>", "</x-y>", "<", ">", "</", "<a href=",
        "text", " ", "\n", "word ", "1. ", "- ", "# ", "> ", "*", "_", "`", "[x]", "(y)", "|", "~~", "\\", "&amp;", "&lt;b&gt;", "&#42;", "&nbsp;", "---", "é", "日本", "😀",
    ];

    [TestMethod]
    public void RandomMarkup_IsConverted()
    {
        var random = new Random(20261120);
        var html = new StringBuilder();

        for (int n = 0; n < 4000; n++)
        {
            html.Clear();
            int length = random.Next(1, 80);
            for (int i = 0; i < length; i++) html.Append(Pieces[random.Next(Pieces.Length)]);

            string source = html.ToString();
            var doc = new HtmlParser(source);
            string markdown;
            try
            {
                markdown = doc.Markdown()!;
            }
            catch (Exception e)
            {
                Assert.Fail($"{e.GetType().Name} on: {source}\n{e}");
                return;
            }

            // no line break at the ends, no line that ends with a space other than the two of a line break,
            // no control chars but the line breaks
            Assert.IsFalse(markdown.StartsWith('\n') || markdown.EndsWith('\n') || markdown.EndsWith(' '), source);
            foreach (string line in markdown.Split('\n'))
            {
                if (line.EndsWith(' ') && !line.EndsWith("  ") && !InCode(markdown)) Assert.Fail($"a line ends with a space: [{line}]\n{source}");
                if (line.EndsWith("   ") && !InCode(markdown)) Assert.Fail($"a line ends with three spaces: [{line}]\n{source}");
            }

            // every tag gives the same Markdown again (nothing is left over from one conversion to the next)
            foreach (var tag in doc.Take(5)) Assert.AreEqual(tag.Markdown(), tag.Markdown(), source);
        }
    }

    // the lines of a code block are as they are written in the page
    private static bool InCode(string markdown) => markdown.Contains("```");
}
