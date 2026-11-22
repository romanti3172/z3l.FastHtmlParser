using System.Text.Json;
using System.Text.RegularExpressions;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// <c>Article()</c>: the main content of a page and its metadata, as Mozilla's Readability finds them.
/// </summary>
[TestClass]
public sealed partial class ArticleTests
{
    private const string Paragraph =
        "The quick brown fox jumps over the lazy dog, again and again, while the reporters watch, take notes, and ask " +
        "whether the dog will ever wake up, which, as everyone knows, it never does before noon.";

    private static string Page(string head, string body) =>
        $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">{head}</head><body>{body}</body></html>";

    private static readonly string NewsBody =
        "<nav class=\"menu\"><ul><li><a href=\"/\">Home</a></li><li><a href=\"/news\">News</a></li><li><a href=\"/sport\">Sport</a></li></ul></nav>" +
        "<div class=\"sidebar\"><h3>Popular</h3><ul><li><a href=\"/a\">Another story people read</a></li><li><a href=\"/b\">And one more story</a></li></ul></div>" +
        "<div id=\"main\"><article class=\"post\">" +
        "<h1>Foxes and dogs: a story of a long morning</h1>" +
        "<p class=\"byline\">By <a rel=\"author\" href=\"/authors/jane\">Jane Doe</a></p>" +
        $"<p>{Paragraph}</p>" +
        "<p style=\"color:red\" onclick=\"track()\">A second paragraph, with <a href=\"/more\">a link</a> and <b>bold</b> text, " +
        "long enough to count as a paragraph of the story and not as a caption, as the rules say.</p>" +
        "<img data-src=\"/img/fox.jpg\" src=\"data:image/gif;base64,R0lGODlhAQABAAAAACw=\" alt=\"A fox\">" +
        "<script>var tracking = 1;</script>" +
        $"<p>{Paragraph} {Paragraph}</p>" +
        "<div class=\"share-buttons\"><a href=\"https://twitter.com/share\">Tweet</a> <a href=\"https://facebook.com/share\">Share</a></div>" +
        "<p hidden>A hidden paragraph that a reader does not see, so it is not a part of the article that is shown.</p>" +
        "</article></div>" +
        "<div id=\"comments\"><h3>Comments</h3><p>First! What a great story about a fox, I loved every word of it, really.</p></div>" +
        "<footer class=\"footer\"><p>Copyright 2026 Example News. All rights reserved. Terms of use and privacy policy.</p></footer>";

    [TestMethod]
    public void TheArticle_IsTheStory_WithoutTheRestOfThePage()
    {
        var article = new HtmlParser(Page("<title>Foxes and dogs: a story of a long morning | Example News</title>", NewsBody),
            "https://news.example.com/2026/11/foxes").Article();

        Assert.IsNotNull(article);
        var text = article.TextContent;
        StringAssert.Contains(text, "The quick brown fox jumps over the lazy dog");
        StringAssert.Contains(text, "A second paragraph");
        foreach (var notArticle in new[] { "Home", "Popular", "First!", "Copyright", "tracking", "hidden paragraph", "Tweet" })
        {
            Assert.IsFalse(text.Contains(notArticle, StringComparison.Ordinal), notArticle + " in: " + text);
        }

        // clean HTML: no scripts, handlers, styles, classes; the links and the lazy image absolute
        var content = article.Content;
        Assert.IsFalse(Regex.IsMatch(content, "onclick|style=|class=|<script", RegexOptions.IgnoreCase), content);
        StringAssert.Contains(content, "<a href=\"https://news.example.com/more\">a link</a>");
        StringAssert.Contains(content, "<img src=\"https://news.example.com/img/fox.jpg\" alt=\"A fox\">");
        StringAssert.Contains(article.Markdown, "[a link](https://news.example.com/more)");
        StringAssert.Contains(article.Markdown, "![A fox](https://news.example.com/img/fox.jpg)");

        Assert.AreEqual("Foxes and dogs: a story of a long morning", article.Title);
        Assert.AreEqual("By Jane Doe", article.Byline); // the text of the byline element, as Readability takes it
        Assert.AreEqual("en", article.Language);
        Assert.AreEqual(article.TextContent.Length, article.Length);
        StringAssert.StartsWith(article.Excerpt, "The quick brown fox");
    }

