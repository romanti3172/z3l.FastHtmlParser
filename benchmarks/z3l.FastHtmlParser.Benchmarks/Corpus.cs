namespace z3l.FastHtmlParser.Benchmarks;

/// <summary>Real pages from public scraping sandboxes (copies from the test fixtures).</summary>
public static class Corpus
{
    public const string Quotes = "quotes 11 KB";
    public const string Laptops = "laptops 38 KB";
    public const string Books = "books 51 KB";
    public const string BooksX10 = "books x10 510 KB";

    /// <summary>The books page with all 75 &lt;/li&gt; and 60 &lt;/p&gt; left out, as on sloppy sites.</summary>
    public const string BooksNoEndTags = "books, no </li> </p>";

    public static IEnumerable<string> Names => [Quotes, Laptops, Books, BooksX10, BooksNoEndTags, .. LocalPages("")];

    private const string LocalPrefix = "local ";

    /// <summary>
    /// Pages saved in benchmarks/local (third-party content, not in the repository) whose file name starts with
    /// <paramref name="prefix"/>, as "local amazon.html".
    /// </summary>
    public static IEnumerable<string> LocalPages(string prefix)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Local");
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, prefix + "*.html").Select(f => LocalPrefix + Path.GetFileName(f)).Order()
            : [];
    }

    public static string Get(string name) => name.StartsWith(LocalPrefix)
        ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Local", name[LocalPrefix.Length..]))
        : name switch
    {
        Quotes => Read("quotes/page1.html"),
        Laptops => Read("webscraper/laptops.html"),
        Books => Read("books/index.html"),
        BooksX10 => string.Concat(Enumerable.Repeat(Read("books/index.html"), 10)),
        BooksNoEndTags => Read("books/index.html").Replace("</li>", "").Replace("</p>", ""),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    public static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", path));
}
