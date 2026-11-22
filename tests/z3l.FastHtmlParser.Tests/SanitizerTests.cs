using System.Text;
using AngleSharp.Dom;

namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// <c>Sanitize()</c>: the exact result for the usual cases and for known XSS vectors, and a differential check: random
/// broken markup is sanitized and the result is parsed by AngleSharp (a parser that follows the HTML standard, as
/// browsers do); a browser must see only allowed elements and attributes in it, and sanitizing it again changes nothing.
/// </summary>
[TestClass]
public sealed class SanitizerTests
{
    private static string S(string html, SanitizeOptions? options = null) => HtmlText.Sanitize(html, options)!;

    [TestMethod]
    [DataRow("<script>alert(1)</script>hello", "hello")]
    [DataRow("<img src=x onerror=alert(1)>", "<img src=\"x\">")]
    [DataRow("<a href=\"javascript:alert(1)\">x</a>", "<a>x</a>")]
    [DataRow("<a href=\"JaVaScRiPt:alert(1)\">x</a>", "<a>x</a>")]
    [DataRow("<a href=\"jav&#x09;ascript:alert(1)\">x</a>", "<a>x</a>")]
    [DataRow("<a href=\"jav&#x0A;ascript:alert(1)\">x</a>", "<a>x</a>")]
    [DataRow("<a href=\" &#14; javascript:alert(1)\">x</a>", "<a>x</a>")]
    [DataRow("<a href=\"java script:alert(1)\">x</a>", "<a>x</a>")]
    [DataRow("<a href=\"&#106;&#97;&#118;&#97;&#115;&#99;&#114;&#105;&#112;&#116;&#58;alert(1)\">x</a>", "<a>x</a>")]
    [DataRow("<a href=\"vbscript:msgbox(1)\">x</a>", "<a>x</a>")]
    [DataRow("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>", "<a>x</a>")]
    [DataRow("<svg><style><img src=x onerror=alert(1)></style></svg>", "")]
    [DataRow("<svg onload=alert(1)><circle r=1></circle></svg>after", "after")]
    [DataRow("<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>", "")]
    [DataRow("<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript>", "")]
    [DataRow("<template><img src=x onerror=1></template>t", "t")]
    [DataRow("<title><img src=x onerror=alert(1)></title><style>*{}</style><xmp><script>alert(1)</script></xmp>", "")]
    [DataRow("<iframe src=\"https://evil\"></iframe><iframe srcdoc=\"<script>alert(1)</script>\"></iframe>x", "x")]
    [DataRow("<form action=\"x\"><input name=a><button>Go</button></form>after", "after")]
    [DataRow("<object data=\"x.swf\"></object><embed src=\"x.swf\">e", "e")]
    [DataRow("<base href=\"https://evil/\"><meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\"><link rel=stylesheet href=x>", "")]
    [DataRow("<div onmouseover=\"alert(1)\" ONCLICK=x OnFocus=y>t</div>", "<div>t</div>")]
    [DataRow("<img src=1 alt=\"x`onerror=alert(1)`\">", "<img src=\"1\" alt=\"x`onerror=alert(1)`\">")]
    [DataRow("<img src=\"a\" alt='\"><script>alert(1)</script>'>", "<img src=\"a\" alt=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;\">")]
    [DataRow("<p title=\"a\nb\" title=\"second\">x</p>", "<p title=\"a\nb\">x</p>")]
    [DataRow("<!-- <img src=x onerror=alert(1)> -->c", "c")]
    // A browser reads "<![CDATA[<img ...>" as a bogus comment; the parser finds the img in it. What is kept of it is
    // checked as any other tag, so the result is still safe.
    [DataRow("<![CDATA[<img src=x onerror=alert(1)>]]>d", "<img src=\"x\">]]&gt;d")]
    [DataRow("a < b > c &amp; &lt;script&gt; &copy; &nbsp;x", "a &lt; b &gt; c &amp; &lt;script&gt; © &nbsp;x")]
    [DataRow("<b>unclosed <i>tags", "<b>unclosed <i>tags</i></b>")]
    [DataRow("<b><i>bold italic</b> rest</i>", "<b><i>bold italic</i></b> rest")]
    [DataRow("<p>one<p>two", "<p>one</p><p>two</p>")]
    [DataRow("<ul><li>a<li>b</ul>", "<ul><li>a</li><li>b</li></ul>")]
    [DataRow("<table><tr><td>1<td>2</table>", "<table><tr><td>1</td><td>2</td></tr></table>")]
    [DataRow("<font color=red face=x size=1>red</font> <center>c</center>", "<font color=\"red\" face=\"x\" size=\"1\">red</font> <center>c</center>")]
    [DataRow("<x-widget>custom</x-widget>", "custom")]
    [DataRow("<img src=\"x\" <script>", "")]
    [DataRow("text <img src=x onerror=alert(1)", "text ")]
    [DataRow("</p></div>stray end tags</b>", "stray end tags")]
    [DataRow("<br/><hr/><wbr>", "<br><hr><wbr>")]
    [DataRow("<div/>x", "<div></div>x")]
    public void KnownCases(string html, string expected)
    {
        Assert.AreEqual(expected, S(html));
    }

