namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// Text() is the visible text in one line, as jsoup's text(): &lt;br&gt;, block elements and table cells separate
/// words, inline tags do not, whitespace and line breaks collapse to one space, &lt;pre&gt; keeps its whitespace.
/// Before 1.7 "line1&lt;br&gt;line2" was "line1line2" and a table row "Price100", as textContent in a browser.
/// </summary>
[TestClass]
public sealed class TextOneLineTests
{
    static string? TextOf(string html) => new HtmlParser("<div>" + html + "</div>").Find(HtmlTag.Div).Text();

    [TestMethod]
    [DataRow("line1<br>line2", "line1 line2")]
    [DataRow("line1<br><br><br>line2", "line1 line2")]
    [DataRow("line1 <br> line2", "line1 line2")]
    [DataRow("line1<br/>line2", "line1 line2")]
    [DataRow("line1<BR>line2", "line1 line2")]
    [DataRow("<p>a</p><p>b</p>", "a b")]
    [DataRow("<div>a</div><div>b</div>", "a b")]
    [DataRow("<ul><li>a</li><li>b</li></ul>", "a b")]
    [DataRow("<table><tr><td>Price</td><td>100</td></tr></table>", "Price 100")]
    [DataRow("<table><tr><th>a</th><th>b</th></tr><tr><td>1</td><td>2</td></tr></table>", "a b 1 2")]
    [DataRow("<h1>Title</h1>text", "Title text")]
    [DataRow("<dl><dt>Price<dd>100</dl>", "Price 100")]
    [DataRow("a<span>b</span>c", "abc")]
    [DataRow("a<b>b</b><i>c</i>", "abc")]
    [DataRow("<a href=x>link</a>text", "linktext")]
    [DataRow("a<my-widget>b</my-widget>", "ab")]
    [DataRow("a\nb", "a b")]
    [DataRow("a\r\n\t  b", "a b")]
    [DataRow("  a  ", "a")]
    [DataRow("a<!-- c -->b", "ab")]
    [DataRow("a<script>x()</script>b", "ab")]
    [DataRow("<p>a</p><script>x()</script><p>b</p>", "a b")]
    [DataRow("a&nbsp;&nbsp;b", "a  b")]
    [DataRow("a &amp; b", "a & b")]
    [DataRow("<br>a<br>", "a")]
    public void OneLine(string html, string text) => Assert.AreEqual(text, TextOf(html));

    [TestMethod]
    public void LeafTag_CollapsesItsWhitespaceToo()
    {
        // a tag without child tags returned its text as written: "a\nb"
        var doc = new HtmlParser("<span>a\n   b</span><p>one\r\ntwo</p>");
        Assert.AreEqual("a b", doc.Find(HtmlTag.Span).Text());
        Assert.AreEqual("one two", doc.Find(HtmlTag.P).Text());
    }

    [TestMethod]
    public void QuickNameCheck_IsTheListOfSeparators()
    {
        // SeparatesWords compares short names directly and packs the longer ones: both must give the list of
        // ParsingCore.SeparatesWords, for every known tag, in any case, opening and closing.
        foreach (HtmlTag tag in Enum.GetValues<HtmlTag>())
        {
            if ((int)tag < 0 || (int)tag >= Core.ParsingCore.TagNames.Length) continue;
            var name = Core.ParsingCore.TagNames[(int)tag];
            if (string.IsNullOrEmpty(name) || !name.All(char.IsAsciiLetterOrDigit)) continue;
            bool expected = Core.ParsingCore.SeparatesWords(tag);
            foreach (var markup in new[] { $"<{name}>", $"</{name}>", $"<{name.ToUpperInvariant()} class=x>", $"<{name}/>" })
                Assert.AreEqual(expected, Core.HtmlToTextCore.SeparatesWords(markup, 0), markup);
        }
        Assert.IsFalse(Core.HtmlToTextCore.SeparatesWords("<my-widget>", 0));
        Assert.IsFalse(Core.HtmlToTextCore.SeparatesWords("<!-- x -->", 0));
    }

