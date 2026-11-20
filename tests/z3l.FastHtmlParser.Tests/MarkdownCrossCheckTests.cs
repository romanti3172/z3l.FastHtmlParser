using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using AngleSharp.Dom;
using Markdig;
using Markdig.Extensions.EmphasisExtras;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// Differential test of <c>Markdown()</c>: the Markdown is rendered back to HTML by Markdig (CommonMark with the
/// GitHub tables, strikethrough and task lists) and compared with the page it was made from, as AngleSharp reads it:
/// the same visible text, links, images, headings and list items. A char that Markdown reads as its own where it
/// should be text, a lost word, a list that falls apart: all of them show up here.
/// </summary>
[TestClass]
public sealed class MarkdownCrossCheckTests
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseDefinitionLists()
        .Build();

    // What Markdown() leaves out.
    private const string Hidden =
        "head, script, style, noscript, template, svg, canvas, iframe, embed, object, applet, audio, video, "
        + "select, datalist, textarea, map, frameset, progress, meter";

    private static readonly HashSet<string> HiddenNames = [.. Hidden.Split(", ")];

    public static IEnumerable<object[]> Pages => Fixtures.All.Select(f => new object[] { f.Path });

    [TestMethod]
    [DynamicData(nameof(Pages))]
    public void Page_RendersBackToTheSameContent(string path)
    {
        var (_, url) = Fixtures.All.First(f => f.Path == path);
        var problems = Compare(Fixtures.Read(path), url);

        if (problems.Count > 0)
        {
            Assert.Fail($"{path}: {problems.Count} differences after HTML → Markdown → HTML:\n" + string.Join("\n", problems.Take(25)));
        }
    }

    /// <summary>The page as Markdown, as saved next to it: a change of the output shows up as a diff of these files.</summary>
    [TestMethod]
    [DynamicData(nameof(Pages))]
    public void Page_GivesTheSavedMarkdown(string path)
    {
        string markdown = Fixtures.Load(path).Markdown()!;
        string file = Path.ChangeExtension(path, ".md");

        // FHP_UPDATE_MARKDOWN=1 dotnet test: writes the files anew (then look at the diff)
        if (Environment.GetEnvironmentVariable("FHP_UPDATE_MARKDOWN") == "1")
        {
            File.WriteAllText(Path.Combine(SourceFolder(), "Fixtures", file), markdown + "\n", new UTF8Encoding(false));
            return;
        }

        string saved = Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
        Assert.IsTrue(File.Exists(saved), $"{file} is not saved yet: run the tests once with FHP_UPDATE_MARKDOWN=1");
        Assert.AreEqual(File.ReadAllText(saved).ReplaceLineEndings("\n").TrimEnd('\n'), markdown, path);
    }

    private static string SourceFolder([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    /// <summary>The HTML of every <c>Converts_</c> case of <see cref="MarkdownTests"/>.</summary>
    public static IEnumerable<object[]> Cases =>
        from method in typeof(MarkdownTests).GetMethods()
        where method.Name.StartsWith("Converts_")
        from row in method.GetCustomAttributes<DataRowAttribute>()
        let html = (string)row.Data![0]!
        where !NotComparable.Any(html.Contains)
        select new object[] { html };

    // Markup that a browser (AngleSharp) and this parser read differently, so that the two sides of the comparison
    // are not the same page: stray table parts, a link inside a link, blocks inside a heading or an inline pair.
    private static readonly string[] NotComparable =
    [
        "<td>cell outside</td>",
        "<a href=\"outer\">",
        "<h2><div>block</div>",
        "<em>a<p>b</p>c</em>",
        "<li>outside</li>",
        "<b><table>",
        "<a href=\"/card\"><div>",
        "<table><td>x</td>",
        "<xmp>",

        // not the same text by design: the markers of a list in a table cell, and "\|" in a code span of a cell,
        // which GitHub reads as "|" and Markdig does not
        "<table><caption>The <b>caption</b>",
    ];

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void Case_RendersBackToTheSameContent(string html)
    {
        var problems = Compare(html, null);

        if (problems.Count > 0)
        {
            Assert.Fail($"{problems.Count} differences after HTML → Markdown → HTML:\n" + string.Join("\n", problems)
                + "\n--- Markdown\n" + HtmlText.ToMarkdown(html));
        }
    }

    /// <summary>What Markdown reads as its own, in every place it can stand: all of it must come back as text.</summary>
    [TestMethod]
    public void EveryPunctuationChar_ComesBackAsText()
    {
        const string punctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
        string[] words = ["a", "1", " ", "", "ab", "x y"];
        var failures = new List<string>();

        foreach (char c in punctuation)
        {
            foreach (char d in punctuation)
            {
                foreach (string word in words)
                {
                    // at the start of a block, inside a word, after a space, doubled, as the whole text
                    foreach (string text in new[] { $"{c}{word}{d}", $"{word}{c}{d}{word}", $"{c}{d} {word}", $"x {c}{word}{d} y", $"1{c} {word}{d}" })
                    {
                        string html = "<p>" + System.Net.WebUtility.HtmlEncode(text) + "</p><ul><li>" + System.Net.WebUtility.HtmlEncode(text) + "</li></ul>";
                        string back = Squeeze(Render(HtmlText.ToMarkdown(html)!).Body!.TextContent);
                        string expected = Squeeze(text + text);
                        if (back != expected) failures.Add($"{text}: came back as {back}");
                    }
                }
            }
        }

        if (failures.Count > 0) Assert.Fail($"{failures.Count} texts changed:\n" + string.Join("\n", failures.Take(40)));
    }

    /// <summary>
    /// Emphasis between any two chars, around any text: Markdown reads "**" as emphasis only when the chars on both
    /// sides of it allow it, so every combination must come back as the same element with the same text.
    /// </summary>
    [TestMethod]
    public void Emphasis_ComesBackAsEmphasis_WhateverIsAroundIt()
    {
        string[] around = ["", " ", "a", "1", ".", "(", ")", "\"", "日", "。", "😀", "_", "*"];
        string[] inside = ["x", "x y", "(x)", "x:", "!", "「x」", "😀", "x*", "_x_", "a.b"];
        (string Open, string Close, string Selector)[] pairs =
        [
            ("<b>", "</b>", "strong"), ("<i>", "</i>", "em"), ("<del>", "</del>", "del"),
            // "***x***" is bold and italic, whichever of the two is outside
            ("<b><i>", "</i></b>", "strong > em, em > strong"), ("<i><b>", "</b></i>", "strong > em, em > strong"),
            ("<del><b>", "</b></del>", "del > strong"),
        ];
        var failures = new List<string>();

        foreach (string before in around)
        {
            foreach (string after in around)
            {
                foreach (string text in inside)
                {
                    foreach (var (open, close, selector) in pairs)
                    {
                        string html = $"<p>{before}{open}{text}{close}{after}</p>";
                        string markdown = HtmlText.ToMarkdown(html)!;
                        var back = Render(markdown);

                        var found = back.QuerySelectorAll(selector);
                        if (found.Length != 1 || found[0].TextContent != text || Squeeze(back.Body!.TextContent) != Squeeze(before + text + after))
                        {
                            failures.Add($"{html}  →  {markdown}  →  {back.Body!.InnerHtml.Trim()}");
                        }
                    }
                }
            }
        }

        if (failures.Count > 0) Assert.Fail($"{failures.Count} are not emphasis any more:\n" + string.Join("\n", failures.Take(40)));
    }

    // ----- the comparison -----

    private static IDocument Render(string markdown)
    {
        return new AngleSharp.Html.Parser.HtmlParser().ParseDocument(Markdown.ToHtml(markdown, Pipeline));
    }

    private static List<string> Compare(string html, string? url)
    {
        var problems = new List<string>();

        var doc = new HtmlParser(html, url);
        string markdown = doc.Markdown()!;
        var back = Render(markdown);
        var original = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        // text: every char that is not whitespace, in order
        foreach (var hidden in original.QuerySelectorAll(Hidden).ToList()) hidden.Remove();
        foreach (var quote in original.QuerySelectorAll("q").ToList())
        {
            quote.Prepend(original.CreateTextNode("\""));
            quote.Append(original.CreateTextNode("\""));
        }

        string expectedText = Squeeze(original.Body?.TextContent ?? "");
        string actualText = Squeeze(back.Body?.TextContent ?? "");
        if (expectedText != actualText)
        {
            int at = 0;
            while (at < expectedText.Length && at < actualText.Length && expectedText[at] == actualText[at]) at++;
            problems.Add($"text differs at {at}: expected …{Cut(expectedText, at)}… but was …{Cut(actualText, at)}…");
        }

        // links, in order; a link around several blocks is written once per block
        var expectedLinks = Collapse(Shown(doc, HtmlTag.A).Where(IsLink).Select(a => Address(a.AttributeHref())));
        var actualLinks = Collapse(back.QuerySelectorAll("a[href]").Select(a => Address(a.GetAttribute("href"))));
        AddDifference(problems, "links", expectedLinks, actualLinks);

        // images
        var expectedImages = Shown(doc, HtmlTag.Img).Select(ImageAddress).Where(src => src != null).Select(Address).ToList();
        var actualImages = back.QuerySelectorAll("img[src]").Select(img => Address(img.GetAttribute("src"))).ToList();
        AddDifference(problems, "images", expectedImages, actualImages);

        // headings: the ones in table cells are text of the cell, in a table written as blocks they are headings
        var headings = Shown(doc).Where(tag => IsHeading(tag) && HasContent(tag)).ToList();
        var outsideCells = headings.Where(h => !InCell(h)).Select(Heading).ToList();
        var actualHeadings = back.QuerySelectorAll("h1, h2, h3, h4, h5, h6").Select(h => $"{h.LocalName} {Squeeze(h.TextContent)}").ToList();
        if (actualHeadings.Count < outsideCells.Count || actualHeadings.Count > headings.Count || !IsSubsequence(outsideCells, actualHeadings))
        {
            AddDifference(problems, "headings", outsideCells, actualHeadings);
        }

        // list items
        var items = Shown(doc, HtmlTag.Li).Where(HasContent).ToList();
        int itemsOutsideCells = items.Count(li => !InCell(li));
        int actualItems = back.QuerySelectorAll("li").Length;
        if (actualItems < itemsOutsideCells || actualItems > items.Count)
        {
            problems.Add($"list items: expected {itemsOutsideCells}..{items.Count} but was {actualItems}");
        }

        return problems;
    }

    private static void AddDifference(List<string> problems, string what, List<string> expected, List<string> actual)
    {
        if (expected.SequenceEqual(actual)) return;

        int at = 0;
        while (at < expected.Count && at < actual.Count && expected[at] == actual[at]) at++;
        problems.Add($"{what}: expected {expected.Count} but was {actual.Count}; first difference at {at}: "
            + $"expected {(at < expected.Count ? expected[at] : "nothing")} but was {(at < actual.Count ? actual[at] : "nothing")}");
    }

    /// <summary>The tags a browser shows: not inside <see cref="Hidden"/>.</summary>
    private static IEnumerable<ParserTag> Shown(HtmlParser doc, HtmlTag? name = null)
    {
        foreach (var tag in doc)
        {
            if (name != null && !tag.Is(name.Value)) continue;

            bool hidden = false;
            for (ParserTag? at = tag; at != null && !hidden; at = at.Parent()) hidden = at.Name != null && HiddenNames.Contains(at.Name);
            if (!hidden) yield return tag;
        }
    }

    private static bool IsHeading(ParserTag tag) => tag.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

    private static string Heading(ParserTag tag) => $"{tag.Name} {Squeeze(tag.Text() ?? "")}";

    private static bool InCell(ParserTag tag) => tag.Parent(HtmlTag.Td) != null || tag.Parent(HtmlTag.Th) != null;

    private static bool HasContent(ParserTag tag)
    {
        return Squeeze(tag.Text() ?? "").Length > 0 || tag.FindAll(HtmlTag.Img).Any(img => ImageAddress(img) != null)
            || tag.Find(HtmlTag.Input) != null || tag.Find(HtmlTag.Hr) != null;
    }

    private static bool IsLink(ParserTag link)
    {
        string href = link.AttributeByName(HtmlAttribute.Href).Trim();
        return IsAddress(href) && HasContent(link) && link.Parent(HtmlTag.A) == null;
    }

    private static bool IsAddress(string address)
    {
        return address.Length > 0 && address != "#"
            && !address.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            && !address.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The address of an image as it is written: of a lazy-loading attribute first, then src.</summary>
    private static string? ImageAddress(ParserTag image)
    {
        foreach (string name in (string[])["data-src", "data-lazy-src", "data-original"])
        {
            string lazy = image.AttributeByName(name).Trim();
            if (IsAddress(lazy)) return Resolve(image, lazy);
        }

        string src = image.AttributeByName(HtmlAttribute.Src).Trim();
        return IsAddress(src) ? image.AttributeSrc() : null;
    }

    /// <summary>As AttributeSrc() resolves src: an address with a scheme stays as written.</summary>
    private static string Resolve(ParserTag tag, string address)
    {
        string page = tag.Document.PageUrl;
        bool hasScheme = System.Text.RegularExpressions.Regex.IsMatch(address, "^[a-zA-Z][a-zA-Z0-9+.-]*:");
        return page.Length > 0 && !hasScheme && Uri.TryCreate(new Uri(page), address, out var absolute) ? absolute.ToString() : address;
    }

    /// <summary>An address without what only the writing of it differs in: percent-escapes, spaces at its ends.</summary>
    private static string Address(string? address) => Uri.UnescapeDataString((address ?? "").Trim());

    private static List<string> Collapse(IEnumerable<string> values)
    {
        var result = new List<string>();
        foreach (string value in values)
        {
            if (result.Count == 0 || result[^1] != value) result.Add(value);
        }
        return result;
    }

    private static bool IsSubsequence(List<string> part, List<string> whole)
    {
        int at = 0;
        foreach (string value in whole)
        {
            if (at < part.Count && part[at] == value) at++;
        }
        return at == part.Count;
    }

    private static string Squeeze(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c)) result.Append(c);
        }
        return result.ToString();
    }

    private static string Cut(string text, int at) => text.Substring(Math.Max(0, at - 30), Math.Min(text.Length, at + 40) - Math.Max(0, at - 30));
}
