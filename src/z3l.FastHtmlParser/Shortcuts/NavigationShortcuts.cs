namespace z3l.FastHtmlParser.Shortcuts;

/// <summary>One-call navigation by name and class: shortcuts for the navigation methods with a <see cref="TagQuery"/>.</summary>
public static class NavigationShortcuts
{
    /// <summary>The nearest enclosing tag with the given name.</summary>
    public static ParserTag? ParentByName(this ParserTag? tag, HtmlTag name)
        => tag.Parent(new TagQuery(name));

    /// <inheritdoc cref="ParentByName(ParserTag?, HtmlTag)"/>
    public static ParserTag? ParentByName(this ParserTag? tag, string name)
        => tag.Parent(new TagQuery(name));

    /// <summary>The nearest enclosing tag with the given name and class.</summary>
    public static ParserTag? ParentByNameClass(this ParserTag? tag, HtmlTag name, string classValue)
        => tag.Parent(new TagQuery(name) { Class = classValue });

    /// <inheritdoc cref="ParentByNameClass(ParserTag?, HtmlTag, string)"/>
    public static ParserTag? ParentByNameClass(this ParserTag? tag, string name, string classValue)
        => tag.Parent(new TagQuery(name) { Class = classValue });

    /// <summary>The first tag after this one (after its closing tag) with the given name.</summary>
    public static ParserTag? NextByName(this ParserTag? tag, HtmlTag name)
        => tag.Next(new TagQuery(name));

    /// <inheritdoc cref="NextByName(ParserTag?, HtmlTag)"/>
    public static ParserTag? NextByName(this ParserTag? tag, string name)
        => tag.Next(new TagQuery(name));

    /// <summary>The first tag after this one (after its closing tag) with the given name and class.</summary>
    public static ParserTag? NextByNameClass(this ParserTag? tag, HtmlTag name, string classValue)
        => tag.Next(new TagQuery(name) { Class = classValue });

    /// <inheritdoc cref="NextByNameClass(ParserTag?, HtmlTag, string)"/>
    public static ParserTag? NextByNameClass(this ParserTag? tag, string name, string classValue)
        => tag.Next(new TagQuery(name) { Class = classValue });

    /// <summary>The next tag with the same parent and the given name.</summary>
    public static ParserTag? NextSiblingByName(this ParserTag? tag, HtmlTag name)
        => tag.NextSibling(new TagQuery(name));

    /// <inheritdoc cref="NextSiblingByName(ParserTag?, HtmlTag)"/>
    public static ParserTag? NextSiblingByName(this ParserTag? tag, string name)
        => tag.NextSibling(new TagQuery(name));

    /// <summary>The next tag with the same parent, name and class.</summary>
    public static ParserTag? NextSiblingByNameClass(this ParserTag? tag, HtmlTag name, string classValue)
        => tag.NextSibling(new TagQuery(name) { Class = classValue });

    /// <inheritdoc cref="NextSiblingByNameClass(ParserTag?, HtmlTag, string)"/>
    public static ParserTag? NextSiblingByNameClass(this ParserTag? tag, string name, string classValue)
        => tag.NextSibling(new TagQuery(name) { Class = classValue });

    /// <summary>The previous tag with the same parent and the given name.</summary>
    public static ParserTag? PreviousSiblingByName(this ParserTag? tag, HtmlTag name)
        => tag.PreviousSibling(new TagQuery(name));

    /// <inheritdoc cref="PreviousSiblingByName(ParserTag?, HtmlTag)"/>
    public static ParserTag? PreviousSiblingByName(this ParserTag? tag, string name)
        => tag.PreviousSibling(new TagQuery(name));

    /// <summary>Raw HTML between the end of this tag and the next tag with the given name; empty if there is none.</summary>
    public static string? TextAfterUntilTagByName(this ParserTag? tag, HtmlTag name)
        => tag.TextAfterUntil(new TagQuery(name));

    /// <inheritdoc cref="TextAfterUntilTagByName(ParserTag?, HtmlTag)"/>
    public static string? TextAfterUntilTagByName(this ParserTag? tag, string name)
        => tag.TextAfterUntil(new TagQuery(name));

    /// <summary>Raw HTML between the end of this tag and the next tag with the given name and class; empty if there is none.</summary>
    public static string? TextAfterUntilTagByNameClass(this ParserTag? tag, HtmlTag name, string classValue)
        => tag.TextAfterUntil(new TagQuery(name) { Class = classValue });

    /// <inheritdoc cref="TextAfterUntilTagByNameClass(ParserTag?, HtmlTag, string)"/>
    public static string? TextAfterUntilTagByNameClass(this ParserTag? tag, string name, string classValue)
        => tag.TextAfterUntil(new TagQuery(name) { Class = classValue });
}
