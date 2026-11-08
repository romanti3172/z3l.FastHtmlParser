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
        "nav", "header", "footer", "section", "div", "dl", "dt", "dd", "select", "option", "optgroup",
    ];

    private static readonly string[] AttributeNames = ["href", "class", "id", "title", "alt", "src", "value", "content", "name", "itemprop"];

    public static IEnumerable<object[]> Pages => Fixtures.All.Select(f => new object[] { f.Path });

    [TestMethod]
    [DynamicData(nameof(Pages))]
    public void SameElementsTextAndAttributes(string path)
    {
        var problems = Compare(Fixtures.Read(path));

        if (problems.Count > 0)
        {
            Assert.Fail($"{path}: {problems.Count} differences from AngleSharp:\n" + string.Join("\n", problems.Take(25)));
        }
    }

    /// <summary>Broken markup as found on real sites: optional end tags left out, stray end tags, truncated pages.</summary>
    public static IEnumerable<object[]> BrokenSnippets =>
    [
        ["<ul><li>One<li>Two<li>Three</ul><p>after</p>"],
        ["<ul><li>a<ul><li>a1<li>a2</ul><li>b</ul>"],
        ["<ol><li><p>para<li>next</ol>"],
        ["<div><p>First<p>Second</div><span>x</span>"],
        ["<p>intro<div>block</div><p>tail"],
        ["<p>one<ul><li>item</ul>"],
        ["<p>head<h2>title</h2><p>body<table><tr><td>cell</table>"],
        ["<table><tr><td>A<td>B<tr><td>C<td>D</table>"],
        ["<table><thead><tr><th>Name<th>Price<tbody><tr><td>Book<td>10<tr><td>Pen<td>2</table>"],
        ["<table><tr><td>outer<table><tr><td>inner</table>after</td></tr></table>"],
        ["<dl><dt>Price<dd>100<dt>Color<dd>red</dl>"],
        ["<select name=s><option value=1>One<option value=2 selected>Two<optgroup label=g><option>Three<option>Four</select>"],
        ["<div class=\"a\">x</span></div></div><div class=\"b\">y</div>"],
        ["<p>if a < b and c > d, 1 <3</p><div data-tpl=\"<b>x</b>\" class=\"t\">ok</div>"],
        ["<div class=\"card\"><span class=\"price\">1 990 &#8381;</span><span class=\"title\">Ноут"],
        ["<div><span>cut in the middle of a tag<a href=\"/x"],
        ["<p>visible</p><!-- <p>hidden</p>"],
        ["<DIV CLASS=card ID=main><A HREF=/item/1 data-id=5>Item</A></DIV>"],
        ["<script>var s = '<div class=\"price\">fake</div>';</script><div class=\"price\">42</div>"],

        // the broken part is scanned again: several in one page, nested, at the end of the document
        ["<div><ul><li>a<li>b</ul><ul><li>c<li>d</ul></div><p>after"],
        ["<table><tr><td>1<td><ul><li>x<li>y</ul><td>3</table><span>tail</span>"],
        ["<ul><li>a<!-- <li>no --><script>var x = '<li>';</script><li>b</ul><b>after</b>"],
        ["<html><body><div class=\"list\"><ul><li>a<li>b"],
        ["<p>a<p>b<div>c</div><p>d<ul><li>e</ul>"],
        ["<div><span>open<ul><li>a<li>b</ul></div><a href=\"/next\">next</a>"],

        // two mistakes at once: the left-out end tag is not the innermost open tag, so that part is scanned again
        // (<span>, not <b>/<i>: a browser copies unclosed formatting elements into the next <li>, this parser does not)
        ["<ul><li><span>a<li>b<li><span>c<li>d</ul><p>after"],
        ["<table><tr><td><span>x<td>y<tr><td>z</table><i>t</i>"],
        ["<ol><li><span>a<li>b"],
        ["<dl><dt><span>Price<dd>100<dt>Color<dd>red</dl>"],
        ["<div><p>a<span>b<p>c<div>d</div></div>"],
    ];

    [TestMethod]
    [DynamicData(nameof(BrokenSnippets))]
    public void BrokenMarkup_SameAsAngleSharp(string html)
    {
        // with a doctype, as real pages have: without one a browser uses quirks mode (where e.g. <table> does not end a <p>)
        var problems = Compare("<!DOCTYPE html>" + html);

        if (problems.Count > 0)
        {
            Assert.Fail($"{html}\n{problems.Count} differences from AngleSharp:\n" + string.Join("\n", problems.Take(25)));
        }
    }

    private static List<string> Compare(string html)
    {
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
                // A fragment without <html>/<body>: a browser puts its top-level tags into the implicit <body>.
                bool implicitBody = parent == "" && expectedParent == "body" && ours.Find(HtmlTag.Body) == null;
                if (parent != expectedParent && !implicitTbody && !strayBody && !implicitBody)
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

        return problems;
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
