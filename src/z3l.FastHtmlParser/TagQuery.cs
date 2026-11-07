using System.Runtime.CompilerServices;
using z3l.FastHtmlParser.Core;

namespace z3l.FastHtmlParser;

/// <summary>
/// What a tag must look like, for <c>Find</c>, <c>FindAll</c>, <c>Is</c> and the navigation methods.
/// Every criterion that is set (not null) must match; criteria that are null are not checked.
/// </summary>
/// <example>
/// <code>
/// doc.Find(new TagQuery(HtmlTag.Span) { Class = "price" });
/// doc.FindAll(new TagQuery(HtmlTag.A) { Attribute = HtmlAttribute.Rel, AttributeValue = "nofollow" });
/// doc.Find(new TagQuery { Id = "main" });   // any tag
/// </code>
/// </example>
/// <remarks>
/// A query is a small struct passed by reference: searching with it allocates nothing.
/// The name is checked first with one integer comparison; the other criteria (attributes first,
/// then text) are only looked at for tags with the right name.
/// </remarks>
public readonly struct TagQuery
{
    // Stored +1, so that 0 means "not set": default(TagQuery) is a valid "any tag" query.
    private readonly int _key;              // (int)HtmlTag of the name + 1, or 0
    private readonly short _attribute;      // (int)HtmlAttribute + 1, or 0
    private readonly bool _more;            // any criterion besides the name
    private readonly string? _name;         // the name as given, for string queries

    // The criteria used most in scraping are stored inline ...
    private readonly string? _class, _id, _text, _attributeName, _attributeValue;

    // ... the rarer ones in a separate object, created only when one of them is set:
    // the struct stays small (it is copied into every lazy FindAll iterator).
    private readonly Rare? _rare;

    private sealed record Rare
    {
        public string? IdStartsWith, TextStartsWith, Body, BodyStartsWith, Source;
    }

    /// <summary>Any tag (combine with the other criteria).</summary>
    public TagQuery()
    {
    }

    /// <summary>Tags with the given name.</summary>
    public TagQuery(HtmlTag tag)
    {
        _key = (int)tag + 1;
    }

    /// <summary>
    /// Tags with the given name (case-insensitive); <see langword="null"/>: any tag.
    /// Use this for names that are not in <see cref="HtmlTag"/> ("my-widget").
    /// </summary>
    public TagQuery(string? name)
    {
        _name = name;
        _key = !string.IsNullOrEmpty(name) && ParsingCore.TryGetTag(name, out var tag) ? (int)tag + 1 : 0;
    }

    /// <summary>One of the tag's classes (space-separated, case-insensitive).</summary>
    public string? Class { get => _class; init { _class = value; _more |= value != null; } }

    /// <summary>The id attribute.</summary>
    public string? Id { get => _id; init { _id = value; _more |= value != null; } }

    /// <summary>The visible text, as returned by <c>Text()</c> for a tag with child tags.</summary>
    public string? Text { get => _text; init { _text = value; _more |= value != null; } }

    /// <summary>The attribute to compare with <see cref="AttributeValue"/>.</summary>
    public HtmlAttribute? Attribute
    {
        get => _attribute > 0 ? (HtmlAttribute)(_attribute - 1) : null;
        init => _attribute = value is { } attribute ? (short)((int)attribute + 1) : (short)0;
    }

    /// <summary>The attribute to compare with <see cref="AttributeValue"/>, by name ("data-product-id"); used when <see cref="Attribute"/> is not set.</summary>
    public string? AttributeName { get => _attributeName; init => _attributeName = value; }

    /// <summary>The decoded value of <see cref="Attribute"/> / <see cref="AttributeName"/>.</summary>
    public string? AttributeValue { get => _attributeValue; init { _attributeValue = value; _more |= value != null; } }

    /// <summary>The beginning of the id attribute (ordinal).</summary>
    public string? IdStartsWith { get => _rare?.IdStartsWith; init { _rare = _rare is { } r ? r with { IdStartsWith = value } : new() { IdStartsWith = value }; _more |= value != null; } }

    /// <summary>The beginning of <c>Text()</c> (ordinal).</summary>
    public string? TextStartsWith { get => _rare?.TextStartsWith; init { _rare = _rare is { } r ? r with { TextStartsWith = value } : new() { TextStartsWith = value }; _more |= value != null; } }

    /// <summary>The inner HTML, exactly.</summary>
    public string? Body { get => _rare?.Body; init { _rare = _rare is { } r ? r with { Body = value } : new() { Body = value }; _more |= value != null; } }

    /// <summary>The beginning of the inner HTML (ordinal).</summary>
    public string? BodyStartsWith { get => _rare?.BodyStartsWith; init { _rare = _rare is { } r ? r with { BodyStartsWith = value } : new() { BodyStartsWith = value }; _more |= value != null; } }

    /// <summary>The source of the opening tag without '&lt;' and '&gt;' (<c>a href="/"</c>), exactly.</summary>
    public string? Source { get => _rare?.Source; init { _rare = _rare is { } r ? r with { Source = value } : new() { Source = value }; _more |= value != null; } }

    /// <summary>Small enough to be inlined into the search loops: one integer comparison for most tags.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Matches(ParserTag tag)
    {
        if (_key != 0)
        {
            // a custom tag can still match a string name (OrdinalIgnoreCase), as IsName(string) does
            if (tag.Key != _key - 1 && (tag.Key >= 0 || _name == null || !tag.IsName(_name))) return false;
        }
        else if (_name != null && !tag.IsName(_name))
        {
            return false;
        }

        return !_more || MatchesCriteria(tag);
    }

    private bool MatchesCriteria(ParserTag tag)
    {
        // attributes
        if (_id != null && !tag.IsId(_id)) return false;
        if (_attributeValue != null)
        {
            var name = _attribute > 0 ? ParsingCore.AttributeNames[_attribute - 1] : _attributeName;
            if (name == null || !tag.IsAttribute(name, _attributeValue)) return false;
        }
        if (_class != null && !tag.IsClass(_class)) return false;

        var rare = _rare;
        if (rare != null)
        {
            if (rare.IdStartsWith != null && !SpanHandlingCore.AttributeStartsWith(SpanHandlingCore.SpanAttributeId(tag), rare.IdStartsWith)) return false;
            if (rare.Source != null && !tag.IsSource(rare.Source)) return false;
            if (rare.Body != null && !tag.IsBody(rare.Body)) return false;
            if (rare.BodyStartsWith != null && !SpanHandlingCore.SpanBody(tag).StartsWith(rare.BodyStartsWith, StringComparison.Ordinal)) return false;
        }

        // the text last: it is the most expensive check
        if (_text != null && !tag.IsText(_text)) return false;
        if (rare?.TextStartsWith != null && !(tag.Text()?.StartsWith(rare.TextStartsWith, StringComparison.Ordinal) ?? false)) return false;

        return true;
    }
}
