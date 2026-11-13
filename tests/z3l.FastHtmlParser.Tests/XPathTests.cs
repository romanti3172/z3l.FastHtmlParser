using System.Globalization;
using System.Xml;
using System.Xml.XPath;
using AngleSharp.Html.Parser;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// XPath: cross-checked with System.Xml (the .NET XPath 1.0 implementation) on a well-formed document,
/// with AngleSharp CSS selectors on the saved pages, and HTML-specific behavior.
/// </summary>
[TestClass]
public sealed class XPathTests
{
    // Well-formed, every element has a unique id: results are compared by id with System.Xml.
    private const string Xhtml = """
        <html id="html">
        <head id="head"><title id="title">Shop</title></head>
        <body id="body">
          <div id="d1" class="card featured">
            <h2 id="h1">Alpha</h2>
            <span id="p1" class="price">10</span>
            <a id="a1" href="/alpha" rel="nofollow">More</a>
          </div>
          <div id="d2" class="card">
            <h2 id="h2">Beta</h2>
            <span id="p2" class="price">25</span>
            <a id="a2" href="/beta">More</a>
          </div>
          <div id="d3" class="card">
            <h2 id="h3">Gamma</h2>
            <span id="p3" class="price old">7</span>
            <span id="p4" class="price">5</span>
          </div>
          <ul id="ul">
            <li id="li1">one</li><li id="li2">two</li><li id="li3">three<ul id="ul2"><li id="li4">four</li></ul></li>
          </ul>
          <!-- note -->
          <p id="para">Hello <b id="b">bold</b> world</p>
        </body>
        </html>
        """;

    public static IEnumerable<object[]> NodeSetExpressions =>
        new[]
        {
            "//div", "//DIV", "/html/body/div[2]/h2", "//body/*[3]", "//*",
            "//div[@class='card']", "//div[@class!='card']", "//div[contains(@class,'featured')]",
            "//span[@class='price']", "//span[@class='price'][2]", "//*[@rel]", "//*[not(@rel)]",
            "//div/span[1]", "//div/span[last()]", "(//span)[2]", "(//span)[last()]", "//li[1]", "//ul//li[1]",
            "//li[position()>1]", "//li[last()-1]", "//div[2]", "//div[position() mod 2 = 1]",
            "//div[h2='Beta']/a", "//div[span][a]", "//div[not(a)]", "//div[.//a]", "//li[ul]",
            "//div[count(span)=2]", "//div[span > 20]", "//div[span < 8]", "//span[number(.) mod 2 = 1]",
            "//h2[.='Gamma']/following-sibling::span", "//span[@id='p4']/preceding-sibling::*",
            "//li[@id='li3']/preceding-sibling::li[1]", "//li[@id='li3']/preceding-sibling::li[last()]",
            "//li/following-sibling::li[1]", "//a/..", "//b/parent::p", "//p/text()/..",
            "//li/ancestor::ul", "//li[@id='li4']/ancestor-or-self::*", "//li[@id='li4']/ancestor::*[2]",
            "//h2/following::a", "//a[@id='a2']/preceding::h2", "//a[@id='a2']/preceding::*[1]",
            "//div[@id='d1']//*", "//div[@id='d1']/descendant-or-self::*", "descendant::li", "//li/self::li",
            "//ul/li | //p", "//p | //h2 | //p", "//*[self::h2 or self::a]", "//*[name()='span'][@class='price old']",
            "//*[local-name()='h2']", "//li[starts-with(.,'t')]", "//li[string-length(.)=3]",
            "//div[substring(@id,2)='2']", "//div[translate(@id,'d','D')='D3']", "//h2[normalize-space(.)='Alpha']",
            "//span[. = //h2[.='Alpha']/../span]", "//div[span = 5]", "//span[@class='price' and . >= 10]",
            "//span[@class='price old' or . = '25']", "//*[@id = 'b' or @id = 'title']", "//div[-1 + 3]",
            "//*[contains(concat(' ', normalize-space(@class), ' '), ' price ')]",
        }.Select(x => new object[] { x });

