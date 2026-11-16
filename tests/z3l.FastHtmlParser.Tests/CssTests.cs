using AngleSharp.Dom;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// CSS selectors: cross-checked with AngleSharp (a browser-grade selector engine) on a document where every element
/// has a unique id and on the saved pages, plus scoping, the non-standard parts and errors.
/// </summary>
[TestClass]
public sealed class CssTests
{
    private const string Page = """
        <!DOCTYPE html>
        <html id="html" lang="en-US">
        <head id="head"><title id="title">Shop</title><link id="css" rel="stylesheet" href="/s.css"></head>
        <body id="body">
          <div id="d1" class="card featured" data-kind="Book">
            <h2 id="h1">Alpha</h2>
            <span id="p1" class="price">10</span>
            <a id="a1" href="https://x.test/alpha?a=1&amp;b=2" rel="nofollow noopener">More</a>
          </div>
          <div id="d2" class="card" data-kind="book-new">
            <h2 id="h2">Beta</h2>
            <span id="p2" class="price">25</span>
            <a id="a2" href="/beta">More</a>
            <!-- comment -->
          </div>
          <div id="d3" class="card Card" data-kind="">
            <h2 id="h3">Gamma</h2>
            <span id="p3" class="price old">7</span>
            <span id="p4" class="price">5</span>
            <p id="e1"></p><p id="e2"><!-- only a comment --></p><p id="e3"> </p>
          </div>
          <ul id="ul">
            <li id="li1">one</li><li id="li2" class="odd">two</li><li id="li3">three<ul id="ul2"><li id="li4">four</li><li id="li5">five</li></ul></li><li id="li6">six</li>
          </ul>
          <form id="f">
            <input id="i1" type="checkbox" checked>
            <input id="i2" type="text" disabled required>
            <input id="i3" type="TEXT">
            <select id="s"><option id="o1">a</option><option id="o2" selected>b</option></select>
            <textarea id="t"></textarea>
            <button id="btn" disabled>Go</button>
          </form>
          <p id="para">Hello <b id="b">bold</b> <i id="i">world</i></p>
          <a id="a3" name="anchor">no href</a>
          <section id="sec"><article id="art1"><h3 id="t1">One</h3></article><article id="art2"><h3 id="t2">Two</h3><p id="ap">x</p></article></section>
        </body>
        </html>
        """;

    public static IEnumerable<object[]> Selectors =>
        new[]
        {
            // type, universal, id, class, lists
            "div", "DIV", "*", "#d2", "#nope", ".card", ".Card", ".card.featured", "div.card > span.price", "span.price.old",
            "h2, a", "a, h2, a", "body > *", "#ul > li", "ul li", "ul > li > ul > li", "section h3",

            // attributes
            "[data-kind]", "[data-kind='']", "[data-kind=Book]", "[data-kind=book i]", "[data-kind|=book]", "[data-kind^=Bo]",
            "[data-kind$=new]", "[data-kind*=ook]", "[data-kind^='']", "[rel~=noopener]", "[rel~=noo]", "a[href]",
            "a[href^='https://']", "a[href*='a=1&b=2']", "a[href$='b=2']", "[type=text]", "[type=text i]", "[TYPE=checkbox]",
            "[lang|=en]", "[class~=Card]", "[rel~=NOFOLLOW]", "[data-kind=BOOK]",

            // combinators
            "h2 + span", "h2 ~ span", "span + span", "h2 ~ a", "li + li", "li ~ li", "#li1 ~ *", "div > h2 + span.price",
            "article + article h3", "ul ul li", "body > ul > li", "#d1 ~ div > h2", "form > input + input ~ select > option",

            // structural
            "li:first-child", "li:last-child", "li:only-child", "span:first-of-type", "span:last-of-type", "p:only-of-type",
            "li:nth-child(2)", "li:nth-child(odd)", "li:nth-child(even)", "li:nth-child(2n+1)", "li:nth-child(-n+2)",
            "li:nth-child(n+3)", "li:nth-last-child(1)", "li:nth-last-child(-n + 2)", "span:nth-of-type(2)",
            "div > *:nth-last-of-type(1)", "*:nth-child(3n)", "li:nth-child(0n+3)", "li:nth-child(+2n - 1)",
            ":root", "html:root", "p:empty", "li:empty",

            // logical
            "div:not(.featured)", "li:not(:first-child):not(:last-child)", ":not(div, span, li)", "div :is(h2, a)",
            ":where(#d1, #d2) > a", "div:has(> a)", "div:has(a[rel])", "li:has(li)", "h2:has(+ span)", "h2:has(~ a)",
            "article:has(h3 + p)", "div:not(:has(a))", "ul:has(> li:nth-child(3))", "li:nth-child(odd of .odd, #li3)",
            "li:nth-child(1 of li:not(#li1))",

            // forms and links
            ":checked", "input:disabled", "*:enabled", ":required", "input:optional", "a:link", ":any-link",
        }.Select(x => new object[] { x });

