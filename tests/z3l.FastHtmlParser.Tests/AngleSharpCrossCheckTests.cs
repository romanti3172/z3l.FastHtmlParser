using System.Text;
using AngleSharp.Dom;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// Differential test: for every fixture page the same elements must be found as by AngleSharp
/// (a spec-compliant browser-grade parser), with the same text and attribute values.
/// </summary>
[TestClass]
public sealed class AngleSharpCrossCheckTests
{
    private static readonly string[] TagNames =
    [
        "a", "p", "span", "li", "ul", "ol", "h1", "h2", "h3", "h4", "table", "tr", "th", "td",
        "img", "small", "strong", "b", "i", "button", "article", "form", "input", "label", "meta",
        "nav", "header", "footer", "section", "div",
    ];

    private static readonly string[] AttributeNames = ["href", "class", "id", "title", "alt", "src", "value", "content", "name", "itemprop"];

    public static IEnumerable<object[]> Pages => Fixtures.All.Select(f => new object[] { f.Path });

    [TestMethod]
    [DynamicData(nameof(Pages))]
    public void SameElementsTextAndAttributes(string path)
    {
        var html = Fixtures.Read(path);
        var ours = new HtmlParser(html);
        var reference = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        var problems = new List<string>();

        foreach (var name in TagNames)
        {
            var found = ours.FindAllByName(name).ToList();
            var expected = reference.QuerySelectorAll(name).ToList();

            if (found.Count != expected.Count)
            {
                problems.Add($"<{name}> count: {found.Count}, AngleSharp: {expected.Count}");
                continue;
            }

            for (int i = 0; i < found.Count; i++)
            {
                var text = Normalize(found[i].Text());
                var expectedText = Normalize(VisibleText(expected[i]));
                if (text != expectedText)
                {
                    problems.Add($"<{name}>[{i}] text: '{Short(text)}' vs '{Short(expectedText)}'");
                }

                var parent = found[i].Parent()?.Name ?? "";
                var expectedParent = expected[i].ParentElement?.LocalName ?? "";
                // A browser inserts an implicit <tbody> between <table> and <tr>; the source has none.
                bool implicitTbody = name == "tr" && parent == "table" && expectedParent == "tbody";
                // A browser ignores a second <body> start tag (quotes/login.html has one inside a <div>); we keep it as a tag.
                bool strayBody = parent == "body" && ours.FindAllByName(HtmlTag.Body).Count() > 1;
                if (parent != expectedParent && !implicitTbody && !strayBody)
                {
                    problems.Add($"<{name}>[{i}] parent: <{parent}> vs <{expectedParent}>");
                }

                foreach (var attribute in AttributeNames)
                {
                    var value = found[i].AttributeByName(attribute);
                    var expectedValue = expected[i].GetAttribute(attribute) ?? "";
                    if (value != expectedValue)
                    {
                        problems.Add($"<{name}>[{i}] @{attribute}: '{Short(value)}' vs '{Short(expectedValue)}'");
                    }
                }
            }
        }

        if (problems.Count > 0)
        {
            Assert.Fail($"{path}: {problems.Count} differences from AngleSharp:\n" + string.Join("\n", problems.Take(25)));
        }
    }

    private static readonly HashSet<string> Invisible = ["script", "style", "noscript", "template", "noframes", "noembed"];

    /// <summary>TextContent without the content of invisible elements (script, style, ...): what Text() returns.</summary>
    private static string VisibleText(INode node)
    {
        var sb = new StringBuilder();
        Append(node);
        return sb.ToString();

        void Append(INode current)
        {
            foreach (var child in current.ChildNodes)
            {
                if (child is IText text) sb.Append(text.Data);
                else if (child is IElement element && !Invisible.Contains(element.LocalName)) Append(element);
            }
        }
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Short(string s) => s.Length <= 80 ? s : s[..80] + "…";
}
