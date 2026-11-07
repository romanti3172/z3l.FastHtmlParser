namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// Saved copies of public scraping sandboxes (quotes.toscrape.com, books.toscrape.com,
/// webscraper.io/test-sites), so the tests do not depend on the network or on site changes.
/// </summary>
internal static class Fixtures
{
    public static readonly (string Path, string Url)[] All =
    [
        ("quotes/page1.html", "https://quotes.toscrape.com/"),
        ("quotes/page2.html", "https://quotes.toscrape.com/page/2/"),
        ("quotes/author-einstein.html", "https://quotes.toscrape.com/author/Albert-Einstein/"),
        ("quotes/tag-love.html", "https://quotes.toscrape.com/tag/love/"),
        ("quotes/tableful.html", "https://quotes.toscrape.com/tableful/"),
        ("quotes/login.html", "https://quotes.toscrape.com/login"),
        ("books/index.html", "https://books.toscrape.com/"),
        ("books/category-mystery.html", "https://books.toscrape.com/catalogue/category/books/mystery_3/index.html"),
        ("books/product-light-in-the-attic.html", "https://books.toscrape.com/catalogue/a-light-in-the-attic_1000/index.html"),
        ("webscraper/allinone.html", "https://webscraper.io/test-sites/e-commerce/allinone"),
        ("webscraper/laptops.html", "https://webscraper.io/test-sites/e-commerce/static/computers/laptops"),
        ("webscraper/product-31.html", "https://webscraper.io/test-sites/e-commerce/static/product/31"),
        ("webscraper/tables.html", "https://webscraper.io/test-sites/tables"),
        ("webscraper/pagination.html", "https://webscraper.io/test-sites/pagination"),
    ];

    public static string Read(string path) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", path));

    public static HtmlParser Load(string path) =>
        new(Read(path), All.First(f => f.Path == path).Url);
}
