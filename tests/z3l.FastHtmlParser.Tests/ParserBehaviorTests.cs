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

    // Markup whose name starts with '!' or '?' contains nothing, in HTML (a bogus comment in a browser) and in XML
    // (a declaration or processing instruction is a leaf): it does not nest the following tags, no end tag is matched to it.

    [TestMethod]
    public void XmlDeclaration_DoesNotNestTheDocument()
    {
        var doc = new HtmlParser("""<?xml version="1.0" encoding="UTF-8"?><urlset><url><loc>a</loc></url><url><loc>b</loc></url></urlset>""");
        var declaration = doc.FindByName(HtmlTag.XmlDeclaration)!;
        var urlset = doc.Find("urlset")!;

        Assert.IsFalse(declaration.HasChildren());
        Assert.IsEmpty(declaration.Children());
        Assert.IsNull(urlset.Parent());
        Assert.AreEqual(0, urlset.Depth);
        Assert.AreEqual(2, doc.Find("loc")!.Depth);
        CollectionAssert.AreEqual(new[] { "a", "b" }, doc.XPath("/urlset/url/loc").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b" }, doc.Css("urlset > url > loc").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "urlset" }, doc.Css(":root").Select(t => t.Name).ToArray());
        Assert.HasCount(2, doc.XPath("//loc"));
    }

    [TestMethod]
    public void XhtmlDeclarationAndDoctype_DoNotNestTheDocument()
    {
        var doc = new HtmlParser("""<?xml version="1.0"?><!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Strict//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd"><html><head><title>t</title></head><body><p>a</p></body></html>""");
        var html = doc.FindByName(HtmlTag.Html)!;

        Assert.IsNull(doc.FindByName(HtmlTag.Doctype)!.Parent());
        Assert.IsFalse(doc.FindByName(HtmlTag.Doctype)!.HasChildren());
        Assert.IsFalse(doc.FindByName(HtmlTag.XmlDeclaration)!.HasChildren());
        Assert.IsNull(html.Parent());
        Assert.AreEqual(0, html.Depth);
        CollectionAssert.AreEqual(new[] { "head", "body" }, html.Children().Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, doc.XPath("/html/body/p").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "t" }, doc.XPath("/html/head/title").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, doc.Css("html > body > p").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "html" }, doc.Css(":root").Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public void DownlevelRevealedConditionals_DoNotNestTheFollowingTags()
    {
        var doc = new HtmlParser("""<html><head><![if !IE]><link rel="stylesheet" href="a.css"><![endif]><title>x</title></head><body></body></html>""");
        var head = doc.FindByName(HtmlTag.Head)!;

        CollectionAssert.AreEqual(new[] { "![if", "link", "![endif]", "title" }, head.Children().Select(t => t.Name).ToArray());
        Assert.IsTrue(head.Children().All(t => !t.HasChildren()));
        Assert.AreSame(head, doc.FindByName(HtmlTag.Link)!.Parent());
        Assert.AreSame(head, doc.FindByName(HtmlTag.Title)!.Parent());
        Assert.AreEqual(2, doc.FindByName(HtmlTag.Title)!.Depth);
        CollectionAssert.AreEqual(new[] { "x" }, doc.Css("head > title").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "x" }, doc.XPath("/html/head/title").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "a.css" }, doc.XPath("/html/head/link").Select(t => t.AttributeByName(HtmlAttribute.Href)).ToArray());
    }

    [TestMethod]
    public void ProcessingInstruction_InHtml_DoesNotNestTheFollowingTags()
    {
        var doc = new HtmlParser("<html><body><p>a</p><?foo bar?><p>b</p><p>c</p></body></html>");
        var body = doc.FindByName(HtmlTag.Body)!;

        Assert.IsFalse(doc.Find("?foo")!.HasChildren());
        Assert.AreSame(body, doc.Find("?foo")!.Parent());
        Assert.IsTrue(doc.FindAll(HtmlTag.P).All(p => ReferenceEquals(p.Parent(), body) && p.Depth == 2));
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, doc.Css("body > p").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, doc.XPath("/html/body/p").Select(t => t.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "html" }, doc.Css(":root").Select(t => t.Name).ToArray());
    }

    [TestMethod]
    [DataRow("<div><?php echo 1; ?><p>a</p></?php></div><p>b</p>")]
    [DataRow("<?xml version=\"1.0\"?><r><?xml-stylesheet href=\"s.xsl\"?><p>a</p></?xml-stylesheet></r><p>b</p>")]
    [DataRow("<div><!ELEMENT x ANY><p>a</p></!ELEMENT></div><p>b</p>")]
    public void BangAndQuestionMarkup_IsNotMatchedToAnEndTag(string html)
    {
        var doc = new HtmlParser(html);
        var paragraphs = doc.FindAll(HtmlTag.P).ToList();

        Assert.HasCount(2, paragraphs);
        Assert.AreEqual(1, paragraphs[0].Depth); // in the <div> / <r>, not inside the "?..." / "!..." tag
        Assert.AreEqual(0, paragraphs[1].Depth);
        Assert.IsTrue(doc.Where(t => t.Name![0] is '!' or '?').All(t => !t.HasChildren()));
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
    public void SelfClosingTags_WithoutASpace_KeepTheirName()
    {
        var doc = new HtmlParser("<div>a<br/>b<hr/><svg><path/><use href=\"#x\"/></svg><br />c<my-icon/></div>");

        Assert.AreEqual(2, doc.FindAll(HtmlTag.Br).Count());
        Assert.AreEqual(2, doc.FindAll("br").Count());
        Assert.HasCount(2, doc.XPath("//br"));
        Assert.AreEqual(1, doc.FindAll(HtmlTag.Hr).Count());
        Assert.AreEqual(1, doc.FindAll(HtmlTag.Path).Count());
        Assert.AreEqual("my-icon", doc.Find("my-icon")!.Name);
        Assert.IsTrue(doc.All(tag => !tag.Name!.Contains('/')));
        Assert.IsTrue(doc.FindAll(HtmlTag.Br).All(br => br.Parent().IsName(HtmlTag.Div)));

        var rss = new HtmlParser("<?xml version=\"1.0\"?><rss><channel><item/><item><title>t</title></item></channel></rss>");
        Assert.AreEqual(2, rss.FindAll(HtmlTag.Item).Count());
        Assert.AreEqual("t", rss.FindAll(HtmlTag.Item).Last().Text());
    }

    [TestMethod]
    [DataRow("<a></a    >")]   // the padding made the position equal to the length of the name
    [DataRow("<a></a   >")]
    [DataRow("<a>x</a\n>")]
    public void EndTag_MayHaveWhitespaceBeforeTheBracket(string html)
    {
        var doc = new HtmlParser(html);

        Assert.HasCount(1, doc);
        Assert.AreEqual(html.Length - 1, doc.First().End);
    }

    [TestMethod]
    public void EndTag_IsMatchedAgain_WhenTheRulesEndedItsTag()
    {
        // the second <li> ends the first one together with its <div>, as in a browser: </div> has nothing to end
        var doc = new HtmlParser("<ul><li>one<div><li>two<span>x<li>three</div></ul><p>after</p>");
        var ul = doc.Find(HtmlTag.Ul)!;
        var div = doc.Find(HtmlTag.Div)!;

        CollectionAssert.AreEqual(new[] { "one", "twox", "three" }, doc.FindAll(HtmlTag.Li).Select(li => li.Text()).ToArray());
        Assert.IsTrue(doc.FindAll(HtmlTag.Li).All(li => ReferenceEquals(li.Parent(), ul)));
        Assert.AreEqual("", div.Body());
        Assert.IsLessThan(doc.FindAll(HtmlTag.Li).ElementAt(1).Start, div.End);
        Assert.AreEqual(0, doc.Find(HtmlTag.P)!.Depth);
    }

    [TestMethod]
    public void SelfClosedTag_IsNotEndedAfterItsParent()
    {
        // </span> comes after </b>: a browser ignores it, the <span/> stays inside <b>
        var doc = new HtmlParser("<b><span />x</b>y</span><i>z</i>");
        var b = doc.Find(HtmlTag.B)!;
        var span = doc.Find(HtmlTag.Span)!;

        Assert.AreSame(b, span.Parent());
        Assert.IsLessThan(b.End, span.End);
        Assert.AreEqual("x", b.Text());
        Assert.AreEqual(0, doc.Find(HtmlTag.I)!.Depth);
    }

    [TestMethod]
    [DataRow("<!--<")]                 // an unfinished comment ends with the source
    [DataRow("<p>a <<div>x</div>")]    // the <p> is ended by the <div>: its last char is a '<'
    [DataRow("<b>x</b>")]
    public void TextAfterTagUntilNextOne_NothingAfterTheTag(string html)
    {
        Assert.AreEqual("", new HtmlParser(html).First().TextAfterTagUntilNextOne());
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
        CollectionAssert.AreEqual(new[] { "one", "two" }, doc.FindAll(HtmlTag.Li).Select(li => li.Text()).ToArray());
        Assert.IsTrue(doc.FindAll(HtmlTag.Li).All(li => li.Parent().IsName(HtmlTag.Ul)));
    }

    [TestMethod]
    public void OptionalEndTags_AndTheEndOfTheDocument_CloseTags()
    {
        var doc = new HtmlParser("<table><tr><td>A<td>B<tr><td>C</table><dl><dt>Price<dd>100</dl><p>a<div>b</div><span>cut");

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, doc.FindAll(HtmlTag.Td).Select(td => td.Text()).ToArray());
        Assert.AreEqual(2, doc.FindAll(HtmlTag.Tr).Count());
        Assert.AreEqual("100", doc.Find(HtmlTag.Dt, text: "Price").NextSibling(HtmlTag.Dd).Text());
        Assert.AreEqual("a", doc.Find(HtmlTag.P).Text());
        Assert.AreEqual(0, doc.Find(HtmlTag.Div)!.Depth);
        Assert.AreEqual("cut", doc.Find(HtmlTag.Span).Text());
    }

    [TestMethod]
    public void RepairedParts_KeepTheTagListConsistent()
    {
        var doc = new HtmlParser(
            "<div id=a><ul><li>1<li>2</ul><table><tr><td>x<td>y</table></div><div id=b><p>p1<p>p2</div><ol><li>3<li>4");
        var tags = doc.ToList();

        for (int k = 0; k < tags.Count; k++)
        {
            // document order, and navigation by index still works
            if (k > 0) Assert.IsLessThan(tags[k].Start, tags[k - 1].Start);
            if (k + 1 < tags.Count) Assert.AreSame(tags[k], tags[k].Next().Previous());
            Assert.IsGreaterThanOrEqualTo(tags[k].Start, tags[k].End);
        }

        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" }, doc.FindAll(HtmlTag.Li).Select(li => li.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "x", "y" }, doc.FindAll(HtmlTag.Td).Select(td => td.Text()).ToArray());
        CollectionAssert.AreEqual(new[] { "p1", "p2" }, doc.Find(HtmlTag.Div, id: "b").FindAll(HtmlTag.P).Select(p => p.Text()).ToArray());
        Assert.AreEqual(0, doc.Find(HtmlTag.Div, id: "b")!.Depth);
        Assert.AreEqual(0, doc.Find(HtmlTag.Ol)!.Depth);
        Assert.IsTrue(doc.FindAll(HtmlTag.Li).All(li => li.Depth == li.Parent()!.Depth + 1));
    }

    [TestMethod]
    public void TwoMistakesAtOnce_AreRepaired()
    {
        // the second <option>/<li> does not see its open sibling on top of the stack (a <b>/<i> is open inside it)
        var select = new HtmlParser("<select><option value=1><b>One<option value=2>Two</select><span>s</span>");
        CollectionAssert.AreEqual(new[] { "One", "Two" }, select.FindAll(HtmlTag.Option).Select(o => o.Text()).ToArray());
        Assert.IsTrue(select.FindAll(HtmlTag.Option).All(o => o.Parent().IsName(HtmlTag.Select)));
        Assert.AreEqual(0, select.Find(HtmlTag.Span)!.Depth);

        var list = new HtmlParser("<ol><li><i>a<li>b<li><b>c<li>d");
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, list.FindAll(HtmlTag.Li).Select(li => li.Text()).ToArray());
        Assert.IsTrue(list.FindAll(HtmlTag.Li).All(li => li.Parent().IsName(HtmlTag.Ol)));
    }

    [TestMethod]
    public void BlockElement_EndsAP_EvenWhenTheMarkupIsBalanced()
    {
        // balanced, so the stack shows nothing wrong; a browser still ends the <p> at the <div>
        // (and turns the stray </p> into an empty <p>, which this parser does not create)
        var doc = new HtmlParser("<p>text<div>block</div></p><span>s</span>");

        Assert.AreEqual("text", doc.Find(HtmlTag.P).Text());
        Assert.AreEqual(0, doc.Find(HtmlTag.Div)!.Depth);
        Assert.AreEqual(0, doc.Find(HtmlTag.Span)!.Depth);
    }

    [TestMethod]
    public void LessThanSign_NotFollowedByANameIsText()
    {
        var doc = new HtmlParser("<p>if a < b and c > d, 1 <3</p><div>x</span></div>");

        Assert.AreEqual(2, doc.Count());
        Assert.AreEqual("if a < b and c > d, 1 <3", doc.Find(HtmlTag.P).Text());
        Assert.IsTrue(doc.Find(HtmlTag.P).IsText("if a < b and c > d, 1 <3"));
        Assert.AreEqual("x", doc.Find(HtmlTag.Div).Text());
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
    public void LineBreaks_AreReadAsInABrowser()
    {
        // CR LF and a lone CR are LF in values, as a browser reads the page (a page saved on Windows);
        // a CR written as a character reference stays.
        var doc = new HtmlParser("<meta name=\"d\" content=\"one\r\ntwo\rthree&#13;four\">" +
                                 "<textarea>a\r\nb</textarea><pre>c\r\nd</pre><div class=\"x\r\ny\">t</div>");

        Assert.AreEqual("one\ntwo\nthree\rfour", doc.Find(HtmlTag.Meta).AttributeByName(HtmlAttribute.Content));
        Assert.AreEqual("a\nb", doc.Find(HtmlTag.Textarea).Text());
        Assert.AreEqual("c\nd", doc.Find(HtmlTag.Pre).Text());
        Assert.AreEqual("a\nb", doc.XPathValue("//textarea/text()"));
        Assert.AreEqual("one\ntwo\nthree\rfour", doc.XPathValue("//meta/@content"));
        Assert.IsTrue(doc.Find(HtmlTag.Div).IsClass("y"));
        Assert.IsTrue(doc.Find(HtmlTag.Div).IsAttribute(HtmlAttribute.Class, "x\ny"));
        Assert.HasCount(1, doc.Css("div[class=\"x\\a y\"]"));
        Assert.AreEqual("x\r\ny", doc.Find(HtmlTag.Div).AttributeBody(HtmlAttribute.Class)); // the source as written
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