    [TestMethod]
    [DynamicData(nameof(NodeSetExpressions))]
    public void NodeSets_SameAsSystemXml(string xpath)
    {
        var expected = SystemXml().SelectNodes(xpath.Replace("DIV", "div"))!
            .Cast<XmlNode>()
            .Where(n => n.NodeType == XmlNodeType.Element)
            .Select(n => n.Attributes!["id"]!.Value)
            .ToArray();

        var actual = new HtmlParser(Xhtml).XPath(xpath).Select(t => t.AttributeByName(HtmlAttribute.Id)).ToArray();

        CollectionAssert.AreEqual(expected, actual, $"{xpath}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    [TestMethod]
    [DataRow("count(//li)")]
    [DataRow("count(//div[span > 20])")]
    [DataRow("sum(//span[@class='price'])")]
    [DataRow("sum(//span) div count(//span)")]
    [DataRow("string(//h2)")]
    [DataRow("//title")]
    [DataRow("concat(//h2[1], '-', (//h2)[3])")]
    [DataRow("name(//*[@rel])")]
    [DataRow("boolean(//table)")]
    [DataRow("not(//table)")]
    [DataRow("//a = '/beta'")]
    [DataRow("//a/@href = '/beta'")]
    [DataRow("count(//span) > 3")]
    [DataRow("7 mod 3")]
    [DataRow("-7 div 2")]
    [DataRow("1 div 0")]
    [DataRow("0 div 0")]
    [DataRow("floor(2.5) + ceiling(2.5) + round(2.5) + round(-2.5)")]
    [DataRow("substring('12345', 1.5, 2.6)")]
    [DataRow("substring('12345', 0, 3)")]
    [DataRow("substring('12345', 2)")]
    [DataRow("substring-before('2026-11-14', '-')")]
    [DataRow("substring-after('2026-11-14', '-')")]
    [DataRow("translate('bar', 'abc', 'ABC')")]
    [DataRow("translate('--aaa--', 'abc-', 'ABC')")]
    [DataRow("normalize-space('  a   b  ')")]
    [DataRow("string-length('hello')")]
    [DataRow("number('  12.5 ')")]
    [DataRow("number('abc')")]
    [DataRow("string(0.1 + 0.2)")]
    [DataRow("string(1000000)")]
    [DataRow("string(-0.5)")]
    [DataRow("string(1 div 3)")]
    [DataRow("starts-with('abc', 'ab') and contains('abc', 'bc')")]
    [DataRow("//li[@id='li4']/ancestor::li/@id")]
    [DataRow("count(//li[@id='li3']/preceding::*)")]
    [DataRow("count(//body/following::*)")]
    [DataRow("count(//@id)")]
    [DataRow("count(//div/@*)")]
    public void Values_SameAsSystemXml(string xpath)
    {
        var expected = SystemXml().CreateNavigator()!.Evaluate(xpath) switch
        {
            XPathNodeIterator nodes => nodes.MoveNext() ? nodes.Current!.Value : null,
            double d => XmlConvert.ToString(d) switch { "INF" => "Infinity", "-INF" => "-Infinity", var s => s },
            bool b => b ? "true" : "false",
            var other => Convert.ToString(other, CultureInfo.InvariantCulture),
        };

        Assert.AreEqual(expected, new HtmlParser(Xhtml).XPathValue(xpath), xpath);
    }

    [TestMethod]
    public void Numbers_WithoutExponent()
    {
        // XPath 1.0 numbers have no exponent (System.Xml writes "1E-05" here)
        var doc = new HtmlParser("");

        Assert.AreEqual("0.00001", doc.XPathValue("string(0.00001)"));
        Assert.AreEqual("-123456789012345690000000", doc.XPathValue("string(-123456789012345678901234)"));
        Assert.AreEqual("1.5", doc.XPathValue("3 div 2"));
    }

    [TestMethod]
    public void TextNodes_SameAsSystemXml()
    {
        foreach (var xpath in new[] { "//p/text()", "//li/text()", "//p/node()", "//p/text()[2]", "//li[@id='li3']/node()[last()]" })
        {
            var expected = SystemXml().SelectNodes(xpath)!.Cast<XmlNode>().Select(n => n.InnerText).ToArray();
            CollectionAssert.AreEqual(expected, new HtmlParser(Xhtml).XPathValues(xpath).ToArray(), xpath);
        }
    }

    private static XmlDocument SystemXml()
    {
        var xml = new XmlDocument { PreserveWhitespace = true };
        xml.LoadXml(Xhtml);
        return xml;
    }

    // HTML specifics

    [TestMethod]
    public void Html_CaseInsensitiveNamesAndAttributes()
    {
        var doc = new HtmlParser("""<DIV ID="x" Class="A"><SPAN>t</SPAN></DIV>""");

        Assert.AreEqual("t", doc.XPathFirst("//div[@id='x']/span").Text());
        Assert.AreEqual("A", doc.XPathValue("//DIV/@CLASS"));
        Assert.AreEqual("div", doc.XPathValue("name(/*)"));
    }

    [TestMethod]
    public void Html_VoidTagsAttributesWithoutValueAndUnquotedValues()
    {
        var doc = new HtmlParser("""
            <form><input type=text name=q disabled><input type="checkbox" checked><img src=/a.png alt='A &amp; B'><br></form>
            """);

        Assert.HasCount(2, doc.XPath("//form/input"));
        Assert.AreEqual("q", doc.XPathValue("//input[@disabled]/@name"));
        Assert.AreEqual("checkbox", doc.XPathValue("//input[@checked]/@type"));
        Assert.AreEqual("", doc.XPathValue("//input/@disabled"));
        Assert.AreEqual("/a.png", doc.XPathValue("//img/@src"));
        Assert.AreEqual("A & B", doc.XPathValue("//img/@alt"));
        Assert.AreEqual("img", doc.XPathFirst("//*[@alt='A & B']")!.Name);
        CollectionAssert.AreEqual(new[] { "text", "q", "" }, doc.XPathValues("(//input)[1]/@*").ToArray());
        CollectionAssert.AreEqual(new[] { "type", "name", "disabled" }, Enumerable.Range(1, 3).Select(i => doc.XPathValue($"name((//input)[1]/@*[{i}])")).ToArray());
    }

    [TestMethod]
    public void Html_OptionalEndTags()
    {
        var doc = new HtmlParser("<ul><li>one<li>two<li>three</ul><p>a<p>b");

        CollectionAssert.AreEqual(new[] { "one", "two", "three" }, doc.XPathValues("//ul/li").ToArray());
        Assert.AreEqual("two", doc.XPathValue("//li[2]/text()"));
        Assert.AreEqual("three", doc.XPathValue("//li[last()]"));
        Assert.AreEqual("b", doc.XPathValue("//p[2]"));
    }

    [TestMethod]
    public void Html_EntitiesAreDecoded()
    {
        var doc = new HtmlParser("""<p title="It&#39;s">Fish &amp; chips&nbsp;now</p><script>if (a &amp;&amp; b) {}</script>""");

        Assert.AreEqual("It's", doc.XPathValue("//p/@title"));
        Assert.IsNotNull(doc.XPathFirst("//p[@title=\"It's\"]"));
        Assert.AreEqual("Fish & chips now", doc.XPathValue("//p/text()"));
        Assert.IsNotNull(doc.XPathFirst("//p[normalize-space(.)='Fish & chips now']"));
        Assert.AreEqual("if (a &amp;&amp; b) {}", doc.XPathValue("//script/text()")); // raw text is not decoded
    }

    [TestMethod]
    public void Html_StringValueOfElementIsVisibleText()
    {
        var doc = new HtmlParser("<div>  Price:\n <b>10</b><script>x()</script> <!-- c --> USD </div>");

        Assert.AreEqual("Price: 10 USD", doc.XPathValue("//div"));
        Assert.IsNotNull(doc.XPathFirst("//div[.='Price: 10 USD']"));
        Assert.IsNotNull(doc.XPathFirst("//div[contains(., '10 USD')]"));
    }

    [TestMethod]
    public void Comments()
    {
        var doc = new HtmlParser("<div><!-- first --><p>x</p><!--second--></div>");

        CollectionAssert.AreEqual(new[] { " first ", "second" }, doc.XPathValues("//comment()").ToArray());
        Assert.AreEqual("!--", doc.XPathFirst("//div/comment()[2]")!.Name);
        Assert.HasCount(1, doc.XPath("//div/*")); // * is elements only
        Assert.HasCount(3, doc.XPath("//div/node()"));
    }

    [TestMethod]
    public void PrefixedNames_Rss()
    {
        var doc = new HtmlParser("""
            <rss><channel><item><title>T1</title><dc:creator>Ann</dc:creator><media:content url="/1.jpg"/></item>
            <item><title>T2</title><dc:creator>Bob</dc:creator></item></channel></rss>
            """);

        CollectionAssert.AreEqual(new[] { "Ann", "Bob" }, doc.XPathValues("//item/dc:creator").ToArray());
        Assert.AreEqual("/1.jpg", doc.XPathValue("//media:content/@url"));
        Assert.HasCount(1, doc.XPath("//item[1]/dc:*"));
        Assert.AreEqual("T2", doc.XPathValue("//item[dc:creator='Bob']/title"));
    }

    [TestMethod]
    public void RelativeToATag()
    {
        var doc = new HtmlParser(Xhtml);
        var card = doc.XPathFirst("//div[@id='d2']")!;

        Assert.AreEqual("/beta", card.XPathValue("a/@href"));
        Assert.HasCount(1, card.XPath(".//a"));
        Assert.HasCount(2, card.XPath("//a"));          // absolute: the whole document
        Assert.AreEqual("body", card.XPathFirst("..")!.Name);
        Assert.AreEqual("d2", card.XPathValue("@id"));
        Assert.AreEqual("Beta", card.XPathValue("string(h2)"));
        Assert.AreEqual("d3", card.XPathValue("following-sibling::div[1]/@id"));
    }

    [TestMethod]
    public void CompiledQueries_AreReusable()
    {
        var query = XPathQuery.Compile("//span[@class='price']");
        var first = new HtmlParser(Xhtml);
        var second = new HtmlParser("<span class='price'>1</span>");

        Assert.HasCount(3, first.XPath(query));
        Assert.HasCount(1, second.XPath(query));
        Assert.AreEqual("10", first.XPathValue(query));
        Assert.AreEqual("//span[@class='price']", query.ToString());

        // and from several threads at once
        var counts = Enumerable.Range(0, 64).AsParallel().Select(_ => first.XPath(query).Count).Distinct().ToArray();
        CollectionAssert.AreEqual(new[] { 3 }, counts);
    }

    [TestMethod]
    public void NullScopeAndMissingNodes()
    {
        ITagSearchable? none = null;

        Assert.IsEmpty(none.XPath("//a"));
        Assert.IsNull(none.XPathFirst("//a"));
        Assert.IsNull(none.XPathValue("//a"));
        Assert.IsEmpty(none.XPathValues("//a"));
        Assert.IsNull(new HtmlParser(Xhtml).XPathValue("//table"));
        Assert.IsEmpty(new HtmlParser("").XPath("//a"));
        Assert.IsEmpty(new HtmlParser(Xhtml).XPath("//a/@href")); // attributes are not tags: XPathValues
    }

    [TestMethod]
    [DataRow("//div[")]
    [DataRow("//div[@class=")]
    [DataRow("//a/@")]
    [DataRow("1 +")]
    [DataRow("//div)")]
    [DataRow("$x")]
    [DataRow("foo()")]
    [DataRow("count()")]
    [DataRow("count('a')")]
    [DataRow("namespace::x")]
    [DataRow("bogus::x")]
    [DataRow("'unterminated")]
    [DataRow("//a ! b")]
    [DataRow("1 | //a")]
    [DataRow("")]
    public void InvalidExpressions_Throw(string xpath)
    {
        var e = Assert.ThrowsExactly<ArgumentException>(() => new HtmlParser(Xhtml).XPathValue(xpath));
        StringAssert.Contains(e.Message, "Invalid XPath");
    }

    [TestMethod]
    public void ScalarExpressionForTags_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new HtmlParser(Xhtml).XPath("count(//a)"));
        Assert.ThrowsExactly<ArgumentNullException>(() => new HtmlParser(Xhtml).XPath((string)null!));
    }