    [TestMethod]
    public void Urls_RelativeAndAllowedSchemesStay()
    {
        Assert.AreEqual(
            "<a href=\"/rel/path\">r</a><a href=\"#top\">h</a><a href=\"?q=1\">q</a><a href=\"mailto:a@b.c\">m</a>"
            + "<a href=\"tel:+123\">t</a><a href=\"https://ok.com/a?b=1&amp;c=2\">ok</a>",
            S("<a href=\"/rel/path\">r</a><a href=#top>h</a><a href=?q=1>q</a><a href=\"mailto:a@b.c\">m</a>"
                + "<a href=\"tel:+123\">t</a><a href=\"https://ok.com/a?b=1&amp;c=2\">ok</a>"));
    }

    [TestMethod]
    public void Urls_AreMadeAbsolute_WithThePageUrl()
    {
        var doc = new HtmlParser("<a href=\"../x?a=1&amp;b=2\">x</a><img src=\"i.png\" srcset=\"s.png 1x, //cdn.example.com/b.png 2x\">", "https://example.com/blog/post/");

        Assert.AreEqual(
            "<a href=\"https://example.com/blog/x?a=1&amp;b=2\">x</a><img src=\"https://example.com/blog/post/i.png\" "
            + "srcset=\"https://example.com/blog/post/s.png 1x, https://cdn.example.com/b.png 2x\">",
            doc.Sanitize());
        Assert.AreEqual(
            "<a href=\"../x?a=1&amp;b=2\">x</a><img src=\"i.png\" srcset=\"s.png 1x, //cdn.example.com/b.png 2x\">",
            doc.Sanitize(new SanitizeOptions { AbsoluteUrls = false }));
    }

    [TestMethod]
    public void Srcset_LosesOnlyTheBadCandidates()
    {
        Assert.AreEqual(
            "<img srcset=\"a.png 1x, b.png 3x\">",
            S("<img srcset=\"a.png 1x, javascript:alert(1) 2x, b.png 3x\">"));
        Assert.AreEqual("<img>", S("<img srcset=\"javascript:alert(1) 2x\">"));
    }

    [TestMethod]
    public void DataImages_OnlyWhenAllowed()
    {
        const string html = "<img src=\"data:image/png;base64,iVBORw0KGgo=\"><img src=\"data:image/svg+xml;base64,PHN2Zz4=\">";

        Assert.AreEqual("<img><img>", S(html));
        Assert.AreEqual("<img src=\"data:image/png;base64,iVBORw0KGgo=\"><img>", S(html, new SanitizeOptions { AllowDataImages = true }));
    }

    [TestMethod]
    public void Styles_KeepOnlyPlainAllowedDeclarations()
    {
        Assert.AreEqual(
            "<p style=\"color: red; font-family: 'Times New Roman'\">s</p>",
            S("<p style=\"color:red;background:url(javascript:alert(1));width:expression(alert(1));font-family: 'Times New Roman';position:fixed\">s</p>"));
        Assert.AreEqual("<p>s</p>", S("<p style=\"color:\\72 ed; behavior:url(x.htc); -moz-binding:url(x)\">s</p>"));
        Assert.AreEqual("<p>s</p>", S("<p style=\"color: red /* */\">s</p>"));
        Assert.AreEqual("<p>s</p>", S("<p style=\"background-image: image-set('x.png' 1x)\">s</p>"));
        Assert.AreEqual("<p>s</p>", S("<p style=\"color:red\">s</p>", Without("style")));
    }

