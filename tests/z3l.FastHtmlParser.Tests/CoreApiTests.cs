namespace z3l.FastHtmlParser.Tests;

/// <summary>The core API: TagQuery with Find / FindAll / Is and the navigation methods.</summary>
[TestClass]
public sealed class CoreApiTests
{
    private const string Html = """
        <div id="main" class="page">
          <ul class="list">
            <li class="item first" data-id="1"><a href="/a" title="It&#39;s A">A</a></li>
            <li class="item" data-id="2"><a href="/b">B</a></li>
            <li class="item last" data-id="3"><span>C</span> text</li>
          </ul>
          <my-widget data-id="9">w</my-widget>
        </div>
        """;

    [TestMethod]
    public void Find_WithNamedCriteria()
    {
        var doc = new HtmlParser(Html);

        Assert.AreEqual("2", doc.Find(HtmlTag.Li, cls: "item", text: "B").AttributeByName("data-id"));
        Assert.AreEqual("main", doc.Find(HtmlTag.Div, id: "main").AttributeByName(HtmlAttribute.Id));
        Assert.AreEqual("9", doc.Find("MY-WIDGET").AttributeByName("data-id"));
        Assert.AreEqual("ul", doc.Find(null, cls: "list")!.Name); // any tag
        Assert.IsNull(doc.Find(HtmlTag.Li, cls: "missing"));
    }

    [TestMethod]
    public void TagQuery_EveryCriterion()
    {
        var doc = new HtmlParser(Html);

        Assert.AreEqual("3", doc.Find(new TagQuery(HtmlTag.Li) { Class = "last" }).AttributeByName("data-id"));
        Assert.AreEqual("main", doc.Find(new TagQuery { IdStartsWith = "ma" }).AttributeByName(HtmlAttribute.Id));
        Assert.AreEqual("/a", doc.Find(new TagQuery(HtmlTag.A) { Attribute = HtmlAttribute.Title, AttributeValue = "It's A" }).AttributeByName(HtmlAttribute.Href));
        Assert.AreEqual("2", doc.Find(new TagQuery(HtmlTag.Li) { AttributeName = "data-id", AttributeValue = "2" }).AttributeByName("data-id"));
        Assert.AreEqual("3", doc.Find(new TagQuery(HtmlTag.Li) { Text = "C text" }).AttributeByName("data-id"));
        Assert.AreEqual("3", doc.Find(new TagQuery(HtmlTag.Li) { TextStartsWith = "C" }).AttributeByName("data-id"));
        Assert.AreEqual("1", doc.Find(new TagQuery(HtmlTag.Li) { BodyStartsWith = "<a href=\"/a\"" }).AttributeByName("data-id"));
        Assert.AreEqual("B", doc.Find(new TagQuery(HtmlTag.A) { Body = "B" }).Text());
        Assert.AreEqual("B", doc.Find(new TagQuery(HtmlTag.A) { Source = "a href=\"/b\"" }).Text());

        // all criteria together
        var query = new TagQuery(HtmlTag.Li) { Class = "item", AttributeName = "data-id", AttributeValue = "1", TextStartsWith = "A" };
        Assert.AreEqual("1", doc.Find(query).AttributeByName("data-id"));
        Assert.IsNull(doc.Find(query with { AttributeValue = "3" })); // "3" has no text starting with "A"
    }

    [TestMethod]
    public void DefaultQuery_MatchesAnyTag()
    {
        var doc = new HtmlParser(Html);

        Assert.AreEqual("div", doc.Find(default(TagQuery))!.Name);
        Assert.AreEqual("div", doc.Find(new TagQuery())!.Name);
        Assert.AreEqual(doc.Count(), doc.FindAll(default(TagQuery)).Count());
        Assert.IsTrue(doc.Find(HtmlTag.Span).Is(default(TagQuery)));
    }

