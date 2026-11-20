namespace z3l.FastHtmlParser.Core.Markdown;

/// <summary>What a tag is in Markdown.</summary>
internal enum MdKind : byte
{
    Inline,         // span, font, label, custom tags, ...: only its content counts
    Skip,           // script, style, head, svg, select, ...: nothing of it is text of the page
    Block,          // div, p, section, ...: a paragraph of its own
    Heading,        // h1-h6

    // inline pairs, in this order (see MarkdownConverter.Wrapper)
    Strong,
    Em,
    Del,
    Sub,
    Sup,
    Quote,
    Link,

    Image,
    Code,           // code, kbd, samp, tt
    Pre,            // pre, listing, xmp, plaintext
    Br,
    Hr,
    List,           // ul, menu, dir
    OrderedList,
    ListItem,
    Blockquote,
    Term,           // dt
    Definition,     // dd
    Table,
    TableRow,
    TableCell,      // td, th
    TablePart,      // thead, tbody, tfoot
    Caption,
    Input,
}

internal static class MarkdownKinds
{
    /// <summary>The kind of every <see cref="HtmlTag"/>, by its value.</summary>
    internal static readonly MdKind[] Of = Build();

    private static MdKind[] Build()
    {
        var kinds = new MdKind[ParsingCore.TagNames.Length]; // Inline

        Set(kinds, MdKind.Skip,
            // not shown by a browser
            HtmlTag.Head, HtmlTag.Title, HtmlTag.Script, HtmlTag.Style, HtmlTag.Noscript, HtmlTag.Template,
            HtmlTag.Noframes, HtmlTag.Noembed, HtmlTag.Comment, HtmlTag.Doctype, HtmlTag.XmlDeclaration,
            HtmlTag.Meta, HtmlTag.Link, HtmlTag.Base, HtmlTag.Basefont, HtmlTag.Bgsound, HtmlTag.Param,
            HtmlTag.Source, HtmlTag.Track, HtmlTag.Col, HtmlTag.Colgroup, HtmlTag.Datalist, HtmlTag.Wbr,
            HtmlTag.Area, HtmlTag.Map, HtmlTag.Frame, HtmlTag.Frameset, HtmlTag.Nextid, HtmlTag.Spacer,
            // shown, but not as text: pictures, players, form controls
            HtmlTag.Svg, HtmlTag.Canvas, HtmlTag.Iframe, HtmlTag.Embed, HtmlTag.Object, HtmlTag.Applet,
            HtmlTag.Audio, HtmlTag.Video, HtmlTag.Select, HtmlTag.Option, HtmlTag.Optgroup, HtmlTag.Textarea,
            HtmlTag.Keygen, HtmlTag.Isindex, HtmlTag.Progress, HtmlTag.Meter);

        Set(kinds, MdKind.Block,
            HtmlTag.Html, HtmlTag.Body, HtmlTag.P, HtmlTag.Div, HtmlTag.Section, HtmlTag.Article, HtmlTag.Aside,
            HtmlTag.Nav, HtmlTag.Header, HtmlTag.Footer, HtmlTag.Main, HtmlTag.Address, HtmlTag.Figure,
            HtmlTag.Figcaption, HtmlTag.Form, HtmlTag.Fieldset, HtmlTag.Legend, HtmlTag.Details, HtmlTag.Summary,
            HtmlTag.Dialog, HtmlTag.Dl, HtmlTag.Hgroup, HtmlTag.Search, HtmlTag.Center, HtmlTag.Marquee,
            HtmlTag.Multicol);

        Set(kinds, MdKind.Heading, HtmlTag.H1, HtmlTag.H2, HtmlTag.H3, HtmlTag.H4, HtmlTag.H5, HtmlTag.H6);

        Set(kinds, MdKind.Strong, HtmlTag.Strong, HtmlTag.B);
        Set(kinds, MdKind.Em, HtmlTag.Em, HtmlTag.I, HtmlTag.Cite, HtmlTag.Dfn, HtmlTag.Var);
        Set(kinds, MdKind.Del, HtmlTag.Del, HtmlTag.S, HtmlTag.Strike);
        Set(kinds, MdKind.Sub, HtmlTag.Sub);
        Set(kinds, MdKind.Sup, HtmlTag.Sup);
        Set(kinds, MdKind.Quote, HtmlTag.Q);
        Set(kinds, MdKind.Link, HtmlTag.A);

        Set(kinds, MdKind.Image, HtmlTag.Img);
        Set(kinds, MdKind.Code, HtmlTag.Code, HtmlTag.Kbd, HtmlTag.Samp, HtmlTag.Tt);
        Set(kinds, MdKind.Pre, HtmlTag.Pre, HtmlTag.Listing, HtmlTag.Xmp, HtmlTag.Plaintext);
        Set(kinds, MdKind.Br, HtmlTag.Br);
        Set(kinds, MdKind.Hr, HtmlTag.Hr);

        Set(kinds, MdKind.List, HtmlTag.Ul, HtmlTag.Menu, HtmlTag.Dir);
        Set(kinds, MdKind.OrderedList, HtmlTag.Ol);
        Set(kinds, MdKind.ListItem, HtmlTag.Li);
        Set(kinds, MdKind.Blockquote, HtmlTag.Blockquote);
        Set(kinds, MdKind.Term, HtmlTag.Dt);
        Set(kinds, MdKind.Definition, HtmlTag.Dd);

        Set(kinds, MdKind.Table, HtmlTag.Table);
        Set(kinds, MdKind.TableRow, HtmlTag.Tr);
        Set(kinds, MdKind.TableCell, HtmlTag.Td, HtmlTag.Th);
        Set(kinds, MdKind.TablePart, HtmlTag.Thead, HtmlTag.Tbody, HtmlTag.Tfoot);
        Set(kinds, MdKind.Caption, HtmlTag.Caption);
        Set(kinds, MdKind.Input, HtmlTag.Input);

        return kinds;
    }

    private static void Set(MdKind[] kinds, MdKind kind, params HtmlTag[] tags)
    {
        foreach (var tag in tags) kinds[(int)tag] = kind;
    }
}