    [TestMethod]
    public void Metadata_FromJsonLd_ComesFirst()
    {
        const string jsonLd =
            "<script type=\"application/ld+json\">{\"@context\":\"https://schema.org\",\"@graph\":[{\"@type\":\"WebSite\",\"name\":\"Site\"}," +
            "{\"@type\":\"NewsArticle\",\"headline\":\"Foxes and dogs\",\"author\":[{\"@type\":\"Person\",\"name\":\"Jane Doe\"},{\"name\":\"John Roe\"}]," +
            "\"datePublished\":\"2026-11-20T08:00:00Z\",\"description\":\"What the fox did.\",\"publisher\":{\"name\":\"Example News\"}," +
            "\"image\":{\"@type\":\"ImageObject\",\"url\":\"/img/lead.jpg\"}}]}</script>";
        const string meta =
            "<meta property=\"og:title\" content=\"OG title\"><meta property=\"og:site_name\" content=\"OG site\">" +
            "<meta name=\"author\" content=\"Meta Author\"><meta property=\"og:image\" content=\"https://cdn.example.com/og.jpg\">";

        var article = new HtmlParser(Page("<title>Page title</title>" + jsonLd + meta, NewsBody), "https://news.example.com/2026/11/foxes").Article();

        Assert.IsNotNull(article);
        Assert.AreEqual("Foxes and dogs", article.Title);
        Assert.AreEqual("Jane Doe, John Roe", article.Byline);
        Assert.AreEqual("What the fox did.", article.Excerpt);
        Assert.AreEqual("Example News", article.SiteName);
        Assert.AreEqual("2026-11-20T08:00:00Z", article.PublishedTime);
        Assert.AreEqual("https://news.example.com/img/lead.jpg", article.Image);
    }

    [TestMethod]
    public void Metadata_FromMetaTags_WhenThereIsNoJsonLd()
    {
        const string meta =
            "<meta property=\"og:title\" content=\"The &amp; title\"><meta property=\"og:site_name\" content=\"Example News\">" +
            "<meta name=\"author\" content=\"Meta Author\"><meta property=\"article:published_time\" content=\"2026-11-21\">" +
            "<meta name=\"description\" content=\"A description.\"><meta name=\"twitter:image\" content=\"/t.jpg\">" +
            "<script type=\"application/ld+json\">{ broken json</script>";

        var article = new HtmlParser(Page("<title>Page title</title>" + meta, NewsBody), "https://news.example.com/x").Article();

        Assert.IsNotNull(article);
        Assert.AreEqual("The & title", article.Title);
        Assert.AreEqual("Meta Author", article.Byline);
        Assert.AreEqual("A description.", article.Excerpt);
        Assert.AreEqual("Example News", article.SiteName);
        Assert.AreEqual("2026-11-21", article.PublishedTime);
        Assert.AreEqual("https://news.example.com/t.jpg", article.Image);
    }

    [TestMethod]
    [DataRow("A long article title about foxes | Example News", "A long article title about foxes")]
    [DataRow("Example News - A long article title about foxes and dogs", "A long article title about foxes and dogs")]
    [DataRow("Example News: A long article title about foxes", "A long article title about foxes")]
    [DataRow("Short | Site", "Short | Site")]
    public void Title_WithoutTheSiteName(string title, string expected)
    {
        var article = new HtmlParser(Page($"<title>{title}</title>", $"<div><p>{Paragraph}</p><p>{Paragraph}</p></div>")).Article();

        Assert.AreEqual(expected, article?.Title);
    }

    [TestMethod]
    public void APageWithoutText_HasNoArticle()
    {
        Assert.IsNull(new HtmlParser("").Article());
        Assert.IsNull(new HtmlParser("<html><body><script>x()</script><img src=a.png></body></html>").Article());
        Assert.IsNull(((HtmlParser?)null).Article());
    }

    [TestMethod]
    public void AShortPage_StillGivesItsText()
    {
        // less than 500 chars: Readability tries again with fewer rules and takes the longest result
        var article = new HtmlParser(Page("<title>Note</title>", "<div class=\"comment\"><p>Only a short note here, in a part named comment.</p></div>")).Article();

        Assert.IsNotNull(article);
        Assert.AreEqual("Only a short note here, in a part named comment.", article.TextContent);
    }

    [TestMethod]
    public void DataTables_AreKept_LayoutListsOfLinksAreNot()
    {
        var rows = string.Concat(Enumerable.Range(1, 12).Select(i => $"<tr><td>Fox {i}</td><td>{i * 3} kg</td><td>Forest</td></tr>"));
        var body = $"<div class=\"content\"><p>{Paragraph}</p><table><tr><th>Name</th><th>Weight</th><th>Home</th></tr>{rows}</table>" +
            $"<p>{Paragraph}</p><ul class=\"related\"><li><a href=\"/1\">Related one</a></li><li><a href=\"/2\">Related two</a></li></ul></div>";

        var article = new HtmlParser(Page("<title>Foxes by weight in a forest near us</title>", body)).Article();

        Assert.IsNotNull(article);
        StringAssert.Contains(article.Content, "<th>Weight</th>");
        StringAssert.Contains(article.Markdown, "| Fox 12 | 36 kg | Forest |");
        Assert.IsFalse(article.TextContent.Contains("Related one", StringComparison.Ordinal), article.Content);
    }

