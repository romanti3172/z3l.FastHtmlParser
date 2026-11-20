using z3l.FastHtmlParser.Core;
using z3l.FastHtmlParser.Core.Css;

namespace z3l.FastHtmlParser;

/// <summary>
/// A <c>&lt;form&gt;</c> and what a browser would send when it is submitted:
/// <see cref="Action"/>, <see cref="Method"/>, <see cref="Enctype"/> and the <see cref="Fields"/>.
/// Get one with <see cref="ParserTagFormExtensions.Form"/>.
/// </summary>
public sealed class HtmlForm
{
    internal HtmlForm(ParserTag tag)
    {
        Tag = tag;
    }

    /// <summary>The <c>&lt;form&gt;</c> tag.</summary>
    public ParserTag Tag { get; }

    /// <summary>
    /// The URL the form is sent to: the <c>action</c> attribute made absolute against the page URL,
    /// or the page URL when the form has no action.
    /// </summary>
    public string Action => UriCore.CorrectLink(SpanHandlingCore.SpanAttributeByName(Tag, HtmlAttribute.Action), Tag.Document, null);

    /// <summary><c>get</c> (the default), <c>post</c> or <c>dialog</c>, in lower case.</summary>
    public string Method => Tag.AttributeByName(HtmlAttribute.Method).Trim().ToLowerInvariant() switch
    {
        "post" => "post",
        "dialog" => "dialog",
        _ => "get",
    };

    /// <summary>
    /// <c>application/x-www-form-urlencoded</c> (the default), <c>multipart/form-data</c> or <c>text/plain</c>.
    /// </summary>
    public string Enctype => Tag.AttributeByName(HtmlAttribute.Enctype).Trim().ToLowerInvariant() switch
    {
        "multipart/form-data" => "multipart/form-data",
        "text/plain" => "text/plain",
        _ => "application/x-www-form-urlencoded",
    };

    /// <summary>
    /// The controls of the form in document order: the <c>input</c>, <c>select</c>, <c>textarea</c> and <c>button</c>
    /// tags inside it, and those outside it that name it with <c>form="id"</c>.
    /// </summary>
    public IReadOnlyList<ParserTag> Controls()
    {
        var controls = new List<ParserTag>();
        foreach (var tag in Tag.Document)
        {
            if (IsControl(tag) && ParserTagFormExtensions.Owner(tag) == Tag) controls.Add(tag);
        }
        return controls;
    }

    /// <summary>
    /// The name / value pairs a browser sends when the form is submitted, in document order: ready for
    /// <c>new FormUrlEncodedContent(form.Fields())</c>. As in a browser:
    /// <list type="bullet">
    /// <item>controls without a name and disabled ones (<c>disabled</c>, or inside <c>&lt;fieldset disabled&gt;</c>) are left out;</item>
    /// <item>a checkbox or radio button is sent only when checked, with its value or <c>on</c>;
    /// of the radio buttons with one name only the last checked one is;</item>
    /// <item>a <c>select</c> sends its selected options; one without <c>multiple</c> sends its last selected option,
    /// or the first option that is not disabled when none is selected;
    /// an option without a <c>value</c> sends its text;</item>
    /// <item>a <c>textarea</c> sends its text; line breaks are sent as CR LF;</item>
    /// <item>submit buttons are sent only as the <paramref name="submitter"/>: pass the button that is "clicked"
    /// (an ASP.NET postback often needs it); an image button sends <c>name.x=0</c> and <c>name.y=0</c>;</item>
    /// <item>file inputs are left out.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Fields(ParserTag? submitter = null)
    {
        var controls = Controls();
        var fields = new List<KeyValuePair<string, string>>(controls.Count);

        // radio buttons with one name: only the last checked one is checked
        Dictionary<string, ParserTag>? checkedRadios = null;
        foreach (var control in controls)
        {
            if (control.Key == (int)HtmlTag.Input && InputType(control) == "radio" && CssMatcher.HasAttribute(control, "checked"))
            {
                (checkedRadios ??= new())[control.AttributeByName(HtmlAttribute.Name)] = control;
            }
        }

        foreach (var control in controls)
        {
            if (IsDisabled(control)) continue;

            var name = control.AttributeByName(HtmlAttribute.Name);

            switch ((HtmlTag)control.Key)
            {
                case HtmlTag.Input:
                    var type = InputType(control);
                    switch (type)
                    {
                        case "image":
                            if (control != submitter) break;
                            Add(fields, name.Length == 0 ? "x" : name + ".x", "0");
                            Add(fields, name.Length == 0 ? "y" : name + ".y", "0");
                            break;

                        case "submit":
                            if (control == submitter && name.Length > 0) Add(fields, name, control.AttributeByName(HtmlAttribute.Value));
                            break;

                        case "reset" or "button" or "file":
                            break;

                        case "checkbox" or "radio":
                            if (name.Length == 0 || !CssMatcher.HasAttribute(control, "checked")) break;
                            if (type == "radio" && checkedRadios![name] != control) break;
                            Add(fields, name, CssMatcher.HasAttribute(control, "value") ? control.AttributeByName(HtmlAttribute.Value) : "on");
                            break;

                        default:
                            if (name.Length > 0) Add(fields, name, InputValue(control, type));
                            break;
                    }
                    break;

                case HtmlTag.Button:
                    if (control == submitter && name.Length > 0 && ButtonType(control) == "submit")
                    {
                        Add(fields, name, control.AttributeByName(HtmlAttribute.Value));
                    }
                    break;

                case HtmlTag.Select:
                    if (name.Length > 0) AddSelected(fields, control, name);
                    break;

                case HtmlTag.Textarea:
                    if (name.Length > 0) Add(fields, name, TextareaValue(control));
                    break;
            }
        }

        return fields;
    }

