namespace z3l.FastHtmlParser;

/// <summary>
/// What <c>Sanitize()</c> keeps of untrusted HTML: the tags, attributes, URL schemes and CSS properties that are allowed.
/// Every set starts with the defaults and can be changed in place.
/// </summary>
/// <remarks>
/// <para>
/// The result is written anew, never copied from the input: every kept tag is written by its lower-case name with
/// its kept attributes in double quotes, every text is escaped, and every element that is opened is closed.
/// So a browser reads the result as the sanitizer wrote it, whatever was in the input.
/// </para>
/// <para>
/// Whatever is in the sets, these are never written: attributes whose name starts with <c>on</c> (event handlers),
/// <c>srcdoc</c>, and the tags <c>script</c>, <c>style</c>, <c>template</c>, <c>noscript</c>, <c>noembed</c>,
/// <c>noframes</c>, <c>xmp</c>, <c>plaintext</c>, <c>base</c>, <c>meta</c>, <c>link</c>, <c>frame</c>, <c>frameset</c>,
/// <c>svg</c> and <c>math</c>: in a browser their content is not read as HTML, so escaping it does not make it safe.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var options = new SanitizeOptions();
/// options.AllowedTags.Add("iframe");             // video embeds
/// options.AllowedAttributes.Add("allowfullscreen");
/// options.AllowedAttributes.Remove("style");
/// string safe = HtmlText.Sanitize(comment, options);
/// </code>
/// </example>
public sealed class SanitizeOptions
{
    // In a class of its own: made after the default lists below, which the constructor reads.
    internal static SanitizeOptions Default => DefaultHolder.Instance;

    private static class DefaultHolder
    {
        public static readonly SanitizeOptions Instance = new();
    }

