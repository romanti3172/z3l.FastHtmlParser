namespace z3l.FastHtmlParser;

/// <summary>
/// Something that can be searched with <c>Find</c> / <c>FindAll</c>:
/// the whole document (<see cref="HtmlParser"/>) or the inside of one tag (<see cref="ParserTag"/>).
/// </summary>
public interface ITagSearchable
{
    /// <summary>
    /// The tags to search: <paramref name="tags"/>[<paramref name="first"/>..], stopping at the first tag
    /// whose End is greater than <paramref name="end"/>.
    /// </summary>
    internal void GetScope(out List<ParserTag> tags, out int first, out int end);
}
