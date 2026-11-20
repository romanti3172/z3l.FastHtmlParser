namespace z3l.FastHtmlParser;

/// <summary>
/// An attribute of a tag, as <see cref="ParserTagAttributeExtensions.Attributes"/> returns it:
/// the name in lower case and the value decoded, as a browser reads them.
/// </summary>
public readonly struct TagAttribute
{
    private readonly HtmlParser? _document;
    private readonly int _valueStart;
    private readonly int _valueLength;

    internal TagAttribute(string name, string value, HtmlParser document, int valueStart, int valueLength)
    {
        Name = name;
        Value = value;
        _document = document;
        _valueStart = valueStart;
        _valueLength = valueLength;
    }

    /// <summary>The name in lower case (<c>DATA-ID</c> → <c>data-id</c>).</summary>
    public string Name { get; }

    /// <summary>
    /// The value with character references decoded (<c>title="It&amp;#39;s"</c> → <c>It's</c>), as
    /// <see cref="ParserTagAttributeExtensions.AttributeByName(ParserTag?, string)"/> returns it;
    /// an empty string for an attribute without a value (<c>&lt;input disabled&gt;</c>).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The value exactly as written in the source, as <see cref="ParserTagAttributeExtensions.AttributeBody(ParserTag?, string)"/>
    /// returns it. Made on each call.
    /// </summary>
    public string Body => _document == null || _valueStart < 0 ? "" : _document.Source.Slice(_valueStart, _valueLength).ToString();

    /// <summary>Whether the attribute has a value: false for <c>&lt;input disabled&gt;</c>, true for <c>disabled=""</c>.</summary>
    public bool HasValue => _document != null && _valueStart >= 0;

    /// <summary>For <c>foreach (var (name, value) in tag.Attributes())</c>.</summary>
    public void Deconstruct(out string name, out string value)
    {
        name = Name;
        value = Value;
    }

    /// <inheritdoc/>
    public override string ToString() => HasValue ? $"{Name}=\"{Value}\"" : Name ?? "";
}