    /// <summary>
    /// Tags that are kept. A tag that is not in the set is left out, its content stays (unless the tag is in
    /// <see cref="RemovedWithContent"/>). Default: the tags of text, lists, tables, links, images and the page
    /// sections (<c>p</c>, <c>b</c>, <c>a</c>, <c>img</c>, <c>table</c>, <c>article</c>, ...); no forms, frames or media.
    /// </summary>
    public HashSet<string> AllowedTags { get; init; } = new(DefaultTags, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Attributes that are kept on every allowed tag. Default: <c>href</c>, <c>src</c>, <c>alt</c>, <c>title</c>,
    /// <c>class</c>, <c>id</c>, <c>style</c>, the table and list attributes, and the old presentation attributes
    /// (<c>align</c>, <c>width</c>, <c>color</c>, ...).
    /// </summary>
    public HashSet<string> AllowedAttributes { get; init; } = new(DefaultAttributes, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Attributes that hold a URL: a value whose scheme is not in <see cref="AllowedSchemes"/> is left out with its
    /// attribute (<c>javascript:</c>, <c>data:</c>, <c>vbscript:</c>). <c>srcset</c> is checked URL by URL.
    /// </summary>
    public HashSet<string> UrlAttributes { get; init; } = new(DefaultUrlAttributes, StringComparer.OrdinalIgnoreCase);

    /// <summary>URL schemes that are kept. Default: <c>http</c>, <c>https</c>, <c>mailto</c>, <c>tel</c>. A relative URL has none and is kept.</summary>
    public HashSet<string> AllowedSchemes { get; init; } = new(DefaultSchemes, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CSS properties kept in a <c>style</c> attribute (when <c>style</c> is allowed): colors, fonts, text alignment,
    /// margins, padding, borders, sizes. A value with <c>url(</c>, <c>expression</c>, a backslash, a comment or
    /// <c>&lt;</c> is left out with its property.
    /// </summary>
    public HashSet<string> AllowedCssProperties { get; init; } = new(DefaultCssProperties, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tags that are left out together with their content: script, style, forms controls with their options,
    /// frames, objects, media players with their fallback text, <c>title</c>, <c>svg</c>, <c>math</c>.
    /// Removing a tag from this set keeps its content as text; an allowed tag is kept whether it is here or not.
    /// </summary>
    public HashSet<string> RemovedWithContent { get; init; } = new(DefaultRemovedWithContent, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Keeps <c>data-*</c> attributes. Default: <see langword="false"/>.
    /// </summary>
    public bool AllowDataAttributes { get; init; }

    /// <summary>
    /// Keeps images inlined in the page: a <c>data:image/...</c> URL (png, gif, jpeg, webp, avif, bmp, ico; not svg)
    /// in <c>src</c> of <c>img</c>. Default: <see langword="false"/>.
    /// </summary>
    public bool AllowDataImages { get; init; }

    /// <summary>
    /// Relative URLs are made absolute when the document has a page URL (<c>new HtmlParser(html, url)</c>).
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool AbsoluteUrls { get; init; } = true;

    /// <summary>
    /// When set, every kept link (<c>a</c> with <c>href</c>) gets this <c>rel</c>, in place of its own:
    /// <c>"nofollow noopener"</c> for links in comments. Default: <see langword="null"/> (the link's own <c>rel</c>, if allowed).
    /// </summary>
    public string? LinkRel { get; init; }

    /// <summary>The default tags of <see cref="AllowedTags"/>.</summary>
    public static IReadOnlyList<string> DefaultTags { get; } =
    [
        "a", "abbr", "acronym", "address", "area", "article", "aside", "b", "bdi", "bdo", "big", "blockquote", "br",
        "caption", "center", "cite", "code", "col", "colgroup", "data", "dd", "del", "details", "dfn", "dir", "div",
        "dl", "dt", "em", "figcaption", "figure", "font", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header",
        "hgroup", "hr", "i", "img", "ins", "kbd", "li", "main", "map", "mark", "menu", "nav", "ol", "p", "picture",
        "pre", "q", "rp", "rt", "ruby", "s", "samp", "section", "small", "source", "span", "strike", "strong", "sub",
        "summary", "sup", "table", "tbody", "td", "tfoot", "th", "thead", "time", "tr", "tt", "u", "ul", "var", "wbr",
    ];

    /// <summary>The default attributes of <see cref="AllowedAttributes"/>.</summary>
    public static IReadOnlyList<string> DefaultAttributes { get; } =
    [
        "abbr", "align", "alt", "axis", "bgcolor", "border", "cellpadding", "cellspacing", "cite", "class", "clear",
        "color", "cols", "colspan", "compact", "coords", "datetime", "dir", "face", "headers", "height", "href",
        "hreflang", "hspace", "id", "lang", "nowrap", "open", "rel", "rev", "reversed", "rowspan", "rules", "scope",
        "shape", "size", "sizes", "span", "src", "srcset", "start", "style", "summary", "target", "title", "type",
        "usemap", "valign", "value", "vspace", "width",
    ];

    /// <summary>The default attributes of <see cref="UrlAttributes"/>.</summary>
    public static IReadOnlyList<string> DefaultUrlAttributes { get; } =
    [
        "action", "background", "cite", "data", "formaction", "href", "icon", "longdesc", "manifest", "ping",
        "poster", "profile", "src", "srcset", "usemap", "xlink:href",
    ];

    /// <summary>The default schemes of <see cref="AllowedSchemes"/>.</summary>
    public static IReadOnlyList<string> DefaultSchemes { get; } = ["http", "https", "mailto", "tel"];

    /// <summary>The default properties of <see cref="AllowedCssProperties"/>.</summary>
    public static IReadOnlyList<string> DefaultCssProperties { get; } =
    [
        "background-color", "border", "border-bottom", "border-bottom-color", "border-bottom-style",
        "border-bottom-width", "border-collapse", "border-color", "border-left", "border-left-color",
        "border-left-style", "border-left-width", "border-radius", "border-right", "border-right-color",
        "border-right-style", "border-right-width", "border-spacing", "border-style", "border-top", "border-top-color",
        "border-top-style", "border-top-width", "border-width", "caption-side", "clear", "color", "direction",
        "display", "float", "font", "font-family", "font-size", "font-style", "font-variant", "font-weight", "height",
        "letter-spacing", "line-height", "list-style-position", "list-style-type", "margin", "margin-bottom",
        "margin-left", "margin-right", "margin-top", "max-height", "max-width", "min-height", "min-width", "padding",
        "padding-bottom", "padding-left", "padding-right", "padding-top", "table-layout", "text-align",
        "text-decoration", "text-indent", "text-transform", "unicode-bidi", "vertical-align", "white-space", "width",
        "word-break", "word-spacing", "word-wrap",
    ];

    /// <summary>The default tags of <see cref="RemovedWithContent"/>.</summary>
    public static IReadOnlyList<string> DefaultRemovedWithContent { get; } =
    [
        "applet", "audio", "button", "canvas", "datalist", "embed", "frame", "frameset", "iframe", "math",
        "noembed", "noframes", "noscript", "object", "plaintext", "script", "select", "style", "svg", "template",
        "textarea", "title", "video", "xmp",
    ];
}