    // The saved pages, cross-checked with AngleSharp CSS selectors

    [TestMethod]
    [DataRow("books/index.html", "//article[contains(concat(' ', normalize-space(@class), ' '), ' product_pod ')]//h3/a", "article.product_pod h3 > a")]
    [DataRow("books/product-light-in-the-attic.html", "//table[@class='table table-striped']//tr/th", "table.table-striped tr > th")]
    [DataRow("quotes/page1.html", "//div[@class='quote']/span/a", "div.quote > span > a")]
    [DataRow("quotes/page1.html", "//div[@class='tags']/a[@class='tag'][1]", "div.tags > a.tag:first-of-type")]
    [DataRow("quotes/page1.html", "//li[@class='next']/a", "li.next > a")]
    [DataRow("webscraper/laptops.html", "//div[contains(@class,'thumbnail')]//h4[contains(@class,'price')]", "div[class*='thumbnail'] h4[class*='price']")]
    [DataRow("webscraper/tables.html", "//table//tr[td][last()]/td[2]", "table tr:last-child > td:nth-child(2)")]
    public void Fixtures_SameAsAngleSharp(string path, string xpath, string css)
    {
        var html = Fixtures.Read(path);
        var expected = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html).QuerySelectorAll(css).Select(e => e.TextContent.Trim()).ToArray();
        var actual = new HtmlParser(html).XPath(xpath).Select(t => t.Text()).ToArray();

        Assert.IsNotEmpty(expected, css);
        CollectionAssert.AreEqual(expected, actual, xpath);
    }

    [TestMethod]
    public void Fixtures_ScrapeBooks()
    {
        var doc = Fixtures.Load("books/index.html");

        var titles = doc.XPathValues("//article[@class='product_pod']/h3/a/@title");
        Assert.HasCount(20, titles);
        Assert.AreEqual("A Light in the Attic", titles[0]);
        Assert.AreEqual("£51.77", doc.XPathValue("//article[@class='product_pod'][1]//p[@class='price_color']"));
        Assert.AreEqual("20", doc.XPathValue("count(//p[contains(@class,'star-rating')])"));
        Assert.AreEqual("Tipping the Velvet", doc.XPathValue("//article[.//p[@class='star-rating One']][1]/h3/a/@title"));
    }
}