    [TestMethod]
    [DynamicData(nameof(Selectors))]
    public void SameAsAngleSharp(string selector)
    {
        var expected = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(Page)
            .QuerySelectorAll(selector).Select(e => e.Id ?? "?").ToArray();

        var actual = new HtmlParser(Page).Css(selector).Select(t => t.AttributeByName(HtmlAttribute.Id)).ToArray();

        CollectionAssert.AreEqual(expected, actual, $"{selector}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    [TestMethod]
    [DataRow("d3", "span")]
    [DataRow("d3", "div span")]
    [DataRow("d3", ":scope > span:last-of-type")]
    [DataRow("ul", "li li")]
    [DataRow("ul", ":scope > li")]
    [DataRow("sec", "section article h3")]
    [DataRow("sec", ":scope article:first-child + article > *")]
    public void Scoped_SameAsAngleSharp(string id, string selector)
    {
        var expected = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(Page).GetElementById(id)!
            .QuerySelectorAll(selector).Select(e => e.Id).ToArray();

        var scope = new HtmlParser(Page).CssFirst("#" + id);
        var actual = scope.Css(selector).Select(t => t.AttributeByName(HtmlAttribute.Id)).ToArray();

        CollectionAssert.AreEqual(expected, actual, selector);
    }

    [TestMethod]
    public void LeadingChildCombinator_SelectsChildren()
    {
        var doc = new HtmlParser(Page);
        var ul = doc.CssFirst("#ul");

        CollectionAssert.AreEqual(new[] { "li1", "li2", "li3", "li6" }, Ids(ul.Css("> li")));
        CollectionAssert.AreEqual(new[] { "li4", "li5" }, Ids(ul.Css("> li > ul > li")));
        CollectionAssert.AreEqual(new[] { "html" }, Ids(doc.Css("> *")));
        Assert.ThrowsExactly<ArgumentException>(() => ul.Css("+ li"));
    }

    [TestMethod]
    public void CssFirst_IsCss_Contains()
    {
        var doc = new HtmlParser(Page);

        Assert.AreEqual("p1", doc.CssFirst("span.price")!.AttributeByName(HtmlAttribute.Id));
        Assert.IsNull(doc.CssFirst("table"));
        Assert.AreEqual("Beta", doc.CssFirst("div.card:has(a[href='/beta']) h2")!.Text());

        var link = doc.CssFirst("#a1");
        Assert.IsTrue(link.IsCss("a[rel~=nofollow]"));
        Assert.IsTrue(link.IsCss("div.featured > a"));
        Assert.IsTrue(link.IsCss(":scope"));
        Assert.IsFalse(link.IsCss("span, h2"));
        Assert.IsFalse(((ParserTag?)null).IsCss("a"));

        CollectionAssert.AreEqual(new[] { "h2" }, Ids(doc.Css("h2:contains(Beta)")));
        CollectionAssert.AreEqual(new[] { "d2" }, Ids(doc.Css("div.card:contains('Beta')")));
        CollectionAssert.AreEqual(new[] { "li3", "li4" }, Ids(doc.Css("li:contains(\"four\")")));

        // comments are not elements
        Assert.IsFalse(doc.Css("*").Any(t => t.Name == "!--"));
        Assert.IsEmpty(((HtmlParser?)null).Css("a"));
        Assert.IsNull(((ParserTag?)null).CssFirst("a"));
    }

    [TestMethod]
    public void AttributeValueCase_HtmlRulesAndFlags()
    {
        var doc = new HtmlParser(Page);

        // the HTML list of attributes compared ignoring case (AngleSharp leaves out lang)
        CollectionAssert.AreEqual(new[] { "html" }, Ids(doc.Css("[LANG|=EN]")));
        CollectionAssert.AreEqual(new[] { "i2" }, Ids(doc.Css("[type=text s]")));
        CollectionAssert.AreEqual(new[] { "i2", "i3" }, Ids(doc.Css("[type=text]")));
        CollectionAssert.AreEqual(new[] { "d1", "d2" }, Ids(doc.Css("[data-kind^=book i]")));
        CollectionAssert.AreEqual(new[] { "d2" }, Ids(doc.Css("[data-kind^=book]")));
    }

    [TestMethod]
    public void CompiledQuery_ReusedAcrossDocuments()
    {
        var query = CssQuery.Compile("li:nth-child(2) , h2");
        Assert.AreEqual("li:nth-child(2) , h2", query.ToString());

        CollectionAssert.AreEqual(new[] { "h1", "h2", "h3", "li2", "li5" }, Ids(new HtmlParser(Page).Css(query)));
        CollectionAssert.AreEqual(new[] { "x" }, Ids(new HtmlParser("<ol><li></li><li id=x></li></ol>").Css(query)));
    }

    [TestMethod]
    [DataRow("#a\\:b", "a:b")]
    [DataRow(".\\31 0", "c10")]
    [DataRow("dc\\:creator", "dc")]
    [DataRow("[title=\"say \\\"hi\\\"\"]", "q")]
    [DataRow("[data-x='\\26']", "amp")]
    public void Escapes(string selector, string id)
    {
        const string html = """<div id="a:b"></div><p class="10" id="c10"></p><dc:creator id="dc">x</dc:creator><b title='say "hi"' id="q"></b><i data-x="&amp;" id="amp"></i>""";

        CollectionAssert.AreEqual(new[] { id }, Ids(new HtmlParser(html).Css(selector)));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("div,")]
    [DataRow(",div")]
    [DataRow("div >")]
    [DataRow("div > > a")]
    [DataRow("a[href")]
    [DataRow("a[href=]")]
    [DataRow("a[href^ ='x']")]
    [DataRow("a[href='x' q]")]
    [DataRow("a[href='x]")]
    [DataRow("a::before")]
    [DataRow("a:hover")]
    [DataRow("a:nth-child(x)")]
    [DataRow("a:nth-child(2n+)")]
    [DataRow("a:not(b")]
    [DataRow("svg|rect")]
    [DataRow("#")]
    [DataRow(".1a")]
    [DataRow("div)")]
    public void InvalidSelector_Throws(string selector)
    {
        var e = Assert.ThrowsExactly<ArgumentException>(() => new HtmlParser(Page).Css(selector));
        StringAssert.Contains(e.Message, "position");
    }

    [TestMethod]
    public void NullSelector_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new HtmlParser(Page).Css((string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => CssQuery.Compile(null!));
    }

    [TestMethod]
    public void Disposed_Throws()
    {
        var doc = new HtmlParser(Page);
        var li = doc.CssFirst("li + li");
        doc.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => doc.Css("li"));
        Assert.ThrowsExactly<ObjectDisposedException>(() => li.IsCss("li + li"));
    }

    // The saved pages

    public static IEnumerable<object[]> PageSelectors =>
    [
        ["books/index.html", "article.product_pod h3 > a"],
        ["books/index.html", "article.product_pod p.price_color"],
        ["books/index.html", "p.star-rating.Three"],
        ["books/index.html", "ol.row > li:nth-child(3n+1) h3 a"],
        ["books/index.html", "ul.nav-list ul li a"],
        ["books/index.html", "li.next > a, li.previous > a"],
        ["books/product-light-in-the-attic.html", "table.table-striped tr > th"],
        ["books/product-light-in-the-attic.html", "table tr:has(th:contains(Tax)) td"],
        ["books/product-light-in-the-attic.html", "ul.breadcrumb > li:not(.active) a"],
        ["quotes/page1.html", "div.quote > span.text"],
        ["quotes/page1.html", "div.quote small.author"],
        ["quotes/page1.html", "div.tags > a.tag:first-of-type"],
        ["quotes/page1.html", "div.quote:has(a.tag[href*=love]) span.text"],
        ["quotes/page1.html", "div.quote:nth-of-type(2n) .author"],
        ["quotes/tableful.html", "table tr td a"],
        ["quotes/tableful.html", "tr:nth-last-child(-n+3) td"],
        ["quotes/login.html", "form input[type=text], form input[type=password]"],
        ["webscraper/laptops.html", "div[class*='thumbnail'] h4[class*='price']"],
        ["webscraper/laptops.html", "div.caption > h4 + h4 > a.title"],
        ["webscraper/laptops.html", "div.ratings p[data-rating]"],
        ["webscraper/tables.html", "table tr:last-child > td:nth-child(2)"],
        ["webscraper/tables.html", "table thead th:only-of-type, table tbody tr:first-child td:last-child"],
        ["webscraper/allinone.html", "div.caption > h4.price ~ h4 > a[itemprop=name]"],
        ["webscraper/allinone.html", "div.caption ~ div.ratings > p"],
        ["webscraper/allinone.html", "div.caption:has(> p.description) h4:not([itemprop]) a"],
        ["webscraper/pagination.html", "ul.pagination li:not(:first-child):not(:last-child) a"],
    ];

    [TestMethod]
    [DynamicData(nameof(PageSelectors))]
    public void Fixtures_SameAsAngleSharp(string path, string selector)
    {
        var html = Fixtures.Read(path);
        var angle = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        // AngleSharp has no :contains(): compare that one with a filter
        var expected = selector.Contains(":contains(")
            ? angle.QuerySelectorAll("table tr").Where(tr => tr.QuerySelector("th")?.TextContent.Contains("Tax") == true)
                .SelectMany(tr => tr.QuerySelectorAll("td")).Select(Normalize).ToArray()
            : angle.QuerySelectorAll(selector).Select(Normalize).ToArray();

        var actual = new HtmlParser(html).Css(selector).Select(t => t.Text()).ToArray();

        Assert.IsNotEmpty(expected, selector);
        CollectionAssert.AreEqual(expected, actual, selector);
    }

    private static string Normalize(IElement e) => string.Join(' ', e.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string[] Ids(IEnumerable<ParserTag> tags) => tags.Select(t => t.AttributeByName(HtmlAttribute.Id)).ToArray();
}