    private static bool IsControl(ParserTag tag) =>
        tag.Key is (int)HtmlTag.Input or (int)HtmlTag.Select or (int)HtmlTag.Textarea or (int)HtmlTag.Button;

    private static string InputType(ParserTag input) => input.AttributeByName(HtmlAttribute.Type).Trim().ToLowerInvariant();

    private static string ButtonType(ParserTag button) => button.AttributeByName(HtmlAttribute.Type).Trim().ToLowerInvariant() switch
    {
        "reset" => "reset",
        "button" => "button",
        _ => "submit",
    };

    /// <summary>The value of an input as a browser sanitizes it: text fields drop line breaks, url and email also trim.</summary>
    private static string InputValue(ParserTag input, string type)
    {
        var value = input.AttributeByName(HtmlAttribute.Value);
        switch (type)
        {
            case "" or "text" or "search" or "tel" or "password":
                return StripLineBreaks(value);
            case "url" or "email":
                return StripLineBreaks(value).Trim(' ', '\t', '\n', '\f', '\r');
            default:
                // an unknown type is a text field
                return type is "hidden" or "checkbox" or "radio" or "number" or "range" or "color" or "date" or "month"
                    or "week" or "time" or "datetime-local"
                    ? value
                    : StripLineBreaks(value);
        }
    }

    private static string StripLineBreaks(string value) =>
        value.AsSpan().IndexOfAny('\r', '\n') < 0 ? value : value.Replace("\r", "").Replace("\n", "");

    /// <summary>The text of a textarea: character references decoded, a line break right after the start tag dropped.</summary>
    private static string TextareaValue(ParserTag textarea)
    {
        // the content as written (Body() trims it), line breaks normalized to LF by the decoder
        if (textarea.InnerStart <= 0 || textarea.InnerEnd <= 0) return "";
        var raw = textarea.Document.Source.Slice(textarea.InnerStart, textarea.InnerEnd - textarea.InnerStart + 1);

        var text = HtmlDecodeCore.HtmlDecode(raw);
        return text.StartsWith('\n') ? text[1..] : text;
    }

