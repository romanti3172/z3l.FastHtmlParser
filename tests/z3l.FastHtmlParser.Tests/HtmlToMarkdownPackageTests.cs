namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// The package z3l.FastHtmlParser.HtmlToMarkdown: <c>HtmlToMarkdown.Convert</c> is the converter of the parser
/// behind one method, and the assembly has nothing else.
/// </summary>
[TestClass]
public sealed class HtmlToMarkdownPackageTests
{
    [TestMethod]
    public void Convert_IsTheMarkdownOfTheParsedPage()
    {
        foreach (var (path, url) in Fixtures.All)
        {
            string html = Fixtures.Read(path);

            Assert.AreEqual(new HtmlParser(html, url).Markdown(), HtmlToMarkdown.Convert(html, url), path);
            Assert.AreEqual(new HtmlParser(html).Markdown(), HtmlToMarkdown.Convert(html), path);
            Assert.AreEqual(HtmlText.ToMarkdown(html), HtmlToMarkdown.Convert(html), path);
        }
    }

    [TestMethod]
    public void Convert_MakesLinksAndImagesAbsolute_WhenTheUrlIsGiven()
    {
        const string html = "<h2>Title</h2><p>Some <b>bold</b> and a <a href=\"/x\">link</a> <img src=\"i.png\" alt=\"pic\"></p>";

        Assert.AreEqual("## Title\n\nSome **bold** and a [link](/x) ![pic](i.png)", HtmlToMarkdown.Convert(html));
        Assert.AreEqual(
            "## Title\n\nSome **bold** and a [link](https://example.com/x) ![pic](https://example.com/blog/i.png)",
            HtmlToMarkdown.Convert(html, "https://example.com/blog/post"));
    }

    [TestMethod]
    public void Convert_LeavesOutWhatTheOptionsSay()
    {
        const string html = "<p>See <a href=\"/a\">the <b>link</b></a> and <img src=\"i.png\" alt=\"pic\">.</p>";

        Assert.AreEqual("See the **link** and ![pic](i.png).", HtmlToMarkdown.Convert(html, options: new MarkdownOptions { Links = false }));
        Assert.AreEqual("See [the **link**](/a) and .", HtmlToMarkdown.Convert(html, options: new MarkdownOptions { Images = false }));
        Assert.AreEqual(
            "See the **link** and .",
            HtmlToMarkdown.Convert(html, "https://example.com/", new MarkdownOptions { Links = false, Images = false }));
    }

    [TestMethod]
    public void Convert_OfNothing_IsAnEmptyString()
    {
        // never null: the result goes into a string without a check
        Assert.AreEqual("", HtmlToMarkdown.Convert(null));
        Assert.AreEqual("", HtmlToMarkdown.Convert(""));
        Assert.AreEqual("", HtmlToMarkdown.Convert(" \n "));
        Assert.AreEqual("", HtmlToMarkdown.Convert("<script>var x;</script><!-- a comment -->"));
        Assert.AreEqual("", HtmlToMarkdown.Convert(null, "https://example.com/", new MarkdownOptions()));
    }

    [TestMethod]
    public void TheAssembly_HasThisOneClass_InTheNamespaceOfTheParser()
    {
        // The two packages are used together. A second public type or an extension method here could get the name
        // of one in the parser, and then a call would not compile in a project that has both.
        var assembly = typeof(HtmlToMarkdown).Assembly;

        Assert.AreEqual("z3l.FastHtmlParser.HtmlToMarkdown", assembly.GetName().Name);
        Assert.AreNotSame(typeof(HtmlParser).Assembly, assembly);
        CollectionAssert.AreEqual(new[] { typeof(HtmlToMarkdown) }, assembly.GetExportedTypes());

        // one "using" for both packages; the full name of the class is the name of the package
        Assert.AreEqual(typeof(HtmlParser).Namespace, typeof(HtmlToMarkdown).Namespace);
        Assert.AreEqual(assembly.GetName().Name, typeof(HtmlToMarkdown).FullName);

        // no type of the parser has the name of the class
        Assert.IsNull(typeof(HtmlParser).Assembly.GetType(typeof(HtmlToMarkdown).FullName!));
    }
}
