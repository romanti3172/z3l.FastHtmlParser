using AngleSharp.Html.Dom;

namespace z3l.FastHtmlParser.Tests;

/// <summary>Forms: action, method and the fields a browser sends, cross-checked with AngleSharp's form submission.</summary>
[TestClass]
public sealed class FormTests
{
    [TestMethod]
    public void Form_ActionMethodEnctype()
    {
        var doc = new HtmlParser("""
            <form id="f" action="/login?next=%2F&amp;x=1" method="POST"><input name="a" value="1"></form>
            <form><input name="b"></form>
            """, "https://example.com/account/page");

        var forms = doc.Forms().ToList();
        Assert.AreEqual(2, forms.Count);

        Assert.AreEqual("https://example.com/login?next=%2F&x=1", forms[0].Action);
        Assert.AreEqual("post", forms[0].Method);
        Assert.AreEqual("application/x-www-form-urlencoded", forms[0].Enctype);

        Assert.AreEqual("https://example.com/account/page", forms[1].Action);
        Assert.AreEqual("get", forms[1].Method);

        // a control gives its form
        Assert.AreEqual(forms[0].Tag, doc.Find("input", id: null).Form()!.Tag);
        Assert.IsNull(doc.Find(HtmlTag.Body).Form());
        Assert.IsNull(((ParserTag?)null).Form());
    }

    [TestMethod]
    public void Fields_AspNetPostback()
    {
        var doc = new HtmlParser("""
            <form method="post" action="./Default.aspx" id="form1">
              <input type="hidden" name="__VIEWSTATE" id="__VIEWSTATE" value="/wEPDwUK&#43;LTk=" />
              <input type="hidden" name="__EVENTVALIDATION" value="abc" />
              <input type="text" name="txtSearch" value="phone" />
              <input type="submit" name="btnSearch" value="Search" />
              <input type="submit" name="btnClear" value="Clear" />
            </form>
            """, "https://example.com/app/Default.aspx");

        var form = doc.Find(HtmlTag.Form).Form()!;

        Assert.AreEqual("__VIEWSTATE=/wEPDwUK+LTk= __EVENTVALIDATION=abc txtSearch=phone", Show(form.Fields()));

        var button = doc.Find(new TagQuery(HtmlTag.Input) { AttributeName = "name", AttributeValue = "btnSearch" });
        Assert.AreEqual("__VIEWSTATE=/wEPDwUK+LTk= __EVENTVALIDATION=abc txtSearch=phone btnSearch=Search", Show(form.Fields(button)));
    }

    [TestMethod]
    public void Fields_Rules()
    {
        var doc = new HtmlParser("""
            <form id="f">
              <input name="noValue"><input value="no name"><input name="off" disabled value="x">
              <input type="checkbox" name="c1" checked><input type="checkbox" name="c2" value="yes" checked>
              <input type="checkbox" name="c3" value="no">
              <input type="radio" name="r" value="1" checked><input type="radio" name="r" value="2" checked>
              <select name="s1"><option disabled>a<option value="b">B</select>
              <select name="s2"><option>a<option selected>  b  c  <option selected>d</select>
              <select name="s3" multiple><option selected>a<option>b<option selected value="c">C</select>
              <select name="s4" size="3"><option>a</select>
              <select name="s5"><optgroup disabled><option selected>a</optgroup></select>
              <textarea name="t">
            line1
            line2 &amp; more</textarea>
              <fieldset disabled><legend><input name="inLegend" value="1"></legend><input name="inFieldset" value="2"></fieldset>
              <input type="file" name="file"><input type="reset" name="reset"><button name="b">B</button>
            </form>
            <input name="outside" value="o" form="f"><input name="other" value="x" form="missing">
            """);

        Assert.AreEqual(
            "noValue= c1=on c2=yes r=2 s1=b s2=d s3=a s3=c t=line1\r\nline2 & more inLegend=1 outside=o",
            Show(doc.Find(HtmlTag.Form).Form()!.Fields()));
    }

    public static IEnumerable<object[]> FormSnippets =>
    [
        ["""<form method="post"><input name="a" value="1"><input type="hidden" name="h" value="x&amp;y"><input name="sp" value="a b+c"></form>"""],
        ["""<form method="post"><input type="checkbox" name="c" checked><input type="checkbox" name="c" value="2" checked><input type="checkbox" name="d"></form>"""],
        ["""<form method="post"><input type="radio" name="r" value="1" checked><input type="radio" name="r" value="2"><input type="radio" name="q" value="x" checked><input type="radio" name="q" value="y" checked></form>"""],
        ["""<form method="post"><select name="s"><option>one<option>two</select><select name="t"><option value="1">x<option value="2" selected>y</select></form>"""],
        ["""<form method="post"><select name="m" multiple><option selected>a<option>b<option selected>c</select><select name="e"></select></form>"""],
        ["""<form method="post"><select name="d"><option disabled>a<option>b</select><select name="g"><optgroup label="x" disabled><option>a</optgroup><option>b</select></form>"""],
        ["""<form method="post"><select name="z" size="4"><option>a<option>b</select><select name="w"><option selected disabled>a<option>b</select></form>"""],
        ["<form method=\"post\"><textarea name=\"t\">  first&lt;b&gt;</textarea><textarea name=\"u\">\nx\r\ny</textarea></form>"],
        ["""<form method="post"><fieldset disabled><input name="a" value="1"><legend><input name="b" value="2"></legend></fieldset><fieldset><input name="c" disabled value="3"><input name="d" value="4"></fieldset></form>"""],
        ["""<form method="post"><fieldset disabled><legend>t</legend><legend><input name="second" value="1"></legend></fieldset></form>"""],
        ["""<form method="post"><input name="a" value="1" form="nowhere"><input name="b" value="2"></form>"""],
        ["""<form method="post"><input type="submit" name="s" value="Go"><input type="image" name="i"><input type="file" name="f"><button name="b" value="v">B</button><input type="reset" name="r" value="R"></form>"""],
        ["""<form method="post"><input type="weird" name="w" value="v"><input name="ünï" value="значение"><input type="number" name="n" value="5"></form>"""],
    ];