    private static void AddSelected(List<KeyValuePair<string, string>> fields, ParserTag select, string name)
    {
        var options = select.FindAll(HtmlTag.Option).ToList();
        bool multiple = CssMatcher.HasAttribute(select, "multiple");

        if (multiple)
        {
            foreach (var option in options)
            {
                if (CssMatcher.HasAttribute(option, "selected") && !IsDisabledOption(option)) Add(fields, name, OptionValue(option));
            }
            return;
        }

        // one option at most: the last selected one; with none, a drop-down (display size 1) selects the first enabled one
        var chosen = options.LastOrDefault(o => CssMatcher.HasAttribute(o, "selected"));
        if (chosen == null && DisplaySize(select) == 1) chosen = options.FirstOrDefault(o => !IsDisabledOption(o));

        if (chosen != null && !IsDisabledOption(chosen)) Add(fields, name, OptionValue(chosen));
    }

    private static int DisplaySize(ParserTag select) =>
        int.TryParse(select.AttributeByName(HtmlAttribute.Size).Trim(), out var size) && size > 0 ? size : 1;

    private static string OptionValue(ParserTag option) =>
        CssMatcher.HasAttribute(option, "value") ? option.AttributeByName(HtmlAttribute.Value) : option.Text() ?? "";

    private static bool IsDisabledOption(ParserTag option)
    {
        if (CssMatcher.HasAttribute(option, "disabled")) return true;

        var parent = option.Parent();
        return parent != null && parent.Key == (int)HtmlTag.Optgroup && CssMatcher.HasAttribute(parent, "disabled");
    }

    /// <summary>
    /// <c>disabled</c>, or inside a <c>&lt;fieldset disabled&gt;</c> but not inside the first <c>&lt;legend&gt;</c> of that fieldset.
    /// </summary>
    private static bool IsDisabled(ParserTag control)
    {
        if (CssMatcher.HasAttribute(control, "disabled")) return true;

        var child = control;
        for (var parent = control.Parent(); parent != null; child = parent, parent = parent.Parent())
        {
            if (parent.Key != (int)HtmlTag.Fieldset || !CssMatcher.HasAttribute(parent, "disabled")) continue;

            var legend = parent.Children().FirstOrDefault(c => c.Key == (int)HtmlTag.Legend);
            if (legend != child) return true;
        }

        return false;
    }

    /// <summary>A name / value pair with line breaks as CR LF, as a browser sends them.</summary>
    private static void Add(List<KeyValuePair<string, string>> fields, string name, string value)
    {
        fields.Add(new(NormalizeLineBreaks(name), NormalizeLineBreaks(value)));
    }

    private static string NormalizeLineBreaks(string s)
    {
        if (s.AsSpan().IndexOfAny('\r', '\n') < 0) return s;
        return s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Method.ToUpperInvariant()} {Action}";
}

/// <summary>Forms: <see cref="Form"/> gives the form a tag is or belongs to.</summary>
public static class ParserTagFormExtensions
{
    /// <summary>
    /// The form of a tag: the tag itself when it is a <c>&lt;form&gt;</c>; for a control with <c>form="id"</c>
    /// the form with that id; otherwise the nearest enclosing <c>&lt;form&gt;</c>. Null when there is none.
    /// </summary>
    public static HtmlForm? Form(this ParserTag? tag)
    {
        if (tag == null) return null;
        if (tag.Key == (int)HtmlTag.Form) return new HtmlForm(tag);

        var owner = Owner(tag);
        return owner == null ? null : new HtmlForm(owner);
    }

    /// <summary>All forms of the document, in document order.</summary>
    public static IEnumerable<HtmlForm> Forms(this ITagSearchable? scope) =>
        scope.FindAll(HtmlTag.Form).Select(f => new HtmlForm(f));

    /// <summary>The form owner of a tag, as a browser finds it (the form attribute first, then the nearest enclosing form).</summary>
    internal static ParserTag? Owner(ParserTag tag)
    {
        if (tag.Key is (int)HtmlTag.Input or (int)HtmlTag.Select or (int)HtmlTag.Textarea or (int)HtmlTag.Button
            && CssMatcher.HasAttribute(tag, "form"))
        {
            var id = tag.AttributeByName(HtmlAttribute.Form);
            if (id.Length == 0) return null;

            var target = tag.Document.Find(new TagQuery { Id = id });
            return target != null && target.Key == (int)HtmlTag.Form ? target : null;
        }

        return tag.Parent(HtmlTag.Form);
    }
}
