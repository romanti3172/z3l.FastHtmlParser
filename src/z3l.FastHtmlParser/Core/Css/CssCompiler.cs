using System.Globalization;
using System.Text;

namespace z3l.FastHtmlParser.Core.Css;

/// <summary>
/// Parses a CSS selector list (Selectors Level 3 and the common Level 4 pseudo-classes) into <see cref="CssSelectorList"/>.
/// Identifiers and strings follow the CSS syntax, escapes included (<c>#a\:b</c>, <c>.\31 0</c>, <c>[title="a\"b"]</c>).
/// </summary>
internal sealed class CssCompiler
{
    private readonly string _s;
    private int _p;

    private CssCompiler(string selector) => _s = selector;

    public static CssSelectorList Compile(string selector)
    {
        var compiler = new CssCompiler(selector);
        var list = compiler.ParseList(relative: false, topLevel: true);
        if (compiler._p < selector.Length) throw compiler.Error($"unexpected '{selector[compiler._p]}'");
        return list;
    }

    // selector-list: complex (',' complex)*
    private CssSelectorList ParseList(bool relative, bool topLevel = false)
    {
        var items = new List<CssComplex>();
        while (true)
        {
            items.Add(ParseComplex(relative, topLevel));
            SkipWhitespace();
            if (Peek() != ',') break;
            _p++;
        }
        return new CssSelectorList([.. items]);
    }

    // complex: [combinator] compound (combinator compound)*
    private CssComplex ParseComplex(bool relative, bool topLevel)
    {
        SkipWhitespace();

        var leading = CssCombinator.None;
        if (relative || topLevel)
        {
            var combinator = ReadCombinatorChar();
            if (combinator != CssCombinator.None)
            {
                if (topLevel && combinator != CssCombinator.Child)
                {
                    throw Error("a selector can start only with '>' (the children of the tag it is called on)");
                }
                leading = combinator;
            }
            else if (relative)
            {
                leading = CssCombinator.Descendant;
            }
        }

        var compounds = new List<CssCompound> { ParseCompound() };
        var combinators = new List<CssCombinator>();

        while (true)
        {
            bool whitespace = SkipWhitespace();
            char c = Peek();
            if (c is '\0' or ',' or ')') break;

            var combinator = ReadCombinatorChar();
            if (combinator == CssCombinator.None)
            {
                if (!whitespace) throw Error($"unexpected '{c}'");
                combinator = CssCombinator.Descendant;
            }

            combinators.Add(combinator);
            compounds.Add(ParseCompound());
        }

        return new CssComplex(leading, [.. compounds], [.. combinators]);
    }

    private CssCombinator ReadCombinatorChar()
    {
        var combinator = Peek() switch
        {
            '>' => CssCombinator.Child,
            '+' => CssCombinator.Adjacent,
            '~' => CssCombinator.Sibling,
            _ => CssCombinator.None,
        };
        if (combinator != CssCombinator.None)
        {
            _p++;
            SkipWhitespace();
        }
        return combinator;
    }

    // compound: [type | '*'] (#id | .class | [attribute] | :pseudo)*
    private CssCompound ParseCompound()
    {
        int key = -1;
        string? name = null;
        bool hasType = false;

        if (Peek() == '*')
        {
            _p++;
            hasType = true;
        }
        else if (IsIdentifierStart(_p))
        {
            name = ReadIdentifier().ToLowerInvariant();
            hasType = true;
            if (ParsingCore.TryGetTag(name, out var tag))
            {
                key = (int)tag;
                name = null;
            }
        }

        if (Peek() == '|') throw Error("namespaces are not supported; escape the colon of a prefixed name: dc\\:creator");

        var simples = new List<CssSimple>();
        while (true)
        {
            switch (Peek())
            {
                case '#':
                    _p++;
                    if (!IsNameChar(_p)) throw Error("an id expected after '#'");
                    simples.Add(new CssId(ReadName()));
                    continue;
                case '.':
                    _p++;
                    if (!IsIdentifierStart(_p)) throw Error("a class name expected after '.'");
                    simples.Add(new CssClass(ReadIdentifier()));
                    continue;
                case '[':
                    simples.Add(ParseAttribute());
                    continue;
                case ':':
                    ParsePseudo(simples);
                    continue;
            }
            break;
        }

        if (!hasType && simples.Count == 0)
        {
            throw Error(_p >= _s.Length ? "a selector expected" : $"unexpected '{_s[_p]}'");
        }

        return new CssCompound(key, name, [.. simples]);
    }

