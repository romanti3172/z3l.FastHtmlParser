namespace z3l.FastHtmlParser.Tests;

/// <summary>Entity decoding and text extraction.</summary>
[TestClass]
public sealed class TextTests
{
    [TestMethod]
    [DataRow("Q&amp;A", "Q&A")]
    [DataRow("&lt;b&gt; &quot;x&quot; &apos;y&apos;", "<b> \"x\" 'y'")]
    [DataRow("&copy; &euro; &mdash; &hellip; &nbsp;", "© € — …  ")]
    [DataRow("&check; &NewLine;", "✓ \n")]                         // HTML5-only names
    [DataRow("&NotEqualTilde; &Afr;", "≂̸ 𝔄")] // two code points, astral plane
    [DataRow("&copy2026 &notit;", "©2026 ¬it;")]                     // legacy names without ';'
    [DataRow("&#65;&#x42;&#X43;&#68", "ABCD")]                       // numeric, ';' optional
    [DataRow("&#128; &#x9F;", "€ Ÿ")]                                // Windows-1252 range
    [DataRow("&#0; &#xD800; &#x110000;", "� � �")]    // invalid code points
    [DataRow("a & b &; &unknown; &#; &#x;", "a & b &; &unknown; &#; &#x;")] // not references
    [DataRow("no entities", "no entities")]
    public void Decode(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.Decode(html));
    }

    [TestMethod]
    public void ToText_RemovesTags_CollapsesWhitespace_Decodes()
    {
        Assert.AreEqual("Hello world & you", HtmlText.ToText("  <b>Hello</b>\n\t  world <i>&amp;</i> you  "));
        Assert.AreEqual("textmore", HtmlText.ToText("text<!-- <b>hidden</b> -->more"));
        Assert.AreEqual("", HtmlText.ToText("<br/>"));
        Assert.IsNull(HtmlText.ToText(""));
    }

    [TestMethod]
    [DataRow("<p>First</p><p>Second</p>", "First\n\nSecond")]
    [DataRow("Line one<br>Line two<BR/>Line three<br class='x'>Line four", "Line one\nLine two\nLine three\nLine four")]
    [DataRow("<P class='a'>Upper</P><p>lower</p>", "Upper\n\nlower")]
    [DataRow("<ul><li>one</li><li>two</li></ul>", "one\ntwo")]
    [DataRow("<div>Block 1</div><div>Block 2</div>", "Block 1\nBlock 2")]
    [DataRow("<h2>Title</h2>Body text", "Title\n\nBody text")]
    [DataRow("<table><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>", "a b\nc d")]
    [DataRow("a<br><br><br><br>b", "a\n\nb")]
    [DataRow("<p>a</p><p>&nbsp;</p><p>b</p>", "a\n\nb")]
    [DataRow("Text <b>bold</b> and <i>it</i>alic", "Text bold and italic")]
    [DataRow("<script>var x = '<p>';</script>Visible<style>p{}</style>", "Visible")]
    [DataRow("  lots   of\n\n spaces \r\n and\tnewlines  ", "lots of spaces and newlines")]
    [DataRow("Q&amp;A &lt;tag&gt;", "Q&A <tag>")]
    public void ToTextWithLineBreaks(string html, string expected)
    {
        Assert.AreEqual(expected.Replace("\n", Environment.NewLine), HtmlText.ToTextWithLineBreaks(html));
    }

    [TestMethod]
    public void IsText_ComparesTheVisibleText()
    {
        var doc = new HtmlParser("<dt>  Price <b>&amp;</b>  tax </dt><dt>Size</dt>");

        Assert.IsTrue(doc.FindByName(HtmlTag.Dt).IsText("Price & tax"));
        Assert.IsNotNull(doc.FindByNameText(HtmlTag.Dt, "Price & tax"));
        Assert.IsNotNull(doc.FindByNameText(HtmlTag.Dt, "Size"));
        Assert.IsNull(doc.FindByNameText(HtmlTag.Dt, "Price"));
    }

    [TestMethod]
    public void Text_SkipsScriptsStylesAndOtherInvisibleContent()
    {
        var doc = new HtmlParser("""
            <div id='d'>Hello
              <script type="text/javascript">if (a < b) { document.write("<p>no</p>"); }</script>
              <style>p > b { color: red }</style>
              <noscript>Enable JS</noscript>
              <template><p>template</p></template>
              <!-- comment <b>x</b> -->
              <script src="lib.js" />
              <b>world</b>
            </div>
            """);
        var div = doc.FindByNameId(HtmlTag.Div, "d");

        Assert.AreEqual("Hello world", div.Text());
        Assert.IsTrue(div.IsText("Hello world"));
        Assert.IsNotNull(doc.FindByNameText(HtmlTag.Div, "Hello world"));

        // asked directly, a script still gives its code
        Assert.AreEqual("if (a < b) { document.write(\"<p>no</p>\"); }", doc.FindByName(HtmlTag.Script).Text());
    }

    [TestMethod]
    public void Text_UnterminatedScript_HidesTheRest()
    {
        Assert.AreEqual("before", HtmlText.ToText("before<script>var x = 1;"));
        Assert.AreEqual("before after", HtmlText.ToText("before <scripts>after</scripts>")); // not a script: "after" is text
    }

    [TestMethod]
    public void Text_RealPage_HasNoJavaScript()
    {
        var body = Fixtures.Load("webscraper/product-31.html").FindByName(HtmlTag.Body);

        var text = body.Text()!;
        Assert.DoesNotContain("getElementsByTagName", text);
        StringAssert.Contains(text, "Packard 255 G2");
    }

    [TestMethod]
    public void TextLineBreaks_OnATag()
    {
        var doc = new HtmlParser("<div class='d'><p>Hello<br>world</p><p>Bye</p></div>");

        Assert.AreEqual($"Hello{Environment.NewLine}world{Environment.NewLine}{Environment.NewLine}Bye", doc.FindByName(HtmlTag.Div).TextLineBreaks());
    }

    [TestMethod]
    public void AttributeHref_ResolvesAndUnescapes()
    {
        var doc = new HtmlParser("<a href='/p?q=1&amp;r=2'>x</a><a href='https://other.org/a'>y</a><a>z</a>", "https://example.com/dir/page.html");
        var links = doc.FindAllByName(HtmlTag.A).ToList();

        Assert.AreEqual("https://example.com/p?q=1&r=2", links[0].AttributeHref());
        Assert.AreEqual("https://other.org/a", links[1].AttributeHref());
        Assert.AreEqual("https://example.com/dir/page.html", links[2].AttributeHref()); // no href: the page URL
        Assert.AreEqual("https://base.org/p?q=1&r=2", links[0].AttributeHref("https://base.org/x/"));
    }

    [TestMethod]
    public void AttributeHref_PathLikeLinks_AreRelativeOnEveryOs()
    {
        // .NET reads "/path" (Linux/macOS) and "//host/path" (every OS) as implicit file paths.
        var doc = new HtmlParser(
            "<a href='/author/Albert-Einstein'>a</a><a href='//cdn.example.org/x.js'>b</a><a href='file:///etc/hosts'>c</a><a href='mailto:me@example.com'>d</a>",
            "https://example.com/dir/page.html");
        var links = doc.FindAllByName(HtmlTag.A).ToList();

        Assert.AreEqual("https://example.com/author/Albert-Einstein", links[0].AttributeHref());
        Assert.AreEqual("https://cdn.example.org/x.js", links[1].AttributeHref());
        Assert.AreEqual("file:///etc/hosts", links[2].AttributeHref());
        Assert.AreEqual("mailto:me@example.com", links[3].AttributeHref());
    }

    [TestMethod]
    public void Attributes_AreDecoded_AttributeBodyIsRaw()
    {
        var doc = new HtmlParser("<a title='Shakespeare&#39;s &amp; Co' data-q='?a=1&copy=2' class='x&#32;y'>t</a>");
        var a = doc.FindByName(HtmlTag.A);

        Assert.AreEqual("Shakespeare's & Co", a.AttributeTitle());
        Assert.AreEqual("Shakespeare's & Co", a.AttributeByName(HtmlAttribute.Title));
        Assert.AreEqual("Shakespeare&#39;s &amp; Co", a.AttributeBody(HtmlAttribute.Title));
        Assert.AreEqual("Shakespeare&#39;s &amp; Co", a.AttributeBody("title"));
        Assert.AreEqual("?a=1&copy=2", a.AttributeByName("data-q")); // HTML5 attribute rule: no "©" here

        // comparisons use the decoded value
        Assert.IsTrue(a.IsAttribute(HtmlAttribute.Title, "Shakespeare's & Co"));
        Assert.IsNotNull(doc.FindByNameAttribute(HtmlTag.A, HtmlAttribute.Title, "Shakespeare's & Co"));
        Assert.IsTrue(a.IsClass("y")); // "x&#32;y" → "x y"
    }

    [TestMethod]
    public void Links_AreDecoded_AndResolved()
    {
        var doc = new HtmlParser("<a href='/p?a=1&amp;b=2&#38;c=3&copy=4'>x</a><img src='/i.png?w=1&amp;h=2'>", "https://example.com/");

        // as a browser reads the attribute: every character reference, by the attribute rules ("&copy=" stays)
        Assert.AreEqual("https://example.com/p?a=1&b=2&c=3&copy=4", doc.FindByName(HtmlTag.A).AttributeHref());
        Assert.AreEqual("https://example.com/i.png?w=1&h=2", doc.FindByName(HtmlTag.Img).AttributeSrc());
        Assert.AreEqual("/p?a=1&b=2&c=3&copy=4", doc.FindByName(HtmlTag.A).AttributeByName(HtmlAttribute.Href)); // the same value, not resolved
    }

    [TestMethod]
    public void Links_KeepPercentEscapes()
    {
        var doc = new HtmlParser(
            "<a href='/files/what%3F.pdf'>a</a><a href='/r?u=https%3A%2F%2Fx.com%2Fp%3Fid%3D1'>b</a>",
            "https://example.com/dir/page.html");
        var links = doc.FindAllByName(HtmlTag.A).ToList();

        // "%3F" is a '?' in a file name, not the start of the query
        Assert.AreEqual("https://example.com/files/what%3F.pdf", links[0].AttributeHref());
        Assert.AreEqual("https://example.com/r?u=https%3A%2F%2Fx.com%2Fp%3Fid%3D1", links[1].AttributeHref());
        Assert.AreEqual("/files/what%3F.pdf", links[0].AttributeHref(""));
    }

    [TestMethod]
    public void MailToAndTelephone_AreDecoded()
    {
        var doc = new HtmlParser("<a href='mailto:me&#64;example.com'>mail</a><a href=' tel:+1&#32;555&amp;1 '>call</a><a href='/page'>page</a>");
        var links = doc.FindAllByName(HtmlTag.A).ToList();

        Assert.AreEqual("me@example.com", links[0].AttributeHrefMailTo());
        Assert.AreEqual("+1 555&1", links[1].AttributeHrefTelephone());
        Assert.IsNull(links[2].AttributeHrefMailTo());
        Assert.IsNull(links[0].AttributeHrefTelephone());
    }

    [TestMethod]
    public void Attributes_AreNotTakenFromInsideOtherValues()
    {
        var doc = new HtmlParser(
            "<a title=\"go id=5 now\" id=\"real\" data-x='x class=\"fake\"' class=\"true\" alt=\"see href=/evil\" href=\"/good\">x</a>");
        var a = doc.Find(HtmlTag.A)!;

        Assert.AreEqual("real", a.AttributeByName("id"));
        Assert.AreEqual("true", a.AttributeByName(HtmlAttribute.Class));
        Assert.AreEqual("/good", a.AttributeByName("href"));
        Assert.AreEqual("/good", a.AttributeHref());
        Assert.AreEqual("go id=5 now", a.AttributeTitle());

        Assert.IsTrue(a.IsId("real"));
        Assert.IsFalse(a.IsId("5"));
        Assert.IsTrue(a.IsClass("true"));
        Assert.IsFalse(a.IsClass("fake"));
        Assert.AreSame(a, doc.Find(null, id: "real"));
        Assert.IsNull(doc.Find(null, id: "5"));
        Assert.IsNull(doc.Find(null, cls: "fake"));
        Assert.AreEqual("real", doc.XPathValue("//a/@id")); // the same answer as XPath
    }

    [TestMethod]
    public void Attributes_UnquotedValues_AndNoSpaceBetweenAttributes()
    {
        var doc = new HtmlParser("<a href=/p?a=1&b=2 class=x>1</a><a href=\"/q\"class=\"y\"id='z'>2</a><input disabled value=v CHECKED>");
        var links = doc.FindAll(HtmlTag.A).ToList();
        var input = doc.Find(HtmlTag.Input);

        Assert.AreEqual("/p?a=1&b=2", links[0].AttributeByName("href")); // '=' is part of an unquoted value
        Assert.IsTrue(links[0].IsClass("x"));
        Assert.IsTrue(links[1].IsClass("y"));                             // right after the closing quote
        Assert.AreEqual("z", links[1].AttributeId());
        Assert.AreEqual("v", input.AttributeValue());
        Assert.AreEqual("", input.AttributeByName("disabled"));           // no value
        Assert.AreEqual("", input.AttributeByName("checked"));
        Assert.AreEqual("", input.AttributeByName("missing"));
    }

    [TestMethod]
    public void Comments_HaveNoAttributes()
    {
        var doc = new HtmlParser("<!-- <div id=\"main\" class=\"box\"> --><div id=\"main\" class=\"box\">x</div>");

        Assert.AreEqual("div", doc.Find(null, id: "main")!.Name);
        Assert.AreEqual("div", doc.Find(null, cls: "box")!.Name);
        Assert.AreEqual("", doc.Find(HtmlTag.Comment).AttributeByName("id"));
    }

    [TestMethod]
    public void Books_TitleWithAnApostrophe()
    {
        var doc = Fixtures.Load("books/index.html");

        var link = doc.FindByNameAttribute(HtmlTag.A, HtmlAttribute.Title, "Shakespeare's Sonnets");
        Assert.IsNotNull(link);
        Assert.AreEqual("Shakespeare's Sonnets", link.AttributeTitle());
        Assert.AreEqual("Shakespeare&#39;s Sonnets", link.AttributeBody(HtmlAttribute.Title));
    }

    [TestMethod]
    public void EnumNames()
    {
        var doc = new HtmlParser("<!doctype html><img data-src='lazy.jpg' aria-label='pic'><svg><use xlink:href='#i'/></svg>");

        Assert.AreEqual("!doctype", doc.First().Name);
        Assert.AreEqual("lazy.jpg", doc.FindByName(HtmlTag.Img).AttributeByName(HtmlAttribute.DataSrc));
        Assert.AreEqual("pic", doc.FindByName(HtmlTag.Img).AttributeByName(HtmlAttribute.AriaLabel));
        Assert.AreEqual("#i", doc.FindByName(HtmlTag.Use).AttributeByName(HtmlAttribute.XlinkHref));
        Assert.IsTrue(doc.FindByName(HtmlTag.Img).IsName("IMG"));
    }
}
