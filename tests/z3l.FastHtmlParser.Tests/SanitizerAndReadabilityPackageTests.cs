namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// The packages z3l.FastHtmlParser.HtmlSanitizer and z3l.FastHtmlParser.Readability: each is a method of the parser
/// behind one class, and the assembly has nothing else.
/// </summary>
[TestClass]
public sealed class SanitizerAndReadabilityPackageTests
{
    [TestMethod]
    public void Sanitize_IsTheSanitizerOfTheParsedPage()
    {
        foreach (var (path, url) in Fixtures.All)
        {
            string html = Fixtures.Read(path);

            Assert.AreEqual(new HtmlParser(html, url).Sanitize(), HtmlSanitizer.Sanitize(html, url), path);
            Assert.AreEqual(HtmlText.Sanitize(html), HtmlSanitizer.Sanitize(html), path);
        }

        // the examples of the READMEs
        Assert.AreEqual("<p>Hi </p>", HtmlSanitizer.Sanitize("<p onclick=\"steal()\">Hi <script>alert(1)</script></p>"));
        Assert.AreEqual("<p>Hi <a>me</a></p>",
            HtmlSanitizer.Sanitize("<p onclick=\"steal()\">Hi <script>alert(1)</script><a href=\"javascript:x()\">me</a></p>"));
        Assert.AreEqual("<b>bold</b>", HtmlText.Sanitize("<b onclick=\"x()\">bold</b>"));
        Assert.AreEqual("<a href=\"https://example.com/x\" rel=\"nofollow\">x</a>",
            HtmlSanitizer.Sanitize("<a href=/x>x</a>", "https://example.com/post", new SanitizeOptions { LinkRel = "nofollow" }));
        Assert.AreEqual("", HtmlSanitizer.Sanitize(null));
        Assert.AreEqual("", HtmlSanitizer.Sanitize(""));
    }

    [TestMethod]
    public void Parse_IsTheArticleOfTheParsedPage()
    {
        const string html = "<html><head><title>A title of the story about foxes</title></head><body><nav><a href=/>Home</a></nav>" +
            "<article><p>The quick brown fox jumps over the lazy dog, again and again, while the reporters watch, take notes, and ask.</p>" +
            "<p>A second paragraph, long enough to count as one, with <a href=\"/more\">a link</a> in it, as the rules say.</p></article></body></html>";

        var article = Readability.Parse(html, "https://example.com/news/1");
        var expected = new HtmlParser(html, "https://example.com/news/1").Article();

        Assert.IsNotNull(article);
        Assert.IsNotNull(expected);
        Assert.AreEqual(expected.Content, article.Content);
        Assert.AreEqual(expected.Title, article.Title);
        StringAssert.Contains(article.Markdown, "[a link](https://example.com/more)");

        Assert.IsNull(Readability.Parse(null));
        Assert.IsNull(Readability.Parse(""));
        Assert.IsFalse(Readability.IsProbablyReadable(null));
        Assert.IsFalse(Readability.IsProbablyReadable(html));
        Assert.IsTrue(Readability.IsProbablyReadable(html, minContentLength: 50, minScore: 5));
    }

    [TestMethod]
    [DataRow(typeof(HtmlSanitizer), "z3l.FastHtmlParser.HtmlSanitizer")]
    [DataRow(typeof(Readability), "z3l.FastHtmlParser.Readability")]
    public void TheAssembly_HasThisOneClass_InTheNamespaceOfTheParser(Type type, string package)
    {
        // The packages are used together. A second public type or an extension method here could get the name
        // of one in the parser, and then a call would not compile in a project that has both.
        var assembly = type.Assembly;

        Assert.AreEqual(package, assembly.GetName().Name);
        Assert.AreNotSame(typeof(HtmlParser).Assembly, assembly);
        CollectionAssert.AreEqual(new[] { type }, assembly.GetExportedTypes());

        // one "using" for all the packages; the full name of the class is the name of the package
        Assert.AreEqual(typeof(HtmlParser).Namespace, type.Namespace);
        Assert.AreEqual(package, type.FullName);

        // no type of the parser has the name of the class
        Assert.IsNull(typeof(HtmlParser).Assembly.GetType(type.FullName!));
    }
}