    [TestMethod]
    public void EveryWhitespaceChar_Collapses()
    {
        // the quick check for "is there whitespace to collapse" has its own list of chars: it must be char.IsWhiteSpace
        for (int c = 0; c <= char.MaxValue; c++)
        {
            if (!char.IsWhiteSpace((char)c) || c == ' ') continue;
            Assert.AreEqual("a b", new HtmlParser($"<span>a{(char)c}b</span>").Find(HtmlTag.Span).Text(), $"U+{c:X4}");
            Assert.AreEqual("a b", new HtmlParser($"<span>a {(char)c}b</span>").Find(HtmlTag.Span).Text(), $"space + U+{c:X4}");
        }
        Assert.AreEqual("a b c", new HtmlParser("<span>a b c</span>").Find(HtmlTag.Span).Text());
    }

    [TestMethod]
    public void Pre_KeepsItsWhitespace()
    {
        var doc = new HtmlParser("<div>before<pre>line 1\n  line 2</pre>after</div><pre><code>x = 1\n\ny = 2</code></pre>");
        Assert.AreEqual("line 1\n  line 2", doc.Find(HtmlTag.Pre).Text());
        Assert.AreEqual("x = 1\n\ny = 2", doc.Find(HtmlTag.Code).Text());
        Assert.AreEqual("x = 1\n\ny = 2", doc.FindAll(HtmlTag.Pre).Last().Text());
        Assert.AreEqual("before line 1\n  line 2 after", doc.Find(HtmlTag.Div).Text());
    }

    [TestMethod]
    public void RawTextTags_AreTheirContentAsWritten()
    {
        var doc = new HtmlParser("<textarea>a\n  b</textarea><script>if (a)\n  b();</script>");
        Assert.AreEqual("a\n  b", doc.Find(HtmlTag.Textarea).Text());
        Assert.AreEqual("if (a)\n  b();", doc.Find(HtmlTag.Script).Text());
    }

    [TestMethod]
    [DataRow("line1<br>line2")]
    [DataRow("<table><tr><td>Price</td><td>100</td></tr></table>")]
    [DataRow("a<span>b</span>")]
    [DataRow("a\n b")]
    [DataRow("x<pre>a\n  b</pre>y")]
    public void IsText_AndTheQueries_AgreeWithText(string html)
    {
        var doc = new HtmlParser("<div>" + html + "</div>");
        var div = doc.Find(HtmlTag.Div)!;
        var text = div.Text()!;
        Assert.IsTrue(div.IsText(text), text);
        Assert.IsFalse(div.IsText(text + "x"));
        // XPath string-value and CSS :contains() read the same text
        Assert.AreEqual(text, doc.XPathValue("string(//div)"));
        if (!text.Contains('\n')) Assert.HasCount(1, doc.Css($"div:contains('{text}')"));
    }

    [TestMethod]
    public void HtmlTextToText_IsTheSame() =>
        Assert.AreEqual("Price 100 Total 5", HtmlText.ToText("<tr><td>Price</td><td>100</td></tr><tr><td>Total</td><td>5</td></tr>"));

    [TestMethod]
    public void EveryTagOfTheFixtures_IsTextAgreesWithText()
    {
        int checkedTags = 0;
        foreach (var path in new[] { "books/index.html", "quotes/page1.html", "webscraper/tables.html", "webscraper/laptops.html", "books/product-light-in-the-attic.html" })
        {
            foreach (var tag in Fixtures.Load(path))
            {
                var text = tag.Text();
                if (string.IsNullOrEmpty(text)) continue;
                Assert.IsTrue(tag.IsText(text), $"{path} <{tag.Name}>: {text}");
                checkedTags++;
            }
        }
        Assert.IsGreaterThan(500, checkedTags);
    }
}
