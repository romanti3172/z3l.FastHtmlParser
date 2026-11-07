namespace z3l.FastHtmlParser.Tests;

/// <summary>Parsing rules: comments, raw text, void elements, XML documents, broken markup, navigation.</summary>
[TestClass]
public sealed class ParserBehaviorTests
{
    [TestMethod]
    public void Comments_AreOneTag_AndTheirContentIsNotMarkup()
    {
        var doc = new HtmlParser("<div><!-- <a href='x'>hidden</a> --><a href='y'>visible</a></div>");

        Assert.AreEqual(1, doc.FindAllByName(HtmlTag.A).Count());
        Assert.AreEqual("y", doc.FindByName(HtmlTag.A).AttributeByName(HtmlAttribute.Href));
        Assert.AreEqual("!--", doc.FindByName(HtmlTag.Comment)!.Name);
        Assert.AreEqual("visible", doc.FindByName(HtmlTag.Div).Text());
    }

    [TestMethod]
    [DataRow("<!--><p>a</p>", 1)]          // "<!-->" is an empty comment
    [DataRow("<!---><p>a</p>", 1)]         // "<!--->" too
    [DataRow("<!-- x --!><p>a</p>", 1)]    // "--!>" ends a comment
    [DataRow("<!-- never closed <p>a</p>", 0)]
    [DataRow("<!--[if IE]><p>ie</p><![endif]--><p>a</p>", 1)]
    public void Comments_EdgeCases(string html, int paragraphs)
    {
        Assert.AreEqual(paragraphs, new HtmlParser(html).FindAllByName(HtmlTag.P).Count());
    }

    [TestMethod]
    public void Comments_DoNotNestTheFollowingTags()
    {
        var doc = new HtmlParser("<ul><!-- items --><li>1</li><li>2</li></ul>");

        var first = doc.FindByName(HtmlTag.Li)!;
        Assert.AreEqual("ul", first.Parent()!.Name);
        Assert.AreEqual("2", first.NextSibling().Text());
    }

    [TestMethod]
    public void ScriptAndStyle_ContentIsText()
    {
        var doc = new HtmlParser("<div><script>if (a<b) { x = '</div><p>no</p>'; }</script><style>p>b{}</style><p>yes</p></div>");

        CollectionAssert.AreEqual(new[] { "yes" }, doc.FindAllByName(HtmlTag.P).Select(p => p.Text()).ToArray());
        Assert.AreEqual("if (a<b) { x = '</div><p>no</p>'; }", doc.FindByName(HtmlTag.Script).Body());
        Assert.AreEqual("div", doc.FindByName(HtmlTag.P)!.Parent()!.Name);
    }

    [TestMethod]
    public void RawText_EndTagIsCaseInsensitive_AndMustBeComplete()
    {
        var doc = new HtmlParser("<script>var s = '</scripts>';</SCRIPT ><p>after</p>");

        Assert.AreEqual("var s = '</scripts>';", doc.FindByName(HtmlTag.Script).Body());
        Assert.AreEqual("after", doc.FindByName(HtmlTag.P).Text());
    }

    [TestMethod]
    public void VoidElements_DoNotContainTheFollowingTags()
    {
        var doc = new HtmlParser("<!doctype html><html><head><meta charset='utf-8'><link rel='x'><title>T</title></head><body><img src='a.png'><br><p>text</p></body></html>");

        Assert.AreEqual("head", doc.FindByName(HtmlTag.Title)!.Parent()!.Name);
        Assert.AreEqual("body", doc.FindByName(HtmlTag.P)!.Parent()!.Name);
        Assert.AreEqual(0, doc.FindByName(HtmlTag.Html)!.Depth); // not inside <!doctype>
        Assert.IsFalse(doc.FindByName(HtmlTag.Img)!.HasChildren());
    }

    [TestMethod]
    public void XmlDocuments_FollowXmlRules()
    {
        var rss = new HtmlParser("""
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel>
              <title>Feed</title>
              <link>https://example.com/</link>
              <item><title>One</title><link>https://example.com/1</link><pubDate>Mon, 01 Jan 2026 00:00:00 GMT</pubDate></item>
              <item><title>Two</title><link>https://example.com/2</link></item>
            </channel></rss>
            """);

        var items = rss.FindAllByName(HtmlTag.Item).ToList();
        Assert.HasCount(2, items);
        Assert.AreEqual("https://example.com/1", items[0].FindByName(HtmlTag.Link).Text()); // <link> has content in RSS
        Assert.AreEqual("Mon, 01 Jan 2026 00:00:00 GMT", items[0].FindByName(HtmlTag.PubDate).Text());
        Assert.AreEqual("Two", items[1].FindByName(HtmlTag.Title).Text());
    }