    // '[' name [op value [i|s]] ']'
    private CssAttribute ParseAttribute()
    {
        _p++; // '['
        SkipWhitespace();
        if (!IsIdentifierStart(_p)) throw Error("an attribute name expected");
        var name = ReadIdentifier();
        SkipWhitespace();

        if (Peek() == ']')
        {
            _p++;
            return new CssAttribute(name, CssAttributeOperator.Exists, "", '\0');
        }

        CssAttributeOperator op;
        char c = Peek();
        if (c == '=')
        {
            op = CssAttributeOperator.Equals;
            _p++;
        }
        else
        {
            op = c switch
            {
                '~' => CssAttributeOperator.Includes,
                '|' => CssAttributeOperator.Dash,
                '^' => CssAttributeOperator.Prefix,
                '$' => CssAttributeOperator.Suffix,
                '*' => CssAttributeOperator.Substring,
                _ => throw Error("'=', '~=', '|=', '^=', '$=', '*=' or ']' expected"),
            };
            if (Peek(1) != '=') throw Error($"'=' expected after '{c}'");
            _p += 2;
        }

        SkipWhitespace();
        string value;
        if (Peek() is '"' or '\'') value = ReadString();
        else if (IsIdentifierStart(_p)) value = ReadIdentifier();
        else throw Error("an attribute value expected (quote it if it is not a plain word)");

        SkipWhitespace();
        char caseFlag = '\0';
        if (IsIdentifierStart(_p))
        {
            var flag = ReadIdentifier();
            if (flag.Equals("i", StringComparison.OrdinalIgnoreCase)) caseFlag = 'i';
            else if (flag.Equals("s", StringComparison.OrdinalIgnoreCase)) caseFlag = 's';
            else throw Error($"unknown attribute flag '{flag}'");
            SkipWhitespace();
        }

        Expect(']');
        return new CssAttribute(name, op, value, caseFlag);
    }

    private void ParsePseudo(List<CssSimple> simples)
    {
        _p++; // ':'
        if (Peek() == ':') throw Error("pseudo-elements (::before, ::text) select no tags");
        if (!IsIdentifierStart(_p)) throw Error("a pseudo-class name expected after ':'");

        int start = _p;
        var name = ReadIdentifier().ToLowerInvariant();

        if (Peek() == '(')
        {
            _p++;
            SkipWhitespace();
            switch (name)
            {
                case "not":
                    simples.Add(new CssNot(ParseList(relative: false)));
                    break;
                case "is" or "where" or "matches" or "any":
                    simples.Add(new CssIs(ParseList(relative: false)));
                    break;
                case "has":
                    simples.Add(new CssHas(ParseList(relative: true)));
                    break;
                case "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type":
                {
                    var (a, b) = ParseNth();
                    bool ofType = name.EndsWith("of-type", StringComparison.Ordinal);
                    CssSelectorList? of = null;
                    SkipWhitespace();
                    if (!ofType && IsWord("of"))
                    {
                        _p += 2;
                        of = ParseList(relative: false);
                    }
                    simples.Add(new CssNth(a, b, name.Contains("last", StringComparison.Ordinal), ofType, of));
                    break;
                }
                case "contains":
                {
                    string text;
                    if (Peek() is '"' or '\'') text = ReadString();
                    else if (IsIdentifierStart(_p)) text = ReadIdentifier();
                    else throw Error("a string expected in :contains()");
                    simples.Add(new CssContains(text));
                    break;
                }
                default:
                    _p = start;
                    throw Error($"unknown pseudo-class ':{name}()'");
            }

            SkipWhitespace();
            Expect(')');
            return;
        }

        switch (name)
        {
            case "first-child": simples.Add(new CssNth(0, 1, false, false, null)); break;
            case "last-child": simples.Add(new CssNth(0, 1, true, false, null)); break;
            case "only-child":
                simples.Add(new CssNth(0, 1, false, false, null));
                simples.Add(new CssNth(0, 1, true, false, null));
                break;
            case "first-of-type": simples.Add(new CssNth(0, 1, false, true, null)); break;
            case "last-of-type": simples.Add(new CssNth(0, 1, true, true, null)); break;
            case "only-of-type":
                simples.Add(new CssNth(0, 1, false, true, null));
                simples.Add(new CssNth(0, 1, true, true, null));
                break;
            case "root": simples.Add(new CssPseudo(CssPseudoKind.Root)); break;
            case "scope": simples.Add(new CssPseudo(CssPseudoKind.Scope)); break;
            case "empty": simples.Add(new CssPseudo(CssPseudoKind.Empty)); break;
            case "link" or "any-link": simples.Add(new CssPseudo(CssPseudoKind.Link)); break;
            case "checked": simples.Add(new CssPseudo(CssPseudoKind.Checked)); break;
            case "disabled": simples.Add(new CssPseudo(CssPseudoKind.Disabled)); break;
            case "enabled": simples.Add(new CssPseudo(CssPseudoKind.Enabled)); break;
            case "required": simples.Add(new CssPseudo(CssPseudoKind.Required)); break;
            case "optional": simples.Add(new CssPseudo(CssPseudoKind.Optional)); break;
            default:
                _p = start;
                throw Error($"unknown pseudo-class ':{name}'");
        }
    }

