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
    public void Links_AreNotDecoded_OnlyResolved()
    {
        var doc = new HtmlParser("<a href='/p?a=1&amp;b=2&#38;c=3'>x</a><img src='/i.png?w=1&amp;h=2'>", "https://example.com/");

        Assert.AreEqual("https://example.com/p?a=1&b=2&#38;c=3", doc.FindByName(HtmlTag.A).AttributeHref()); // only &amp;
        Assert.AreEqual("https://example.com/i.png?w=1&h=2", doc.FindByName(HtmlTag.Img).AttributeSrc());
        Assert.AreEqual("/p?a=1&b=2&c=3", doc.FindByName(HtmlTag.A).AttributeByName(HtmlAttribute.Href)); // the generic getter decodes
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
