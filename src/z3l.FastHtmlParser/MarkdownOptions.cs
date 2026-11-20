namespace z3l.FastHtmlParser;

/// <summary>What <c>Markdown()</c> keeps of a page besides its text.</summary>
/// <example>
/// <code>
/// var text = doc.Markdown(new MarkdownOptions { Links = false, Images = false });   // less to read for a language model
/// </code>
/// </example>
public sealed class MarkdownOptions
{
    internal static readonly MarkdownOptions Default = new();

    /// <summary>
    /// Links are written as <c>[text](address)</c>; <see langword="false"/> keeps only their text.
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool Links { get; init; } = true;

    /// <summary>
    /// Images are written as <c>![alt](address)</c>; <see langword="false"/> leaves them out.
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool Images { get; init; } = true;
}
