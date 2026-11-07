namespace z3l.FastHtmlParser.Benchmarks;

/// <summary>Real pages from public scraping sandboxes (copies from the test fixtures).</summary>
public static class Corpus
{
    public const string Quotes = "quotes 11 KB";
    public const string Laptops = "laptops 38 KB";
    public const string Books = "books 51 KB";
    public const string BooksX10 = "books x10 510 KB";

    public static IEnumerable<string> Names => [Quotes, Laptops, Books, BooksX10];

    public static string Get(string name) => name switch
    {
        Quotes => Read("quotes/page1.html"),
        Laptops => Read("webscraper/laptops.html"),
        Books => Read("books/index.html"),
        BooksX10 => string.Concat(Enumerable.Repeat(Read("books/index.html"), 10)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    public static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", path));
}
