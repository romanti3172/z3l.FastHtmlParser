
// Member names are the tag / attribute names themselves; per-member docs would only repeat them.
#pragma warning disable CS1591

namespace z3l.FastHtmlParser;

/// <summary>
/// Common HTML / SVG / XML attribute names. The actual attribute name is the lower-case member name,
/// or its real form when that is not a valid C# identifier (DataSrc = "data-src", XlinkHref = "xlink:href", ...).
/// For attributes not listed here use the string overloads.
/// </summary>
public enum HtmlAttribute
{
    // Global
    Id,
    Class,
    Style,
    Title,
    Lang,
    Dir,
    Hidden,
    Tabindex,
    Role,
    /// <summary><c>aria-label</c></summary>
    AriaLabel,
    /// <summary><c>aria-hidden</c></summary>
    AriaHidden,

    // Links & media
    Href,
    Src,
    Srcset,
    Sizes,
    Alt,
    Rel,
    Target,
    Download,
    Hreflang,
    Poster,
    Loading,
    Width,
    Height,

    // Meta & microdata
    Content,
    Charset,
    /// <summary><c>http-equiv</c></summary>
    HttpEquiv,
    Property,
    Itemprop,
    Itemscope,
    Itemtype,

    // Forms
    Name,
    Value,
    Type,
    Placeholder,
    Checked,
    Selected,
    Disabled,
    Readonly,
    Required,
    Multiple,
    Maxlength,
    Min,
    Max,
    Step,
    Pattern,
    Autocomplete,
    For,
    Form,
    Action,
    Method,
    Enctype,
    Label,

    // Tables & misc
    Colspan,
    Rowspan,
    Datetime,

    // New members are appended below, so the numeric values of the members above do not change.

    // Global (more)
    Accesskey,
    Autocapitalize,
    Autofocus,
    Contenteditable,
    Draggable,
    Enterkeyhint,
    Inert,
    Inputmode,
    Is,
    Nonce,
    Part,
    Slot,
    Spellcheck,
    Translate,
    Popover,
    Itemid,
    Itemref,

    // ARIA
    /// <summary><c>aria-describedby</c></summary>
    AriaDescribedby,
    /// <summary><c>aria-labelledby</c></summary>
    AriaLabelledby,
    /// <summary><c>aria-expanded</c></summary>
    AriaExpanded,
    /// <summary><c>aria-controls</c></summary>
    AriaControls,
    /// <summary><c>aria-current</c></summary>
    AriaCurrent,
    /// <summary><c>aria-haspopup</c></summary>
    AriaHaspopup,
    /// <summary><c>aria-selected</c></summary>
    AriaSelected,
    /// <summary><c>aria-disabled</c></summary>
    AriaDisabled,
    /// <summary><c>aria-checked</c></summary>
    AriaChecked,
    /// <summary><c>aria-live</c></summary>
    AriaLive,

    // Common data-* (lazy images, ids, links)
    /// <summary><c>data-src</c></summary>
    DataSrc,
    /// <summary><c>data-srcset</c></summary>
    DataSrcset,
    /// <summary><c>data-lazy-src</c></summary>
    DataLazySrc,
    /// <summary><c>data-original</c></summary>
    DataOriginal,
    /// <summary><c>data-bg</c></summary>
    DataBg,
    /// <summary><c>data-background</c></summary>
    DataBackground,
    /// <summary><c>data-image</c></summary>
    DataImage,
    /// <summary><c>data-href</c></summary>
    DataHref,
    /// <summary><c>data-url</c></summary>
    DataUrl,
    /// <summary><c>data-link</c></summary>
    DataLink,
    /// <summary><c>data-id</c></summary>
    DataId,
    /// <summary><c>data-value</c></summary>
    DataValue,
    /// <summary><c>data-name</c></summary>
    DataName,
    /// <summary><c>data-type</c></summary>
    DataType,
    /// <summary><c>data-title</c></summary>
    DataTitle,
    /// <summary><c>data-text</c></summary>
    DataText,
    /// <summary><c>data-phone</c></summary>
    DataPhone,
    /// <summary><c>data-price</c></summary>
    DataPrice,
    /// <summary><c>data-target</c></summary>
    DataTarget,
    /// <summary><c>data-toggle</c></summary>
    DataToggle,
    /// <summary><c>data-content</c></summary>
    DataContent,

    // Event handlers
    Onclick,
    Onload,
    Onerror,
    Onchange,
    Oninput,
    Onsubmit,
    Onfocus,
    Onblur,
    Onkeydown,
    Onkeyup,
    Onmouseover,
    Onmouseout,

    // Links, media, embedding
    Ping,
    Referrerpolicy,
    Media,
    Crossorigin,
    Integrity,
    As,
    Blocking,
    Fetchpriority,
    Decoding,
    Usemap,
    Ismap,
    Autoplay,
    Controls,
    Loop,
    Muted,
    Playsinline,
    Preload,
    Kind,
    Srclang,
    Default,
    Data,
    Allow,
    Allowfullscreen,
    Sandbox,
    Srcdoc,
    Frameborder,
    Scrolling,
    Coords,
    Shape,
    Async,
    Defer,
    Nomodule,
    Cite,
    Manifest,

    // Meta, RDFa
    Scheme,
    Vocab,
    Typeof,
    Resource,
    Prefix,
    About,
    Datatype,

    // Forms (more)
    Accept,
    /// <summary><c>accept-charset</c></summary>
    AcceptCharset,
    Novalidate,
    Formaction,
    Formmethod,
    Formenctype,
    Formtarget,
    Formnovalidate,
    List,
    Dirname,
    Size,
    Cols,
    Rows,
    Wrap,
    Minlength,
    High,
    Low,
    Optimum,
    Open,
    Start,
    Reversed,

    // Tables
    Headers,
    Scope,
    Span,
    Abbr,

    // Legacy presentational
    Align,
    Valign,
    Bgcolor,
    Border,
    Cellpadding,
    Cellspacing,
    Color,
    Face,
    Background,
    Nowrap,
    Language,

    // SVG
    ViewBox,
    PreserveAspectRatio,
    D,
    Fill,
    /// <summary><c>fill-rule</c></summary>
    FillRule,
    /// <summary><c>fill-opacity</c></summary>
    FillOpacity,
    Stroke,
    /// <summary><c>stroke-width</c></summary>
    StrokeWidth,
    /// <summary><c>stroke-linecap</c></summary>
    StrokeLinecap,
    /// <summary><c>stroke-linejoin</c></summary>
    StrokeLinejoin,
    Opacity,
    Transform,
    Points,
    X,
    Y,
    X1,
    Y1,
    X2,
    Y2,
    Cx,
    Cy,
    R,
    Rx,
    Ry,
    Dx,
    Dy,
    Offset,
    /// <summary><c>stop-color</c></summary>
    StopColor,
    /// <summary><c>clip-path</c></summary>
    ClipPath,
    /// <summary><c>font-size</c></summary>
    FontSize,
    /// <summary><c>font-family</c></summary>
    FontFamily,
    /// <summary><c>text-anchor</c></summary>
    TextAnchor,

    // XML
    Xmlns,
    /// <summary><c>xmlns:xlink</c></summary>
    XmlnsXlink,
    /// <summary><c>xlink:href</c></summary>
    XlinkHref,
    /// <summary><c>xml:lang</c></summary>
    XmlLang,
    /// <summary><c>xml:space</c></summary>
    XmlSpace,
    Version,
    Encoding,
    Standalone,
}
