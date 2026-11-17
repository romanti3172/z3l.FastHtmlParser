using System.Collections;
using System.Text;
using z3l.FastHtmlParser.Core;
using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// Hostile and random markup: parsing and navigation stay linear, the tag list stays consistent,
/// nothing throws; a disposed document fails in one way.
/// </summary>
[TestClass]
public sealed class HardeningTests
{
    private const int Many = 200_000;

    private static string Repeat(string s, int count) => new StringBuilder(s.Length * count).Insert(0, s, count).ToString();

    /// <summary>Markup on which the time used to grow with the square of the size (seconds to minutes at this size).</summary>
    public static IEnumerable<object[]> QuadraticMarkup =>
    [
        // name, markup, tags expected
        ["stray end tags after unclosed tags", "<div>" + Repeat("<span>", Many) + Repeat("</b>", Many) + "</div>", Many + 1],
        ["stray end tags with custom names", Repeat("<x-a>", Many) + Repeat("</x-b>", Many), Many],
        ["tags no rule ends", Repeat("<caption>", Many), Many],
        ["a rule looking for its scope", "<ul>" + Repeat("<span>", Many) + Repeat("<li>", Many), 2 * Many + 1],
        ["nested parts scanned again", Repeat("<div><li><span><li>", Many / 4) + Repeat("</div>", Many / 4), Many],
        ["self-closed tags before end tags", Repeat("<i><path/>", Many / 2) + Repeat("</i>", Many / 2), Many],
        ["deep nesting", Repeat("<div>", Many) + Repeat("</div>", Many), Many],
    ];

    [TestMethod]
    [DynamicData(nameof(QuadraticMarkup))]
    public void HostileMarkup_IsParsedInLinearTime(string name, string html, int tags)
    {
        // milliseconds when linear; the test does not wait for a quadratic run to finish
        var parsing = Task.Run(() => new HtmlParser(html));
        if (!parsing.Wait(TimeSpan.FromSeconds(20))) Assert.Fail($"not parsed in 20 s: {name}");
        var doc = parsing.Result;

        Assert.AreEqual(tags, doc.Count(), name);
        AssertConsistent(doc, html, name);
    }

    [TestMethod]
    public void DeepNesting_IsFlattened()
    {
        var doc = new HtmlParser(Repeat("<div>", 2000) + "<p>deep</p>" + Repeat("</div>", 2000) + "<b>after</b>");

        Assert.AreEqual(512, doc.Max(tag => tag.Depth));     // as in browsers
        Assert.AreEqual("deep", doc.Find(HtmlTag.P).Text());
        Assert.AreEqual("deep", doc.First().Text());         // every <div> has its end tag
        Assert.AreEqual(0, doc.Find(HtmlTag.B)!.Depth);
        Assert.AreEqual("after", doc.Find(HtmlTag.B).Text());
    }

    [TestMethod]
    public void Parent_DoesNotWalkOverThePreviousSiblings()
    {
        var doc = new HtmlParser("<ul>" + Repeat("<li><a href=\"/p\">x</a></li>", Many) + "</ul>");
        var ul = doc.Find(HtmlTag.Ul);

        // every <li> asks for its parent: seconds to minutes when each call walks back to the <ul>
        var counting = Task.Run(() => doc.FindAll(HtmlTag.Li).Count(li => ReferenceEquals(li.Parent(), ul)));
        if (!counting.Wait(TimeSpan.FromSeconds(20))) Assert.Fail("Parent() of every list item took more than 20 s");

        Assert.AreEqual(Many, counting.Result);
        Assert.AreSame(ul, doc.Last().Parent(HtmlTag.Ul));
    }

    [TestMethod]
    public void Fixtures_AreConsistent()
    {
        foreach (var (path, _) in Fixtures.All)
        {
            AssertConsistent(Fixtures.Load(path), Fixtures.Read(path), path);
        }
    }

