namespace z3l.FastHtmlParser.Tests;

/// <summary>quotes.toscrape.com: lists, pagination, author page, tag page, table layout, login form.</summary>
[TestClass]
public sealed class QuotesToScrapeTests
{
    private static readonly string[] Page1Authors =
    [
        "Albert Einstein", "J.K. Rowling", "Albert Einstein", "Jane Austen", "Marilyn Monroe",
        "Albert Einstein", "André Gide", "Thomas A. Edison", "Eleanor Roosevelt", "Steve Martin",
    ];

    [TestMethod]
    public void Page1_HasTenQuotesWithAuthors()
    {
        var doc = Fixtures.Load("quotes/page1.html");

        var quotes = doc.FindAllByNameClass(HtmlTag.Div, "quote").ToList();

        Assert.HasCount(10, quotes);
        CollectionAssert.AreEqual(Page1Authors, quotes.Select(q => q.FindByNameClass(HtmlTag.Small, "author").Text()).ToArray());
    }

    [TestMethod]
    public void Page1_FirstQuote_TextTagsAndKeywords()
    {
        var quote = Fixtures.Load("quotes/page1.html").FindByNameClass(HtmlTag.Div, "quote");

        Assert.AreEqual(
            "“The world as we have created it is a process of our thinking. It cannot be changed without changing our thinking.”",
            quote.FindByNameClass(HtmlTag.Span, "text").Text());
        CollectionAssert.AreEqual(
            new[] { "change", "deep-thoughts", "thinking", "world" },
            quote.FindAllByNameClass(HtmlTag.A, "tag").Select(a => a.Text()).ToArray());

        // <meta ... /    > : self-closing with spaces before '>'
        Assert.AreEqual("change,deep-thoughts,thinking,world", quote.FindByNameClass(HtmlTag.Meta, "keywords").AttributeContent());
        Assert.AreEqual("text", quote.FindByNameClass(HtmlTag.Span, "text").AttributeByName(HtmlAttribute.Itemprop));
    }

    [TestMethod]
    public void Page1_LinksAreResolvedAgainstThePageUrl()
    {
        var doc = Fixtures.Load("quotes/page1.html");

        var about = doc.FindByNameClass(HtmlTag.Div, "quote").FindByNameText(HtmlTag.A, "(about)");
        Assert.AreEqual("https://quotes.toscrape.com/author/Albert-Einstein", about.AttributeHref());

        var next = doc.FindByNameClass(HtmlTag.Li, "next").FindByName(HtmlTag.A);
        Assert.AreEqual("https://quotes.toscrape.com/page/2/", next.AttributeHref());
        Assert.AreEqual("Next →", next.Text()); // &rarr; decoded
    }

    [TestMethod]
    public void Page1_TopTags()
    {
        var tags = Fixtures.Load("quotes/page1.html")
            .FindByNameClass(HtmlTag.Div, "tags-box")
            .FindAllByNameClass(HtmlTag.A, "tag")
            .Select(a => a.Text())
            .ToList();

        Assert.HasCount(10, tags);
        Assert.AreEqual("love", tags[0]);
    }

    [TestMethod]
    public void Page2_FirstAuthor_AndPreviousLink()
    {
        var doc = Fixtures.Load("quotes/page2.html");

        Assert.AreEqual("Marilyn Monroe", doc.FindByNameClass(HtmlTag.Small, "author").Text());
        Assert.AreEqual("https://quotes.toscrape.com/page/1/", doc.FindByNameClass(HtmlTag.Li, "previous").FindByName(HtmlTag.A).AttributeHref());
    }

    [TestMethod]
    public void AuthorPage_Details()
    {
        var doc = Fixtures.Load("quotes/author-einstein.html");

        Assert.AreEqual("Albert Einstein", doc.FindByNameClass(HtmlTag.H3, "author-title").Text());
        Assert.AreEqual("March 14, 1879", doc.FindByNameClass(HtmlTag.Span, "author-born-date").Text());
        Assert.AreEqual("in Ulm, Germany", doc.FindByNameClass(HtmlTag.Span, "author-born-location").Text());
        StringAssert.StartsWith(
            doc.FindByNameClass(HtmlTag.Div, "author-description").Text(),
            "In 1879, Albert Einstein was born in Ulm, Germany.");

        // <p><strong>Born:</strong> <span>...</span> <span>...</span></p>
        Assert.AreEqual("Born: March 14, 1879 in Ulm, Germany", doc.FindByNameText(HtmlTag.Strong, "Born:").Parent().Text());
    }

    [TestMethod]
    public void TagPage_EveryQuoteHasTheTag()
    {
        var quotes = Fixtures.Load("quotes/tag-love.html").FindAllByNameClass(HtmlTag.Div, "quote").ToList();

        Assert.HasCount(10, quotes);
        foreach (var quote in quotes)
        {
            CollectionAssert.Contains(quote.FindAllByNameClass(HtmlTag.A, "tag").Select(a => a.Text()).ToList(), "love");
        }
    }

    [TestMethod]
    public void TableLayout_QuotesAreInCells()
    {
        var doc = Fixtures.Load("quotes/tableful.html");

        var quoteCells = doc.FindAllByName(HtmlTag.Td)
            .Where(td => td.AttributeStyle().Contains("padding-top: 2em"))
            .Select(td => td.Text())
            .ToList();

        Assert.HasCount(10, quoteCells);
        StringAssert.StartsWith(quoteCells[0], "“The world as we have created it");
        Assert.AreEqual("https://quotes.toscrape.com/tableful/tag/love/page/1/", doc.FindByNameText(HtmlTag.A, "love").AttributeHref());
    }

    [TestMethod]
    public void LoginForm_Fields()
    {
        var form = Fixtures.Load("quotes/login.html").FindByName(HtmlTag.Form);

        Assert.AreEqual("/login", form.AttributeByName(HtmlAttribute.Action));
        Assert.AreEqual("post", form.AttributeByName(HtmlAttribute.Method));
        Assert.AreEqual("utf-8", form.AttributeByName(HtmlAttribute.AcceptCharset));

        Assert.IsFalse(string.IsNullOrEmpty(form.FindByNameAttribute(HtmlTag.Input, HtmlAttribute.Name, "csrf_token").AttributeValue()));
        Assert.AreEqual("password", form.FindByNameId(HtmlTag.Input, "password").AttributeByName(HtmlAttribute.Type));
        Assert.AreEqual("Login", form.FindByNameAttribute(HtmlTag.Input, HtmlAttribute.Type, "submit").AttributeValue());
        Assert.AreEqual(4, form.FindAllByName(HtmlTag.Input).Count());
    }
}