    [TestMethod]
    public void FindAll_AndScope()
    {
        var doc = new HtmlParser(Html);
        var ul = doc.Find(HtmlTag.Ul)!;

        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, doc.FindAll(HtmlTag.Li, cls: "item").Select(li => li.AttributeByName("data-id")).ToArray());
        Assert.AreEqual(2, ul.FindAll(HtmlTag.A).Count());              // inside the tag only
        Assert.AreEqual(0, ul.FindAll("my-widget").Count());
        Assert.AreEqual(0, ((ITagSearchable?)null).FindAll(HtmlTag.A).Count());
    }

    [TestMethod]
    public void Is_WithQueryAndNamedCriteria()
    {
        var li = new HtmlParser(Html).Find(HtmlTag.Li)!;

        Assert.IsTrue(li.Is(HtmlTag.Li, cls: "first"));
        Assert.IsTrue(li.Is("li", cls: "item", text: "A"));
        Assert.IsTrue(li.Is(new TagQuery { AttributeName = "data-id", AttributeValue = "1" }));
        Assert.IsFalse(li.Is(HtmlTag.Li, cls: "last"));
        Assert.IsFalse(((ParserTag?)null).Is(HtmlTag.Li));
    }

    [TestMethod]
    public void Navigation_WithQuery()
    {
        var doc = new HtmlParser(Html);
        var span = doc.Find(HtmlTag.Span)!;
        var first = doc.Find(HtmlTag.Li, cls: "first")!;

        Assert.AreEqual("main", span.Parent(new TagQuery(HtmlTag.Div)).AttributeByName(HtmlAttribute.Id));
        Assert.AreEqual("3", first.NextSibling(new TagQuery(HtmlTag.Li) { Class = "last" }).AttributeByName("data-id"));
        Assert.AreEqual("my-widget", first.Next(new TagQuery("my-widget"))!.Name);
        Assert.AreEqual("1", doc.Find(HtmlTag.Li, cls: "last").PreviousSibling(new TagQuery(HtmlTag.Li) { Class = "first" }).AttributeByName("data-id"));
        StringAssert.StartsWith(first.TextAfterUntil(new TagQuery(HtmlTag.Span))!.Trim(), "<li class=\"item\"");
    }

    [TestMethod]
    public void Navigation_WithPlainHtmlTag()
    {
        var doc = new HtmlParser(Html);
        var span = doc.Find(HtmlTag.Span)!;
        var first = doc.Find(HtmlTag.Li, cls: "first")!;
        var last = doc.Find(HtmlTag.Li, cls: "last")!;

        // HtmlTag converts to TagQuery implicitly
        Assert.AreSame(span.Parent(new TagQuery(HtmlTag.Div)), span.Parent(HtmlTag.Div));
        Assert.AreSame(first.NextSibling(new TagQuery(HtmlTag.Li)), first.NextSibling(HtmlTag.Li));
        Assert.AreSame(last.PreviousSibling(new TagQuery(HtmlTag.Li)), last.PreviousSibling(HtmlTag.Li));
        Assert.AreSame(first.Next(new TagQuery(HtmlTag.Span)), first.Next(HtmlTag.Span));
        Assert.AreEqual(first.TextAfterUntil(new TagQuery(HtmlTag.Span)), first.TextAfterUntil(HtmlTag.Span));
        Assert.IsNotNull(first.NextSibling(HtmlTag.Li));
    }

    [TestMethod]
    public void Navigation_WithPlainName()
    {
        var doc = new HtmlParser(Html);
        var span = doc.Find(HtmlTag.Span)!;
        var first = doc.Find(HtmlTag.Li, cls: "first")!;
        var last = doc.Find(HtmlTag.Li, cls: "last")!;

        // a string converts to TagQuery implicitly, case-insensitive, custom names included
        Assert.AreSame(span.Parent(new TagQuery("div")), span.Parent("div"));
        Assert.AreSame(first.NextSibling(HtmlTag.Li), first.NextSibling("LI"));
        Assert.AreSame(last.PreviousSibling(HtmlTag.Li), last.PreviousSibling("li"));
        Assert.AreEqual("my-widget", first.Next("my-widget")!.Name);
        Assert.AreEqual(first.TextAfterUntil(HtmlTag.Span), first.TextAfterUntil("span"));
        Assert.IsNull(first.NextSibling("no-such-tag"));

        // string overloads of Find / FindAll keep winning over the conversion
        Assert.AreSame(doc.Find(HtmlTag.Span), doc.Find("span"));
        Assert.AreEqual(doc.FindAll(HtmlTag.Li).Count(), doc.FindAll("li").Count());
    }

    [TestMethod]
    public void Children_AreTheDirectChildrenOnly()
    {
        var doc = new HtmlParser(Html);

        CollectionAssert.AreEqual(new[] { "ul", "my-widget" }, doc.Find(HtmlTag.Div).Children().Select(c => c.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "li", "li", "li" }, doc.Find(HtmlTag.Ul).Children().Select(c => c.Name).ToArray());
        Assert.AreEqual(0, doc.Find(HtmlTag.Span).Children().Count());
    }

    [TestMethod]
    public void PreviousSibling_SkipsDescendantsOfThePreviousSibling()
    {
        // the previous sibling has children: they are deeper and must be skipped
        var doc = new HtmlParser("<ul><li>1<b>x</b></li><li>2</li></ul><p>outside</p>");
        var second = doc.FindAll(HtmlTag.Li).Last();

        Assert.AreEqual("1x", second.PreviousSibling().Text());
        Assert.IsNull(doc.Find(HtmlTag.Li).PreviousSibling());     // the first child has none
        Assert.IsNull(doc.Find(HtmlTag.B).PreviousSibling());      // its parent is not a sibling
        Assert.AreEqual("ul", doc.Find(HtmlTag.P).PreviousSibling()!.Name);
    }

    [TestMethod]
    public void TextAfterTagUntilNextOne_AtTheEndOfTheDocument()
    {
        var doc = new HtmlParser("<b>x</b> tail text");

        Assert.AreEqual(" tail text", doc.Find(HtmlTag.B).TextAfterTagUntilNextOne());
    }
}