    [TestMethod]
    public void RandomMarkup_StaysConsistent_AndNothingThrows()
    {
        string[] atoms =
        [
            "<", ">", "/", "=", "\"", "'", " ", "!", "-", "--", "<!--", "-->", "a", "b", "p", "li", "td", "tr", "table", "script", "style",
            "title", "br", "div", "</", "/>", "&", "&amp;", "&#", "&#x", ";", "x", "\n", "?", "<?xml", "id", "class", "href", "textarea",
            "caption", "option", "select", "ul", "<a", "<b", "<p", "<li", "<div", "</a>", "</b>", "</p>", "</div>", "<span/>", "</span>",
        ];
        var random = new Random(20261115);
        var html = new StringBuilder();

        for (int run = 0; run < 100_000; run++)
        {
            html.Clear();
            for (int i = random.Next(1, 40); i > 0; i--) html.Append(atoms[random.Next(atoms.Length)]);
            var source = html.ToString();

            try
            {
                var doc = new HtmlParser(source, "https://example.com/a/b");
                AssertConsistent(doc, source, source);

                foreach (var tag in doc)
                {
                    _ = tag.Text();
                    _ = tag.TextLineBreaks();
                    _ = tag.Body();
                    _ = tag.Source();
                    _ = tag.FullSource();
                    _ = tag.AttributeByName("id");
                    _ = tag.AttributeHref();
                    _ = tag.AttributeHrefMailTo();
                    _ = tag.IsClass("a");
                    _ = tag.IsText("a");
                    _ = tag.Children().Count();
                    _ = tag.NextSibling();
                    _ = tag.PreviousSibling();
                    _ = tag.Count();
                    _ = tag.TextAfterTagUntilNextOne();
                    _ = tag.TextAfterUntil("a");
                    _ = tag.Find("a");
                    _ = tag.XPath(".//a[@id]");
                }

                _ = doc.XPathValues("//a/@href");
                _ = HtmlText.ToText(source);
                _ = HtmlText.ToTextWithLineBreaks(source);
            }
            catch (Exception e) when (e is not AssertFailedException)
            {
                Assert.Fail($"{e.GetType().Name}: {e.Message} for: {source.Replace("\n", "\\n")}");
            }
        }
    }

    [TestMethod]
    public void AttributeLookup_AgreesWithReadingTheAttributesOneByOne()
    {
        // The lookup searches for the name and usually reads the value in place; the scanner reads every attribute
        // from the start of the tag (as XPath does). On any tag both must give the same value.
        string[] atoms =
        [
            " ", " ", "  ", "\n", "=", "\"", "'", "/", "id", "class", "ID", "data-id", "idx", "a", "x", "5", "=5", "id=1", "id = 2",
            "class=\"a b\"", "class='c'", "title=\"see id=7\"", "title='class=\"q\"'", "alt=\"\"", "x= id", "x=\"", "y='", "href=/p?id=3",
        ];
        string[] names = ["id", "class", "title", "x"];
        var random = new Random(20261115);
        var html = new StringBuilder();

        for (int run = 0; run < 200_000; run++)
        {
            html.Clear().Append("<div");
            for (int i = random.Next(1, 12); i > 0; i--) html.Append(atoms[random.Next(atoms.Length)]);
            html.Append(">text</div>");
            var source = html.ToString();

            foreach (var tag in new HtmlParser(source))
            {
                if (tag.IsName(HtmlTag.Comment)) continue;

                foreach (var name in names)
                {
                    var expected = ReadOneByOne(tag, name);
                    var actual = SpanHandlingCore.SpanAttributeByName(tag, name).ToString();
                    if (expected != actual) Assert.Fail($"{name}: '{actual}' instead of '{expected}' in: {source.Replace("\n", "\\n")}");
                }
            }
        }
    }

    private static string ReadOneByOne(ParserTag tag, string name)
    {
        var source = tag.Document.PageSource.AsSpan();
        var scanner = new AttributeScanner(tag);
        while (scanner.Next(out var nameStart, out var nameLength, out var valueStart, out var valueLength))
        {
            if (source.Slice(nameStart, nameLength).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return valueStart < 0 ? "" : source.Slice(valueStart, valueLength).ToString();
            }
        }
        return "";
    }

    public static IEnumerable<object[]> NestedForXPath =>
    [
        ["unclosed nesting", Repeat("<div><span>", Many / 2) + "<a href=\"/x\">link</a>"],
        ["many links in deep nesting", Repeat("<span>", 1000) + Repeat("<a href=\"/x\">x</a>", Many)],
        ["nested lists", Repeat("<div><ul><li><a href=\"/x\">x</a>", Many / 4)],
    ];