    [TestMethod]
    public void BrokenMarkup_DoesNotThrow()
    {
        string[] inputs =
        [
            "", "<", ">", "<<<>>>", "</>", "< /div>", "<div", "<div class='a", "text only", "<p>unclosed",
            "</div></div><div>", "<a href=\"x>y\">z</a>", "<!--", "<script>", "<br/><br /><BR>", "<div/><span/>",
        ];

        foreach (var html in inputs)
        {
            var doc = new HtmlParser(html);
            foreach (var tag in doc)
            {
                _ = tag.Text();
                _ = tag.TextLineBreaks();
                _ = tag.Body();
                _ = tag.Source();
                _ = tag.FullSource();
                _ = tag.Parent();
                _ = tag.NextSibling();
                _ = tag.AttributeHref();
            }
        }
    }

    [TestMethod]
    public void QuotedAttributeValuesMayContainAngleBrackets()
    {
        var doc = new HtmlParser("<div data-x=\"<p>not a tag</p>\" title='a > b'><p>real</p></div>");

        Assert.AreEqual(1, doc.FindAllByName(HtmlTag.P).Count());
        Assert.AreEqual("a > b", doc.FindByName(HtmlTag.Div).AttributeTitle());
        Assert.AreEqual("<p>not a tag</p>", doc.FindByName(HtmlTag.Div).AttributeByName("data-x"));
    }

    [TestMethod]
    public void UnclosedTags_AreClosedByTheParent()
    {
        var doc = new HtmlParser("<ul><li>one<li>two</ul><p>after</p>");

        Assert.AreEqual(2, doc.FindAllByName(HtmlTag.Li).Count());
        Assert.AreEqual(0, doc.FindByName(HtmlTag.P)!.Depth);
    }

    [TestMethod]
    public void NamesAreCaseInsensitive_CustomNamesWork()
    {
        var doc = new HtmlParser("<DIV Class='Box'><my-widget data-id='7'>w</my-widget><svg><path d='M0'/></svg></DIV>");

        Assert.IsTrue(doc.FindByName("div").IsName(HtmlTag.Div));
        Assert.IsTrue(doc.FindByName(HtmlTag.Div).IsClass("box"));
        Assert.AreEqual("my-widget", doc.FindByName("MY-WIDGET")!.Name);
        Assert.AreEqual("7", doc.FindByName("my-widget").AttributeByName("data-id"));
        Assert.AreEqual("M0", doc.FindByName(HtmlTag.Path).AttributeByName(HtmlAttribute.D));
    }

    [TestMethod]
    public void MissingThings_GiveNull_NotExceptions()
    {
        var doc = new HtmlParser("<div></div>");

        Assert.IsNull(doc.FindByNameId(HtmlTag.Div, "nope").FindByName(HtmlTag.A).Text());
        Assert.IsNull(doc.FindByName(HtmlTag.Table).FindByName(HtmlTag.Td).AttributeHref());
        Assert.IsNull(doc.FindByName(HtmlTag.Span).Parent());
        Assert.AreEqual(0, doc.FindByName(HtmlTag.Ul).FindAllByName(HtmlTag.Li).Count());
        Assert.AreEqual(0, new HtmlParser(null).Count());
    }

    [TestMethod]
    public void LastTag_TextAndNext()
    {
        var doc = new HtmlParser("<p>a</p><b>last</b>");
        var last = doc.Last();

        Assert.AreEqual("last", last.Text());
        Assert.IsFalse(last.HasChildren());
        Assert.IsNull(last.Next());
    }

    [TestMethod]
    public void Parent_SkipsPreviousSiblings()
    {
        var doc = new HtmlParser("<div id='p'><span>1</span><span>2</span><b>x</b></div>");

        Assert.AreEqual("p", doc.FindByName(HtmlTag.B).Parent().AttributeId());
        Assert.AreEqual("p", doc.FindByName(HtmlTag.B).ParentByName(HtmlTag.Div).AttributeId());
    }

    [TestMethod]
    public void Siblings_AndDescendants()
    {
        var doc = new HtmlParser("<dl><dt>A</dt><dd>1</dd><dt>B</dt><dd><i>2</i></dd></dl>");

        Assert.AreEqual("2", doc.FindByNameText(HtmlTag.Dt, "B").NextSiblingByName(HtmlTag.Dd).Text());
        Assert.AreEqual("A", doc.FindByNameText(HtmlTag.Dt, "B").PreviousSiblingByName(HtmlTag.Dt).Text());

        var dl = doc.FindByName(HtmlTag.Dl)!;
        CollectionAssert.AreEqual(new[] { "dt", "dd", "dt", "dd" }, dl.FindAllByFirstLevel().Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "dt", "dd", "dt", "dd", "i" }, dl.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public void ParallelReads_GiveTheSameResults()
    {
        var doc = Fixtures.Load("books/index.html");
        var expected = doc.FindAllByNameClass(HtmlTag.P, "price_color").Select(p => p.Text()).ToArray();

        Parallel.For(0, 64, _ =>
        {
            var prices = doc.FindAllByNameClass(HtmlTag.P, "price_color").Select(p => p.Text()).ToArray();
            CollectionAssert.AreEqual(expected, prices);
        });
    }
}
