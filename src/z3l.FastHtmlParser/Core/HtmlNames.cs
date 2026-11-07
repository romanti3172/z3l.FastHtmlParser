namespace z3l.FastHtmlParser.Core;

/// <summary>
/// Real names of <see cref="HtmlTag"/> / <see cref="HtmlAttribute"/> members: the lower-case member name,
/// except the names that are not valid C# identifiers (listed in the switches below).
/// No reflection: building the tables takes ~1 ms on first use and works the same under trimming / Native AOT.
/// </summary>
internal static class HtmlNames
{
    internal static string[] BuildTagNames()
    {
        var members = Enum.GetNames<HtmlTag>();
        var values = Enum.GetValues<HtmlTag>();
        var names = new string[members.Length];
        var seen = new HashSet<string>(members.Length, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < members.Length; i++)
        {
            var name = Special(values[i]) ?? members[i].ToLowerInvariant();
            Store(names, (int)values[i], name, seen, members[i]);
        }

        return names;
    }

    internal static string[] BuildAttributeNames()
    {
        var members = Enum.GetNames<HtmlAttribute>();
        var values = Enum.GetValues<HtmlAttribute>();
        var names = new string[members.Length];
        var seen = new HashSet<string>(members.Length, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < members.Length; i++)
        {
            var name = Special(values[i]) ?? members[i].ToLowerInvariant();
            Store(names, (int)values[i], name, seen, members[i]);
        }

        return names;
    }

    private static void Store(string[] names, int index, string name, HashSet<string> seen, string member)
    {
        // The tables are indexed by the enum value: members must stay 0, 1, 2, ... without gaps.
        if ((uint)index >= (uint)names.Length) throw new InvalidOperationException($"{member}: enum values must be contiguous from 0");
        if (!seen.Add(name)) throw new InvalidOperationException($"{member}: duplicate name '{name}'");
        names[index] = name;
    }

    private static string? Special(HtmlTag tag) => tag switch
    {
        HtmlTag.Comment => "!--",
        HtmlTag.Doctype => "!doctype",
        HtmlTag.XmlDeclaration => "?xml",
        _ => null,
    };

    private static string? Special(HtmlAttribute attribute) => attribute switch
    {
        HtmlAttribute.AriaLabel => "aria-label",
        HtmlAttribute.AriaHidden => "aria-hidden",
        HtmlAttribute.AriaDescribedby => "aria-describedby",
        HtmlAttribute.AriaLabelledby => "aria-labelledby",
        HtmlAttribute.AriaExpanded => "aria-expanded",
        HtmlAttribute.AriaControls => "aria-controls",
        HtmlAttribute.AriaCurrent => "aria-current",
        HtmlAttribute.AriaHaspopup => "aria-haspopup",
        HtmlAttribute.AriaSelected => "aria-selected",
        HtmlAttribute.AriaDisabled => "aria-disabled",
        HtmlAttribute.AriaChecked => "aria-checked",
        HtmlAttribute.AriaLive => "aria-live",
        HtmlAttribute.HttpEquiv => "http-equiv",
        HtmlAttribute.AcceptCharset => "accept-charset",
        HtmlAttribute.DataSrc => "data-src",
        HtmlAttribute.DataSrcset => "data-srcset",
        HtmlAttribute.DataLazySrc => "data-lazy-src",
        HtmlAttribute.DataOriginal => "data-original",
        HtmlAttribute.DataBg => "data-bg",
        HtmlAttribute.DataBackground => "data-background",
        HtmlAttribute.DataImage => "data-image",
        HtmlAttribute.DataHref => "data-href",
        HtmlAttribute.DataUrl => "data-url",
        HtmlAttribute.DataLink => "data-link",
        HtmlAttribute.DataId => "data-id",
        HtmlAttribute.DataValue => "data-value",
        HtmlAttribute.DataName => "data-name",
        HtmlAttribute.DataType => "data-type",
        HtmlAttribute.DataTitle => "data-title",
        HtmlAttribute.DataText => "data-text",
        HtmlAttribute.DataPhone => "data-phone",
        HtmlAttribute.DataPrice => "data-price",
        HtmlAttribute.DataTarget => "data-target",
        HtmlAttribute.DataToggle => "data-toggle",
        HtmlAttribute.DataContent => "data-content",
        HtmlAttribute.FillRule => "fill-rule",
        HtmlAttribute.FillOpacity => "fill-opacity",
        HtmlAttribute.StrokeWidth => "stroke-width",
        HtmlAttribute.StrokeLinecap => "stroke-linecap",
        HtmlAttribute.StrokeLinejoin => "stroke-linejoin",
        HtmlAttribute.StopColor => "stop-color",
        HtmlAttribute.ClipPath => "clip-path",
        HtmlAttribute.FontSize => "font-size",
        HtmlAttribute.FontFamily => "font-family",
        HtmlAttribute.TextAnchor => "text-anchor",
        HtmlAttribute.XmlnsXlink => "xmlns:xlink",
        HtmlAttribute.XlinkHref => "xlink:href",
        HtmlAttribute.XmlLang => "xml:lang",
        HtmlAttribute.XmlSpace => "xml:space",
        _ => null,
    };
}