    [TestMethod]
    public void IsProbablyReadable_ForAnArticle_NotForAListOfLinks()
    {
        Assert.IsTrue(new HtmlParser(Page("", NewsBody)).IsProbablyReadable());
        Assert.IsTrue(new HtmlParser(Page("", NewsBody)).IsProbablyReadable(minContentLength: 100, minScore: 5));

        var links = string.Concat(Enumerable.Range(1, 50).Select(i => $"<li><a href=\"/{i}\">Story number {i}</a></li>"));
        Assert.IsFalse(new HtmlParser(Page("", $"<ul>{links}</ul>")).IsProbablyReadable());
        Assert.IsFalse(new HtmlParser(Page("", $"<p hidden>{Paragraph}{Paragraph}{Paragraph}</p>")).IsProbablyReadable());
        Assert.IsFalse(((HtmlParser?)null).IsProbablyReadable());
    }

    [TestMethod]
    public void TheFixturePages_GiveAnArticleOrNothing_AndNeverThrow()
    {
        foreach (var (path, url) in Fixtures.All)
        {
            var article = new HtmlParser(Fixtures.Read(path), url).Article();
            if (article == null) continue;

            Assert.IsGreaterThan(0, article.Length, path);
            _ = article.Markdown;
        }
    }

    // ----- Mozilla's test pages -----

    /// <summary>
    /// The test pages of Mozilla Readability (github.com/mozilla/readability, test/test-pages; Apache License 2.0) are
    /// not in this repository. To check against them, clone that repository and set FHP_READABILITY_PAGES to its
    /// test/test-pages folder. The text of each article is compared with the expected one word by word (F1), and the
    /// metadata field by field.
    /// </summary>
    [TestMethod]
    public void MozillaTestPages_GiveTheSameArticles()
    {
        var root = Environment.GetEnvironmentVariable("FHP_READABILITY_PAGES");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            Assert.Inconclusive("FHP_READABILITY_PAGES is not set: see the summary of this test.");
            return;
        }

        double sum = 0;
        int pages = 0, fields = 0, fieldsSame = 0;
        var report = new List<string>();
        foreach (var dir in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
        {
            var source = File.ReadAllText(Path.Combine(dir, "source.html"));
            var expected = HtmlText.ToText(File.ReadAllText(Path.Combine(dir, "expected.html"))) ?? "";
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "expected-metadata.json")));

            var article = new HtmlParser(source, "http://fakehost/test/page.html").Article();
            double f1 = F1(HtmlText.ToText(article?.Content) ?? "", expected);
            sum += f1;
            pages++;
            if (f1 < 0.8) report.Add($"{Path.GetFileName(dir)}: F1 {f1:F2}");

            foreach (var (name, value) in new[]
            {
                ("title", article?.Title), ("byline", article?.Byline), ("excerpt", article?.Excerpt),
                ("siteName", article?.SiteName), ("publishedTime", article?.PublishedTime), ("lang", article?.Language),
            })
            {
                if (!metadata.RootElement.TryGetProperty(name, out var want) || want.ValueKind != JsonValueKind.String) continue;
                fields++;
                if (Spaces().Replace(want.GetString()!, " ").Trim() == Spaces().Replace(value ?? "", " ").Trim()) fieldsSame++;
            }
        }

        double mean = sum / pages;
        Console.WriteLine($"{pages} pages: mean F1 {mean:F3}, metadata {fieldsSame}/{fields}");
        Assert.IsGreaterThanOrEqualTo(0.98, mean, string.Join("\n", report));
        Assert.IsGreaterThanOrEqualTo(0.97, (double)fieldsSame / fields);
    }

    private static double F1(string actual, string expected)
    {
        var a = Words(actual);
        var e = Words(expected);
        if (a.Count == 0 && e.Count == 0) return 1;

        var bag = new Dictionary<string, int>();
        foreach (var w in e) bag[w] = bag.GetValueOrDefault(w) + 1;
        int common = 0;
        foreach (var w in a)
        {
            if (bag.TryGetValue(w, out int n) && n > 0)
            {
                bag[w] = n - 1;
                common++;
            }
        }
        if (common == 0) return 0;
        double precision = (double)common / a.Count, recall = (double)common / e.Count;
        return 2 * precision * recall / (precision + recall);
    }

    private static List<string> Words(string text) => NotWord().Split(text.ToLowerInvariant()).Where(w => w.Length > 0).ToList();

    [GeneratedRegex(@"\W+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
