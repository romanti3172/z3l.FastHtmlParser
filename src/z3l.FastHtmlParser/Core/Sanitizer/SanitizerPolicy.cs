namespace z3l.FastHtmlParser.Core.Sanitizer;

/// <summary>
/// <see cref="SanitizeOptions"/> read once per call: the known tags as flags by <see cref="HtmlTag"/>, the sets
/// looked up by span. Made anew for each call, so a change of the options is seen by the next one.
/// </summary>
internal sealed class SanitizerPolicy
{
    // Never written, whatever the options say: a browser does not read their content as HTML (raw text, foreign
    // content, a template), or they change how the rest of the page is read (base, meta, link).
    private static readonly HtmlTag[] Forbidden =
    [
        HtmlTag.Script, HtmlTag.Style, HtmlTag.Template, HtmlTag.Noscript, HtmlTag.Noembed, HtmlTag.Noframes,
        HtmlTag.Xmp, HtmlTag.Plaintext, HtmlTag.Base, HtmlTag.Meta, HtmlTag.Link, HtmlTag.Frame, HtmlTag.Frameset,
        HtmlTag.Svg, HtmlTag.Math, HtmlTag.Comment, HtmlTag.Doctype, HtmlTag.XmlDeclaration, HtmlTag.Listing,
    ];

    public readonly bool[] Allowed = new bool[ParsingCore.TagNames.Length];
    public readonly bool[] Removed = new bool[ParsingCore.TagNames.Length];
    public readonly HashSet<string> AllowedTags;
    public readonly HashSet<string> RemovedTags;
    public readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> Attributes;
    public readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> UrlAttributes;
    public readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> Schemes;
    public readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> CssProperties;
    public readonly bool DataAttributes, DataImages, AbsoluteUrls;
    public readonly string? LinkRel;

    // Used by the article extractor: lazy images get their real address, see SanitizerWriter.WriteImageSource.
    public bool LazyImages;

    public SanitizerPolicy(SanitizeOptions options)
    {
        AllowedTags = Ignoring(options.AllowedTags);
        RemovedTags = Ignoring(options.RemovedWithContent);
        Attributes = Ignoring(options.AllowedAttributes).GetAlternateLookup<ReadOnlySpan<char>>();
        UrlAttributes = Ignoring(options.UrlAttributes).GetAlternateLookup<ReadOnlySpan<char>>();
        Schemes = Ignoring(options.AllowedSchemes).GetAlternateLookup<ReadOnlySpan<char>>();
        CssProperties = Ignoring(options.AllowedCssProperties).GetAlternateLookup<ReadOnlySpan<char>>();
        DataAttributes = options.AllowDataAttributes;
        DataImages = options.AllowDataImages;
        AbsoluteUrls = options.AbsoluteUrls;
        LinkRel = string.IsNullOrWhiteSpace(options.LinkRel) ? null : options.LinkRel.Trim();

        foreach (var name in AllowedTags)
        {
            if (ParsingCore.TryGetTag(name.AsSpan(), out var tag)) Allowed[(int)tag] = true;
        }
        foreach (var name in RemovedTags)
        {
            if (ParsingCore.TryGetTag(name.AsSpan(), out var tag)) Removed[(int)tag] = true;
        }
        foreach (var tag in Forbidden)
        {
            Allowed[(int)tag] = false;
        }
    }

    /// <summary>The set itself when it already ignores case (the sets of <see cref="SanitizeOptions"/> do), a copy otherwise.</summary>
    private static HashSet<string> Ignoring(HashSet<string> set)
    {
        return ReferenceEquals(set.Comparer, StringComparer.OrdinalIgnoreCase)
            ? set
            : new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether a custom tag (a name not in <see cref="HtmlTag"/>) is kept: it is in the set and is a plain name.</summary>
    public bool AllowsCustom(string name)
    {
        if (!AllowedTags.Contains(name)) return false;

        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        }
        return name.Length > 0 && char.IsAsciiLetter(name[0]);
    }
}