    [TestMethod]
    public void Attributes_FirstOfTheSameNameIsKept_InLowerCase()
    {
        Assert.AreEqual("<div id=\"a\" class=\"c\">t</div>", S("<div ID=a id=b class=c CLASS=d>t</div>"));
    }

    [TestMethod]
    public void Options_ChangeWhatIsKept()
    {
        var options = new SanitizeOptions { AllowDataAttributes = true, LinkRel = "nofollow noopener" };
        options.AllowedTags.Add("iframe");
        options.AllowedAttributes.Add("allowfullscreen");
        options.AllowedTags.Remove("b");

        Assert.AreEqual(
            "<iframe src=\"https://www.youtube.com/embed/x\" allowfullscreen></iframe>bold<span data-id=\"7\">s</span>"
            + "<a href=\"/x\" rel=\"nofollow noopener\">l</a><a>no href</a>",
            S("<iframe src=\"https://www.youtube.com/embed/x\" allowfullscreen srcdoc=\"<b>\"></iframe><b>bold</b>"
                + "<span data-id=7 onclick=x>s</span><a href=/x rel=author>l</a><a rel=author>no href</a>", options));

        // what is removed with its content keeps its text when it is taken out of the set
        var keepText = new SanitizeOptions();
        keepText.RemovedWithContent.Remove("button");
        Assert.AreEqual("Go", S("<button>Go</button>", keepText));
    }

    [TestMethod]
    public void ForbiddenTags_AreNeverWritten_EvenWhenAllowed()
    {
        var options = new SanitizeOptions();
        foreach (var tag in new[] { "script", "style", "svg", "math", "template", "noscript", "xmp", "plaintext", "base", "meta", "link" })
        {
            options.AllowedTags.Add(tag);
        }
        options.AllowedAttributes.Add("onclick");
        options.AllowedAttributes.Add("srcdoc");

        Assert.AreEqual("<p>x</p>", S("<script>alert(1)</script><style>p{}</style><svg><g></g></svg><p onclick=x srcdoc=y>x</p>", options));
    }

    [TestMethod]
    public void ATag_GivesTheTagWithItsContent_ADocumentAllOfIt()
    {
        var doc = new HtmlParser("<html><body><div id=a onclick=x><p>one</p><script>x</script></div><p>two</p></body></html>");

        Assert.AreEqual("<div id=\"a\"><p>one</p></div>", doc.Find(HtmlTag.Div).Sanitize());
        Assert.AreEqual("<div id=\"a\"><p>one</p></div><p>two</p>", doc.Sanitize());
        Assert.IsNull(((ParserTag?)null).Sanitize());
        Assert.IsNull(((HtmlParser?)null).Sanitize());
        Assert.IsNull(HtmlText.Sanitize(null));
        Assert.AreEqual("", HtmlText.Sanitize(""));
    }

    [TestMethod]
    public void TheSanitizedFixturePages_KeepTheirText()
    {
        foreach (var (path, url) in Fixtures.All)
        {
            var doc = new HtmlParser(Fixtures.Read(path), url);
            var sanitized = doc.Sanitize();

            // what a browser shows of the page is still there; only the text of buttons and other forms controls is not
            var before = doc.Find(HtmlTag.Body).Text() ?? "";
            var after = HtmlText.ToText(sanitized) ?? "";
            Assert.IsGreaterThanOrEqualTo(before.Length * 0.8, after.Length, path);
            Check(sanitized, SanitizeOptions.DefaultTags, SanitizeOptions.DefaultAttributes, path);
        }
    }

    // ----- the differential check -----