    /// <summary>
    /// Where AngleSharp 1.x differs from the HTML standard and browsers, checked against the standard
    /// (constructing the entry list, value sanitization, form owner).
    /// </summary>
    [TestMethod]
    public void Fields_AsTheStandardSays_WhereAngleSharpDiffers()
    {
        // value sanitization: text fields drop line breaks; email and url also trim whitespace
        Assert.AreEqual("e=a@b.c u=http://x/ t=ab", Show(Fields(
            """<form><input type="email" name="e" value="  a@b.c  "><input type="url" name="u" value=" http://x/ "><input name="t" value="a&#10;b"></form>""")));

        // line breaks are sent as CR LF in every value, not only in a textarea
        Assert.AreEqual("h=l1\r\nl2", Show(Fields("""<form><input type="hidden" name="h" value="l1&#10;l2"></form>""")));

        // a control outside the form with form="id" belongs to it
        Assert.AreEqual("in=1 out=2 ta=x", Show(Fields(
            """<form id="f"><input name="in" value="1"></form><input name="out" value="2" form="f"><textarea name="ta" form="f">x</textarea>""")));

        // an empty name is left out; so is an <input type=button>, which is never a submitter
        Assert.AreEqual("n=5", Show(Fields(
            """<form><input name="" value="nameless"><input type="button" name="x" value="X"><input name="n" value="5"></form>""")));

        static IReadOnlyList<KeyValuePair<string, string>> Fields(string html) => new HtmlParser(html).Find(HtmlTag.Form).Form()!.Fields();
    }

    [TestMethod]
    [DynamicData(nameof(FormSnippets))]
    public void Fields_SameAsAngleSharp(string html)
    {
        html = "<!DOCTYPE html><html><body>" + html + "</body></html>";
        var ours = new HtmlParser(html, "https://example.com/");
        var reference = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        Assert.AreEqual(Show(Submitted(reference.Forms[0], null)), Show(ours.Find(HtmlTag.Form).Form()!.Fields()), html);
    }

    [TestMethod]
    [DynamicData(nameof(FormSnippets))]
    public void Fields_WithSubmitter_SameAsAngleSharp(string html)
    {
        html = "<!DOCTYPE html><html><body>" + html + "</body></html>";
        var ours = new HtmlParser(html, "https://example.com/");
        var reference = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        var ourButtons = ours.FindAll(HtmlTag.Input).Concat(ours.FindAll(HtmlTag.Button))
            .Where(t => t.Name == "button" || t.AttributeByName(HtmlAttribute.Type) is "submit" or "image")
            .OrderBy(t => t.Start).ToList();
        var theirButtons = reference.QuerySelectorAll("button, input[type=submit], input[type=image]").Cast<IHtmlElement>().ToList();
        Assert.AreEqual(theirButtons.Count, ourButtons.Count);

        for (int i = 0; i < ourButtons.Count; i++)
        {
            var form = ourButtons[i].Form()!;
            Assert.AreEqual(Show(Submitted(reference.Forms[0], theirButtons[i])), Show(form.Fields(ourButtons[i])), $"{html}\nsubmitter {i}");
        }
    }

    [TestMethod]
    public void Fields_LoginPage_SameAsAngleSharp()
    {
        var html = Fixtures.Read("quotes/login.html");
        var ours = new HtmlParser(html, "https://quotes.toscrape.com/login");
        var reference = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        var fields = ours.Find(HtmlTag.Form).Form()!.Fields();
        Assert.IsTrue(fields.Any(f => f.Key == "csrf_token" && f.Value.Length > 10));
        Assert.AreEqual(Show(Submitted(reference.Forms[0], null)), Show(fields));
    }

    /// <summary>The pairs AngleSharp would send: the urlencoded body of the submission, decoded.</summary>
    private static List<KeyValuePair<string, string>> Submitted(IHtmlFormElement form, IHtmlElement? submitter)
    {
        // an http(s) action: a document parsed without a URL is about:blank, and AngleSharp sends no body there
        form.Action = "https://example.com/submit";
        form.Method = "post";
        form.Enctype = "application/x-www-form-urlencoded";
        var request = form.GetSubmission(submitter)!;
        if (request.Body.CanSeek) request.Body.Position = 0;
        using var reader = new StreamReader(request.Body);
        var body = reader.ReadToEnd();

        var pairs = new List<KeyValuePair<string, string>>();
        if (body.Length == 0) return pairs;
        foreach (var part in body.Split('&'))
        {
            var eq = part.IndexOf('=');
            var name = eq < 0 ? part : part[..eq];
            var value = eq < 0 ? "" : part[(eq + 1)..];
            pairs.Add(new(Unescape(name), Unescape(value)));
        }
        return pairs;

        static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
    }

    private static string Show(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join(" ", fields.Select(f => $"{f.Key}={f.Value}"));
}
