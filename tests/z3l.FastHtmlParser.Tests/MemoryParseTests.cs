using System.Buffers;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// <see cref="HtmlParser.Parse(ReadOnlyMemory{char}, string?)"/> and <see cref="HtmlParser.Parse(IMemoryOwner{char}, int, string?)"/>:
/// a document read in place from a buffer gives the same results as one parsed from a string.
/// </summary>
[TestClass]
public sealed class MemoryParseTests
{
    public static IEnumerable<object[]> Pages => Fixtures.All.Select(f => new object[] { f.Path });

    [TestMethod]
    [DynamicData(nameof(Pages))]
    public void SliceOfLargerBuffer_SameAsString(string path)
    {
        var html = Fixtures.Read(path);
        var url = Fixtures.All.First(f => f.Path == path).Url;

        // the text in the middle of a buffer, with markup around it that must not be read
        const string before = "<div id=\"outside\">before</div><a href=\"/x\">";
        const string after = "</a><p>after</p><!--";
        var buffer = (before + html + after).ToCharArray();
        var memory = new ReadOnlyMemory<char>(buffer, before.Length, html.Length);

        var expected = new HtmlParser(html, url);
        var actual = HtmlParser.Parse(memory, url);

        Assert.AreEqual(expected.Count(), actual.Count());
        foreach (var (e, a) in expected.Zip(actual))
        {
            Assert.AreEqual(e.Name, a.Name);
            Assert.AreEqual(e.Start, a.Start);
            Assert.AreEqual(e.End, a.End);
            Assert.AreEqual(e.Source(), a.Source());
            Assert.AreEqual(e.FullSource(), a.FullSource());
            Assert.AreEqual(e.Text(), a.Text());
            Assert.AreEqual(e.AttributeHref(), a.AttributeHref());
            Assert.AreEqual(e.AttributeByName(HtmlAttribute.Class), a.AttributeByName(HtmlAttribute.Class));
            Assert.AreEqual(e.TextAfterTagUntilNextOne(), a.TextAfterTagUntilNextOne());
        }

        CollectionAssert.AreEqual(expected.XPathValues("//a/@href").ToArray(), actual.XPathValues("//a/@href").ToArray());
        CollectionAssert.AreEqual(expected.XPathValues("//p").ToArray(), actual.XPathValues("//p").ToArray());
        Assert.AreEqual(expected.XPathValue("string(/)"), actual.XPathValue("string(/)"));
        CollectionAssert.AreEqual(
            expected.Css("li:nth-child(odd) a, p:has(+ *)").Select(t => t.Start).ToArray(),
            actual.Css("li:nth-child(odd) a, p:has(+ *)").Select(t => t.Start).ToArray());

        Assert.AreEqual(html, actual.PageSource);
        Assert.IsNull(actual.CssFirst("#outside"));
    }

    [TestMethod]
    public void WholeString_PageSourceIsNotCopied()
    {
        var html = "<p>a</p>";

        Assert.AreSame(html, HtmlParser.Parse(html.AsMemory()).PageSource);
        Assert.AreSame(html, new HtmlParser(html).PageSource);
        Assert.AreEqual("a", HtmlParser.Parse(html.AsMemory(3, 1)).PageSource);
    }

    [TestMethod]
    public void Empty()
    {
        Assert.IsEmpty(HtmlParser.Parse(ReadOnlyMemory<char>.Empty));
        Assert.AreEqual("", HtmlParser.Parse(default(ReadOnlyMemory<char>)).PageSource);
        Assert.IsEmpty(new HtmlParser(null));
        Assert.AreEqual("", new HtmlParser(null).PageSource);
    }

    [TestMethod]
    public void MemoryOwner_ReturnedOnDispose()
    {
        var owner = new TrackingOwner("<ul><li><a href='/a'>A</a></li><li>B</li></ul>   unused tail <b>x</b>");
        var doc = HtmlParser.Parse(owner, 46, "https://x.test/");

        Assert.AreEqual(0, owner.Disposed);
        Assert.AreEqual("https://x.test/a", doc.CssFirst("li a").AttributeHref());
        CollectionAssert.AreEqual(new[] { "A", "B" }, doc.Css("li").Select(t => t.Text()).ToArray());
        Assert.IsNull(doc.CssFirst("b"));

        var li = doc.CssFirst("li");
        doc.Dispose();
        doc.Dispose();

        Assert.AreEqual(1, owner.Disposed);
        Assert.ThrowsExactly<ObjectDisposedException>(() => doc.PageSource);
        Assert.ThrowsExactly<ObjectDisposedException>(() => li.Text());
    }

    [TestMethod]
    public void MemoryOwner_FromPool()
    {
        var html = Fixtures.Read("books/index.html");
        var owner = MemoryPool<char>.Shared.Rent(html.Length);
        html.AsSpan().CopyTo(owner.Memory.Span);

        using var doc = HtmlParser.Parse(owner, html.Length, "https://books.toscrape.com/");

        Assert.HasCount(20, doc.Css("article.product_pod"));
        Assert.AreEqual(new HtmlParser(html).Count(), doc.Count());
    }

    [TestMethod]
    public void MemoryOwner_InvalidArguments_OwnerStaysWithCaller()
    {
        var owner = new TrackingOwner("<p>x</p>");

        Assert.ThrowsExactly<ArgumentNullException>(() => HtmlParser.Parse(null!, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HtmlParser.Parse(owner, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HtmlParser.Parse(owner, 9));
        Assert.AreEqual(0, owner.Disposed);

        Assert.IsEmpty(HtmlParser.Parse(owner, 0));
    }

    private sealed class TrackingOwner(string text) : IMemoryOwner<char>
    {
        private readonly char[] _buffer = text.ToCharArray();

        public int Disposed { get; private set; }

        public Memory<char> Memory => _buffer;

        public void Dispose() => Disposed++;
    }
}