    [TestMethod]
    [DynamicData(nameof(NestedForXPath))]
    public void XPath_OnNestedNodes_IsLinear(string name, string html)
    {
        var doc = new HtmlParser(html);
        var links = doc.FindAll(HtmlTag.A).ToList();

        // descendant and ancestor steps on nodes inside one another: minutes when every input node
        // collects all its descendants or ancestors again
        var querying = Task.Run(() => (
            Descendants: doc.XPath("//*//*//a").Count,
            InDiv: doc.XPath("//div//a[@href]").Count,
            Ancestors: doc.XPathValue("count(//a/ancestor::*)"),
            Self: doc.XPath("//a/ancestor-or-self::*[@href]").Count));
        if (!querying.Wait(TimeSpan.FromSeconds(20))) Assert.Fail($"XPath on {name} took more than 20 s");
        var result = querying.Result;

        var ancestors = new HashSet<ParserTag>(ReferenceEqualityComparer.Instance);
        foreach (var link in links)
        {
            for (var parent = link.Parent(); parent != null && ancestors.Add(parent); parent = parent.Parent()) { }
        }

        Assert.AreEqual(links.Count(a => a.Depth >= 2), result.Descendants, name);
        Assert.AreEqual(links.Count(a => a.Parent(HtmlTag.Div) != null), result.InDiv, name);
        Assert.AreEqual(ancestors.Count.ToString(), result.Ancestors, name);
        Assert.AreEqual(links.Count, result.Self, name);
    }

    /// <summary>Document order, positions inside the source, and a parent that encloses the tag one level up.</summary>
    private static void AssertConsistent(HtmlParser doc, string html, string name)
    {
        var tags = doc.ToList();

        for (int k = 0; k < tags.Count; k++)
        {
            var tag = tags[k];
            if (k > 0 && tag.Start <= tags[k - 1].Start) Assert.Fail($"not in document order at {tag.Start}: {name}");
            if (tag.End < tag.Start || tag.End >= html.Length) Assert.Fail($"[{tag.Name}] {tag.Start}..{tag.End} is outside the source: {name}");

            var parent = tag.Parent();
            if (!ReferenceEquals(parent, ParentByDepth(tags, k))) Assert.Fail($"[{tag.Name}] at {tag.Start}: the parent is not the nearest shallower tag: {name}");

            if (parent == null)
            {
                if (tag.Depth != 0) Assert.Fail($"[{tag.Name}] at {tag.Start}: depth {tag.Depth} without a parent: {name}");
            }
            else if (parent.Depth + 1 != tag.Depth || parent.Start >= tag.Start || parent.End < tag.End)
            {
                Assert.Fail($"[{parent.Name}] {parent.Start}..{parent.End} does not enclose [{tag.Name}] {tag.Start}..{tag.End}: {name}");
            }
        }
    }

    /// <summary>The parent as it follows from the depths alone: the nearest previous tag that is shallower.</summary>
    private static ParserTag? ParentByDepth(List<ParserTag> tags, int index)
    {
        var tag = tags[index];
        if (tag.Depth == 0) return null;

        // checked for the previous tag, its parent, the parent of that, ...: linear even for a long list of siblings
        for (var candidate = tags[index - 1]; candidate != null; candidate = candidate.Parent())
        {
            if (candidate.Depth < tag.Depth) return candidate.Depth + 1 == tag.Depth ? candidate : null;
        }

        return null;
    }

    [TestMethod]
    public void Dispose_MakesTheDocumentAndItsTagsUnusable()
    {
        var doc = new HtmlParser("<div id='a'><p>text</p></div>", "https://example.com/");
        var p = doc.Find(HtmlTag.P)!;

        doc.Dispose();
        doc.Dispose(); // twice is fine

        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = p.Text(); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = p.Source(); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = p.AttributeByName("id"); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = p.Parent(); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = p.Next(); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = p.XPath(".//a"); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = doc.Find(HtmlTag.P); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = doc.FindAll(HtmlTag.P).Count(); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = doc.Count(); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = ((IEnumerable)doc).GetEnumerator(); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = doc.PageSource; });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = doc.PageUrl; });

        // what a tag holds itself is still there
        Assert.AreEqual("p", p.Name);
        Assert.AreEqual(1, p.Depth);
    }
}
