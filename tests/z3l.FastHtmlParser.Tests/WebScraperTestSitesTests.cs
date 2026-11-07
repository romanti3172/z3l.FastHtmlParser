namespace z3l.FastHtmlParser.Tests;

/// <summary>webscraper.io/test-sites: e-commerce cards, product page, tables, pagination.</summary>
[TestClass]
public sealed class WebScraperTestSitesTests
{
    [TestMethod]
    public void Laptops_ProductCards()
    {
        var cards = Fixtures.Load("webscraper/laptops.html").FindAllByNameClass(HtmlTag.Div, "thumbnail").ToList();

        Assert.HasCount(6, cards);
        CollectionAssert.AreEqual(
            new[] { "Packard 255 G2", "Aspire E1-510", "ThinkPad T540p", "ProBook", "ThinkPad X240", "Aspire E1-572G" },
            cards.Select(c => c.FindByNameClass(HtmlTag.A, "title").AttributeTitle()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "$416.99", "$306.99", "$1178.99", "$739.99", "$1311.99", "$581.99" },
            cards.Select(c => c.FindByNameAttribute(HtmlTag.Span, HtmlAttribute.Itemprop, "price").Text()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "2", "3", "1", "4", "3", "1" },
            cards.Select(c => c.FindByNameAttribute(HtmlTag.Div, HtmlAttribute.Itemprop, "aggregateRating")
                               .FindAllByName(HtmlTag.P).Last().AttributeByName("data-rating")).ToArray());
    }

    [TestMethod]
    public void Laptops_FirstCard_Details()
    {
        var card = Fixtures.Load("webscraper/laptops.html").FindByNameClass(HtmlTag.Div, "thumbnail");

        var title = card.FindByNameClass(HtmlTag.A, "title");
        Assert.AreEqual("Packard 255 G2", title.Text()); // surrounded by newlines and tabs in the source
        Assert.AreEqual("https://webscraper.io/test-sites/e-commerce/static/product/31", title.AttributeHref());
        Assert.AreEqual("15.6\", AMD E2-3800 1.3GHz, 4GB, 500GB, Windows 8.1", card.FindByNameClass(HtmlTag.P, "description").Text()); // &quot;
        Assert.AreEqual("2 reviews", card.FindByNameClass(HtmlTag.P, "review-count").Text());
        Assert.AreEqual("USD", card.FindByNameAttribute(HtmlTag.Meta, HtmlAttribute.Itemprop, "priceCurrency").AttributeContent());
        Assert.AreEqual("https://webscraper.io/images/test-sites/e-commerce/items/cart2.png", card.FindByName(HtmlTag.Img).AttributeSrc());
    }

    [TestMethod]
    public void Laptops_Pagination()
    {
        var pager = Fixtures.Load("webscraper/laptops.html").FindByNameClass(HtmlTag.Ul, "pagination");

        Assert.AreEqual("1", pager.FindByNameClass(HtmlTag.Li, "active").Text());
        CollectionAssert.Contains(
            pager.FindAllByName(HtmlTag.A).Select(a => a.AttributeHref()).ToList(),
            "https://webscraper.io/test-sites/e-commerce/static/computers/laptops?page=2");
    }

    [TestMethod]
    public void Product_TitlePriceAndSwatches()
    {
        var doc = Fixtures.Load("webscraper/product-31.html");

        Assert.AreEqual("Packard 255 G2", doc.FindByNameClass(HtmlTag.H4, "title").Text());
        Assert.AreEqual("$416.99", doc.FindByNameClass(HtmlTag.H4, "price").FindByName(HtmlTag.Span).Text());

        var swatches = doc.FindByNameClass(HtmlTag.Div, "swatches").FindAllByName(HtmlTag.Button).ToList();
        CollectionAssert.AreEqual(new[] { "128", "256", "512", "1024" }, swatches.Select(b => b.AttributeValue()).ToArray());
        CollectionAssert.AreEqual(new[] { "128", "256", "512", "1024" }, swatches.Select(b => b.Text()).ToArray());

        // class="btn swatch\n\n\tbtn-primary active": classes separated by newlines and tabs
        Assert.IsTrue(swatches[0].IsClass("active"));
        Assert.IsTrue(swatches[0].IsClass("btn-primary"));
        Assert.IsFalse(swatches[1].IsClass("active"));

        Assert.AreEqual("HDD:", doc.FindByNameClass(HtmlTag.Label, "memory").Text());
    }