    // An+B: odd, even, 3, n, -n+3, 2n+1, +2n - 1
    private (int A, int B) ParseNth()
    {
        if (IsWord("odd"))
        {
            _p += 3;
            return (2, 1);
        }
        if (IsWord("even"))
        {
            _p += 4;
            return (2, 0);
        }

        int start = _p;
        var text = new StringBuilder();
        while (_p < _s.Length && (_s[_p] is '+' or '-' or 'n' or 'N' || char.IsAsciiDigit(_s[_p]) || IsWhitespace(_s[_p])))
        {
            // "of" follows the expression after whitespace: stop before it
            if (!IsWhitespace(_s[_p])) text.Append(char.ToLowerInvariant(_s[_p]));
            _p++;
        }

        var t = text.ToString();
        int n = t.IndexOf('n');
        if (n < 0)
        {
            if (TryInt(t, out int only)) return (0, only);
        }
        else if (t.IndexOf('n', n + 1) < 0)
        {
            var aText = t[..n];
            var bText = t[(n + 1)..];
            int a = aText switch { "" or "+" => 1, "-" => -1, _ => TryInt(aText, out int value) ? value : int.MinValue };
            int b = 0;
            bool bOk = bText.Length == 0 || (bText[0] is '+' or '-' && bText.Length > 1 && char.IsAsciiDigit(bText[1]) && TryInt(bText, out b));
            if (a != int.MinValue && bOk) return (a, b);
        }

        _p = start;
        throw Error("An+B expected (odd, even, 3, 2n+1, -n+3)");
    }

    private static bool TryInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) && text.Length > 0;

    // Lexing

    private char Peek(int offset = 0) => _p + offset < _s.Length ? _s[_p + offset] : '\0';

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private bool SkipWhitespace()
    {
        int start = _p;
        while (_p < _s.Length && IsWhitespace(_s[_p])) _p++;
        return _p > start;
    }

    private void Expect(char c)
    {
        if (Peek() != c) throw Error(_p >= _s.Length ? $"'{c}' expected" : $"'{c}' expected instead of '{_s[_p]}'");
        _p++;
    }

    /// <summary>Whether the identifier at the position is <paramref name="word"/> (case-insensitive) and ends there.</summary>
    private bool IsWord(string word) =>
        string.Compare(_s, _p, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
        && _p + word.Length <= _s.Length && !IsNameChar(_p + word.Length);

    private static bool IsNameStartChar(char c) => char.IsAsciiLetter(c) || c == '_' || c >= 0x80;

    private bool IsEscape(int p) => p < _s.Length && _s[p] == '\\' && (p + 1 >= _s.Length || _s[p + 1] is not ('\n' or '\r' or '\f'));

    private bool IsNameChar(int p) =>
        p < _s.Length && (IsNameStartChar(_s[p]) || char.IsAsciiDigit(_s[p]) || _s[p] == '-' || IsEscape(p));

    private bool IsIdentifierStart(int p)
    {
        if (p >= _s.Length) return false;

        char c = _s[p];
        if (c == '-')
        {
            p++;
            return p < _s.Length && (IsNameStartChar(_s[p]) || _s[p] == '-' || IsEscape(p));
        }
        return IsNameStartChar(c) || IsEscape(p);
    }

    private string ReadIdentifier() => ReadName();

    private string ReadName()
    {
        var result = new StringBuilder();
        while (IsNameChar(_p))
        {
            if (_s[_p] == '\\') ReadEscape(result);
            else result.Append(_s[_p++]);
        }
        return result.ToString();
    }

    private string ReadString()
    {
        char quote = _s[_p++];
        int start = _p - 1;
        var result = new StringBuilder();
        while (true)
        {
            if (_p >= _s.Length)
            {
                _p = start;
                throw Error("unterminated string");
            }

            char c = _s[_p];
            if (c == quote)
            {
                _p++;
                return result.ToString();
            }

            if (c == '\\')
            {
                if (_p + 1 < _s.Length && _s[_p + 1] is '\n' or '\f')
                {
                    _p += 2; // an escaped line break continues the string
                }
                else if (_p + 2 < _s.Length && _s[_p + 1] == '\r')
                {
                    _p += _s[_p + 2] == '\n' ? 3 : 2;
                }
                else
                {
                    ReadEscape(result);
                }
                continue;
            }

            result.Append(c);
            _p++;
        }
    }

    // '\' then up to 6 hex digits and one optional whitespace, or any other char as itself
    private void ReadEscape(StringBuilder result)
    {
        _p++; // '\'
        if (_p >= _s.Length)
        {
            result.Append('�');
            return;
        }

        if (!char.IsAsciiHexDigit(_s[_p]))
        {
            result.Append(_s[_p++]);
            return;
        }

        int code = 0, digits = 0;
        while (digits < 6 && _p < _s.Length && char.IsAsciiHexDigit(_s[_p]))
        {
            code = code * 16 + Convert.ToInt32(_s[_p].ToString(), 16);
            _p++;
            digits++;
        }

        if (_p < _s.Length && IsWhitespace(_s[_p]))
        {
            _p += _s[_p] == '\r' && Peek(1) == '\n' ? 2 : 1;
        }

        if (code == 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) result.Append('�');
        else result.Append(char.ConvertFromUtf32(code));
    }

    private ArgumentException Error(string message) =>
        new($"Invalid CSS selector \"{_s}\" at position {_p}: {message}.", "selector");
}