    private static readonly string[] Pieces =
    [
        "<p>", "</p>", "<div class=\"a\">", "</div>", "<b>", "</b>", "<i>", "</i>", "<a href=\"/x\">", "<a href=\"javascript:alert(1)\">",
        "<a href=\"jav&#x09;ascript:1\">", "<a href=\"  JAVASCRIPT:1\">", "</a>", "<img src=x onerror=alert(1)>", "<img src=\"data:text/html,x\">",
        "<script>alert(1)</script>", "<script>", "</script>", "<style>b{}</style>", "<svg><a href=javascript:1>s</a></svg>",
        "<math><mi>x</mi><style><img src=x onerror=1></style></math>", "<table>", "<tr>", "<td>", "</td>", "</table>",
        "<ul>", "<li>", "</ul>", "<!--", "-->", "<!-- c -->", "<![CDATA[", "]]>", "<noscript>", "</noscript>", "<template>",
        "</template>", "<textarea>", "</textarea>", "<title>", "</title>", "<xmp>", "</xmp>", "<iframe src=x>", "</iframe>",
        "<form>", "<input value=\"x\">", "<select><option>o</select>", "<p title=\"", "\">", "'", "\"", "<", ">", "&",
        "&lt;", "&gt;", "&amp;", "&quot;", "&#60;", "&#x3c;", "text", " ", "\n", "<span style=\"color:red;background:url(x)\">",
        "<span style=\"x:expression(1)\">", "</span>", "<p onclick=\"x\">", "<div/>", "<br/>", "<x-y>", "</x-y>", "<!doctype html>",
        "<base href=x>", "<meta charset=x>", "<plaintext>", "<object data=x>", "</object>", "<a href=\"&#106;avascript:1\">",
        "<img srcset=\"a 1x, javascript:1 2x\">", "<p/onclick=1>", "<img/src=x/onerror=1>", "<a href=x id=a id=b ID=c>",
        "<IMG SRC=x ONERROR=1>", "</", "<!", "<?xml x?>", "<h1>", "</h1>", "<blockquote>", "</blockquote>", "<code>", "</code>",
    ];

    [TestMethod]
    public void RandomMarkup_ABrowserSeesOnlyWhatIsAllowed_AndASecondPassChangesNothing()
    {
        // FHP_FUZZ_CASES and FHP_FUZZ_SEED run a longer check locally: FHP_FUZZ_CASES=1000000 dotnet test ...
        int cases = int.TryParse(Environment.GetEnvironmentVariable("FHP_FUZZ_CASES"), out int c) ? c : 3000;
        int seed = int.TryParse(Environment.GetEnvironmentVariable("FHP_FUZZ_SEED"), out int r) ? r : 20261122;
        var random = new Random(seed);
        var html = new StringBuilder();
        for (int n = 0; n < cases; n++)
        {
            html.Clear();
            int count = random.Next(1, 25);
            for (int k = 0; k < count; k++) html.Append(Pieces[random.Next(Pieces.Length)]);

            var input = html.ToString();
            var once = S(input);
            Check(once, SanitizeOptions.DefaultTags, SanitizeOptions.DefaultAttributes, input);
            Assert.AreEqual(once, S(once), "not stable: " + input);
        }
    }

    /// <summary>What a browser makes of <paramref name="sanitized"/> has only allowed elements and attributes and safe URLs.</summary>
    private static void Check(string sanitized, IEnumerable<string> tags, IEnumerable<string> attributes, string input)
    {
        var allowedTags = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase) { "html", "head", "body" };
        var allowedAttributes = new HashSet<string>(attributes, StringComparer.OrdinalIgnoreCase);
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(sanitized);

        foreach (var element in document.All)
        {
            Assert.AreEqual("http://www.w3.org/1999/xhtml", element.NamespaceUri, $"foreign element <{element.LocalName}> from: {input}");
            Assert.IsTrue(allowedTags.Contains(element.LocalName), $"<{element.LocalName}> from: {input}\n=> {sanitized}");

            foreach (var attribute in element.Attributes)
            {
                Assert.IsTrue(allowedAttributes.Contains(attribute.Name), $"{attribute.Name} on <{element.LocalName}> from: {input}");
                Assert.IsFalse(attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), input);

                if (attribute.Name is "href" or "src" or "cite" or "srcset")
                {
                    var compact = new string(attribute.Value.Where(c => c > ' ').ToArray()).ToLowerInvariant();
                    Assert.IsFalse(compact.Contains("javascript:") || compact.Contains("vbscript:") || compact.StartsWith("data:"),
                        $"{attribute.Name}=\"{attribute.Value}\" from: {input}");
                }
                if (attribute.Name == "style")
                {
                    var value = attribute.Value.ToLowerInvariant();
                    Assert.IsFalse(value.Contains("url") || value.Contains("expression") || value.Contains('\\'), $"style=\"{attribute.Value}\" from: {input}");
                }
            }
        }
    }

    private static SanitizeOptions Without(string attribute)
    {
        var options = new SanitizeOptions();
        options.AllowedAttributes.Remove(attribute);
        return options;
    }
}