    [TestMethod]
    public void Tables_HeadersAndRows()
    {
        var tables = Fixtures.Load("webscraper/tables.html").FindAllByName(HtmlTag.Table).ToList();
        Assert.HasCount(2, tables);

        string[][] Rows(ParserTag table) => table.FindByName(HtmlTag.Tbody).FindAllByName(HtmlTag.Tr)
            .Select(tr => tr.FindAllByName(HtmlTag.Td).Select(td => td.Text()!).ToArray())
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "#", "First Name", "Last Name", "Username" },
            tables[0].FindByName(HtmlTag.Thead).FindAllByName(HtmlTag.Th).Select(th => th.Text()).ToArray());

        var first = Rows(tables[0]);
        Assert.HasCount(3, first);
        CollectionAssert.AreEqual(new[] { "1", "Mark", "Otto", "@mdo" }, first[0]);
        CollectionAssert.AreEqual(new[] { "3", "Larry", "the Bird", "@twitter" }, first[2]);

        var second = Rows(tables[1]);
        CollectionAssert.AreEqual(new[] { "6", "Tim", "Bean", "@timbean" }, second[^1]);
        Assert.IsTrue(tables[1].IsClass("table-bordered2"));
    }

    [TestMethod]
    public void Pagination_CardsAndPages()
    {
        var doc = Fixtures.Load("webscraper/pagination.html");

        var container = doc.FindByNameClass(HtmlTag.Div, "test-items-container");
        Assert.AreEqual("17", container.AttributeByName("data-last-page"));

        var cards = container.FindAllByNameClass(HtmlTag.Div, "test-sites-card").ToList();
        Assert.HasCount(6, cards);

        var first = cards[0];
        Assert.AreEqual("Mercedes-Benz W123 280E 1955", first.FindByNameClass(HtmlTag.H3, "card-title").AttributeTitle());
        Assert.AreEqual("Mercedes-Benz W123 280E 1955", first.FindByNameClass(HtmlTag.H3, "card-title").Text());
        Assert.AreEqual("Uncompromising Stuttgart saloon", first.FindByNameClass(HtmlTag.P, "description").Text());
        Assert.AreEqual("Mileage: 104 338 km", first.FindByNameText(HtmlTag.B, "Mileage:").Parent().Text());
        Assert.AreEqual("USD 228 511", first.FindByNameAttribute(HtmlTag.Span, HtmlAttribute.Itemprop, "price").Text());

        var pageLinks = doc.FindByNameClass(HtmlTag.Ul, "pagination").FindAllByNameClass(HtmlTag.A, "page-link").Select(a => a.AttributeHref()).ToList();
        Assert.AreEqual("https://webscraper.io/test-sites/pagination?page=2", pageLinks[0]);
        CollectionAssert.Contains(pageLinks, "https://webscraper.io/test-sites/pagination?page=17");
    }

    [TestMethod]
    public void AllInOne_FeaturedProducts()
    {
        var cards = Fixtures.Load("webscraper/allinone.html").FindAllByNameClass(HtmlTag.Div, "thumbnail").ToList();

        Assert.HasCount(3, cards);
        CollectionAssert.AreEqual(
            new[] { "Packard 255 G2", "Acer Aspire A315-51-33TG", "Galaxy Tab 4" },
            cards.Select(c => c.FindByNameClass(HtmlTag.A, "title").AttributeTitle()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "$416.99", "$457.38", "$233.99" },
            cards.Select(c => c.FindByNameAttribute(HtmlTag.Span, HtmlAttribute.Itemprop, "price").Text()).ToArray());
    }
}
