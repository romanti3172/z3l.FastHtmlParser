namespace z3l.FastHtmlParser.Tests;

/// <summary>books.toscrape.com: catalogue, category with pagination, product page with a details table.</summary>
[TestClass]
public sealed class BooksToScrapeTests
{
    private static readonly string[] IndexPrices =
    [
        "£51.77", "£53.74", "£50.10", "£47.82", "£54.23", "£22.65", "£33.34", "£17.93", "£22.60", "£52.15",
        "£13.99", "£20.66", "£17.46", "£52.29", "£35.02", "£57.25", "£23.88", "£37.59", "£51.33", "£45.17",
    ];

    [TestMethod]
    public void Index_TwentyBooksWithTitlesAndPrices()
    {
        var pods = Fixtures.Load("books/index.html").FindAllByNameClass(HtmlTag.Article, "product_pod").ToList();

        Assert.HasCount(20, pods);
        CollectionAssert.AreEqual(IndexPrices, pods.Select(p => p.FindByNameClass(HtmlTag.P, "price_color").Text()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "A Light in the Attic", "Tipping the Velvet", "Soumission", "Sharp Objects" },
            pods.Take(4).Select(p => p.FindByName(HtmlTag.H3).FindByName(HtmlTag.A).AttributeTitle()).ToArray());

        // the visible text is shortened, the title attribute is not
        Assert.AreEqual("A Light in the ...", pods[0].FindByName(HtmlTag.H3).Text());
    }

    [TestMethod]
    public void Index_RatingIsOneOfTheClasses()
    {
        var pods = Fixtures.Load("books/index.html").FindAllByNameClass(HtmlTag.Article, "product_pod").ToList();

        var firstRating = pods[0].FindByNameClass(HtmlTag.P, "star-rating");
        Assert.IsTrue(firstRating.IsClass("Three"));
        Assert.IsFalse(firstRating.IsClass("One"));
        Assert.IsTrue(pods[1].FindByNameClass(HtmlTag.P, "star-rating").IsClass("one")); // case-insensitive
    }

    [TestMethod]
    public void Index_RelativeLinksAndImages()
    {
        var pod = Fixtures.Load("books/index.html").FindByNameClass(HtmlTag.Article, "product_pod");

        Assert.AreEqual("https://books.toscrape.com/catalogue/a-light-in-the-attic_1000/index.html", pod.FindByName(HtmlTag.A).AttributeHref());
        var img = pod.FindByName(HtmlTag.Img);
        Assert.AreEqual("https://books.toscrape.com/media/cache/2c/da/2cdad67c44b002e7ead0cc35693c0e8b.jpg", img.AttributeSrc());
        Assert.AreEqual("A Light in the Attic", img.AttributeAlt());
        Assert.AreEqual("thumbnail", img.AttributeClass());
    }

    [TestMethod]
    public void Category_ResultCountsAndPager()
    {
        var doc = Fixtures.Load("books/category-mystery.html");

        Assert.HasCount(20, doc.FindAllByNameClass(HtmlTag.Article, "product_pod").ToList());
        CollectionAssert.AreEqual(
            new[] { "32", "1", "20" },
            doc.FindByNameClass(HtmlTag.Form, "form-horizontal").FindAllByName(HtmlTag.Strong).Select(s => s.Text()).ToArray());
        Assert.AreEqual("Page 1 of 2", doc.FindByNameClass(HtmlTag.Li, "current").Text());
        Assert.AreEqual(
            "https://books.toscrape.com/catalogue/category/books/mystery_3/page-2.html",
            doc.FindByNameClass(HtmlTag.Li, "next").FindByName(HtmlTag.A).AttributeHref());
    }

    [TestMethod]
    public void Product_TitleAndDetailsTable()
    {
        var doc = Fixtures.Load("books/product-light-in-the-attic.html");

        Assert.AreEqual("A Light in the Attic", doc.FindByName(HtmlTag.H1).Text());

        // <tr><th>UPC</th><td>a897fe39b1053632</td></tr> ...
        string Row(string header) => doc.FindByNameText(HtmlTag.Th, header).NextSiblingByName(HtmlTag.Td).Text()!;
        Assert.AreEqual("a897fe39b1053632", Row("UPC"));
        Assert.AreEqual("Books", Row("Product Type"));
        Assert.AreEqual("£51.77", Row("Price (excl. tax)"));
        Assert.AreEqual("£51.77", Row("Price (incl. tax)"));
        Assert.AreEqual("£0.00", Row("Tax"));
        Assert.AreEqual("In stock (22 available)", Row("Availability"));
        Assert.AreEqual("0", Row("Number of reviews"));

        var rows = doc.FindByNameClass(HtmlTag.Table, "table-striped").FindAllByName(HtmlTag.Tr).ToList();
        Assert.HasCount(7, rows);
    }

    [TestMethod]
    public void Product_DescriptionFollowsItsHeader()
    {
        var doc = Fixtures.Load("books/product-light-in-the-attic.html");

        var description = doc.FindByNameId(HtmlTag.Div, "product_description").NextByName(HtmlTag.P).Text()!;

        StringAssert.StartsWith(description, "It's hard to imagine a world without A Light in the Attic.");
        StringAssert.EndsWith(description, "Shel, you never sounded so good. ...more");
    }

    [TestMethod]
    public void Product_Breadcrumb()
    {
        var breadcrumb = Fixtures.Load("books/product-light-in-the-attic.html").FindByNameClass(HtmlTag.Ul, "breadcrumb");

        var links = breadcrumb.FindAllByName(HtmlTag.A).ToList();
        CollectionAssert.AreEqual(new[] { "Home", "Books", "Poetry" }, links.Select(a => a.Text()).ToArray());
        Assert.AreEqual("https://books.toscrape.com/catalogue/category/books/poetry_23/index.html", links[2].AttributeHref());
        Assert.AreEqual("https://books.toscrape.com/index.html", links[0].AttributeHref());
        Assert.AreEqual("A Light in the Attic", breadcrumb.FindByNameClass(HtmlTag.Li, "active").Text());
    }
}
