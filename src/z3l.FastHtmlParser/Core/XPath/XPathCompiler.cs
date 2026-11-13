using System.Globalization;

namespace z3l.FastHtmlParser.Core.XPath;

/// <summary>Tokenizer and recursive-descent parser for XPath 1.0 expressions.</summary>
internal sealed class XPathCompiler
{
    private enum T : byte
    {
        End, Slash, DoubleSlash, LParen, RParen, LBracket, RBracket, Dot, DotDot, At, Comma, ColonColon,
        Pipe, Plus, Minus, Eq, Neq, Lt, Le, Gt, Ge,
        Star,       // name test *
        Multiply,   // operator *
        And, Or, Div, Mod,
        Name, Literal, Number, Dollar,
    }

    private readonly record struct Token(T Kind, string? Text, double Number, int Position);

    private readonly string _source;
    private readonly List<Token> _tokens;
    private int _index;

    private XPathCompiler(string source)
    {
        _source = source;
        _tokens = Tokenize(source);
    }

    public static Expr Compile(string xpath)
    {
        var compiler = new XPathCompiler(xpath);
        var expr = compiler.ParseExpr();
        if (compiler.Peek().Kind != T.End) throw compiler.Error("unexpected " + compiler.Describe(compiler.Peek()));
        return expr;
    }

    // Tokenizer

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';

    private List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (true)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length)
            {
                tokens.Add(new Token(T.End, null, 0, i));
                return tokens;
            }

            var start = i;
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';

            // XPath 1.0 lexical rule: after a token that can end an operand, '*' and and/or/div/mod are operators
            var operatorExpected = tokens.Count > 0 && tokens[^1].Kind is
                T.RParen or T.RBracket or T.Dot or T.DotDot or T.Star or T.Name or T.Literal or T.Number;

            T kind;
            string? text = null;
            double number = 0;

            switch (c)
            {
                case '/':
                    if (next == '/') { kind = T.DoubleSlash; i += 2; }
                    else { kind = T.Slash; i++; }
                    break;
                case '(': kind = T.LParen; i++; break;
                case ')': kind = T.RParen; i++; break;
                case '[': kind = T.LBracket; i++; break;
                case ']': kind = T.RBracket; i++; break;
                case '@': kind = T.At; i++; break;
                case ',': kind = T.Comma; i++; break;
                case '|': kind = T.Pipe; i++; break;
                case '+': kind = T.Plus; i++; break;
                case '-': kind = T.Minus; i++; break;
                case '=': kind = T.Eq; i++; break;
                case '$': kind = T.Dollar; i++; break;
                case '*': kind = operatorExpected ? T.Multiply : T.Star; i++; break;
                case '!':
                    if (next != '=') throw Error("'!' must be followed by '='", i);
                    kind = T.Neq; i += 2;
                    break;
                case '<':
                    if (next == '=') { kind = T.Le; i += 2; }
                    else { kind = T.Lt; i++; }
                    break;
                case '>':
                    if (next == '=') { kind = T.Ge; i += 2; }
                    else { kind = T.Gt; i++; }
                    break;
                case ':':
                    if (next != ':') throw Error("unexpected ':'", i);
                    kind = T.ColonColon; i += 2;
                    break;
                case '"':
                case '\'':
                {
                    var close = s.IndexOf(c, i + 1);
                    if (close < 0) throw Error("unterminated string literal", i);
                    kind = T.Literal;
                    text = s[(i + 1)..close];
                    i = close + 1;
                    break;
                }
                case '.' when next == '.':
                    kind = T.DotDot; i += 2;
                    break;
                case '.' when !char.IsAsciiDigit(next):
                    kind = T.Dot; i++;
                    break;
                default:
                    if (char.IsAsciiDigit(c) || c == '.')
                    {
                        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                        if (i < s.Length && s[i] == '.')
                        {
                            i++;
                            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                        }
                        kind = T.Number;
                        number = double.Parse(s.AsSpan(start, i - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                    }
                    else if (IsNameStart(c))
                    {
                        i++;
                        while (i < s.Length)
                        {
                            if (IsNameChar(s[i]))
                            {
                                i++;
                            }
                            else if (s[i] == ':' && i + 1 < s.Length && s[i + 1] != ':' && (IsNameStart(s[i + 1]) || s[i + 1] == '*'))
                            {
                                // a prefixed name (dc:creator) or prefix:*
                                i += 2;
                                if (s[i - 1] == '*') break;
                            }
                            else
                            {
                                break;
                            }
                        }

                        text = s[start..i];
                        kind = T.Name;
                        if (operatorExpected)
                        {
                            kind = text switch
                            {
                                "and" => T.And,
                                "or" => T.Or,
                                "div" => T.Div,
                                "mod" => T.Mod,
                                _ => throw Error($"expected an operator, found '{text}'", start),
                            };
                        }
                    }
                    else
                    {
                        throw Error($"unexpected character '{c}'", i);
                    }
                    break;
            }

            tokens.Add(new Token(kind, text, number, start));
        }
    }

    // Parser helpers

    private Token Peek(int offset = 0) => _tokens[Math.Min(_index + offset, _tokens.Count - 1)];

    private Token Advance() => _tokens[_index++];

    private bool Accept(T kind)
    {
        if (Peek().Kind != kind) return false;
        _index++;
        return true;
    }

    private void Expect(T kind, string what)
    {
        if (!Accept(kind)) throw Error($"expected {what}, found {Describe(Peek())}");
    }

    private string Describe(Token token) => token.Kind switch
    {
        T.End => "the end of the expression",
        T.Name or T.Literal => $"'{token.Text}'",
        T.Number => token.Number.ToString(CultureInfo.InvariantCulture),
        _ => $"'{_source[token.Position..Math.Min(token.Position + 2, _source.Length)].TrimEnd()}'",
    };

    private ArgumentException Error(string message) => Error(message, Peek().Position);

    private ArgumentException Error(string message, int position) =>
        new($"Invalid XPath \"{_source}\" at position {position}: {message}.", "xpath");

    // Grammar (XPath 1.0, section 3)

    private Expr ParseExpr() => ParseOr();

    private Expr ParseOr()
    {
        var left = ParseAnd();
        while (Accept(T.Or)) left = new OrExpr(left, ParseAnd());
        return left;
    }

    private Expr ParseAnd()
    {
        var left = ParseEquality();
        while (Accept(T.And)) left = new AndExpr(left, ParseEquality());
        return left;
    }

    private Expr ParseEquality()
    {
        var left = ParseRelational();
        while (true)
        {
            CompareOp op;
            if (Accept(T.Eq)) op = CompareOp.Eq;
            else if (Accept(T.Neq)) op = CompareOp.Neq;
            else return left;

            left = MakeEquality(op, left, ParseRelational());
        }
    }

    /// <summary><c>@name = 'literal'</c> is compared in place; everything else by the general rules.</summary>
    private static Expr MakeEquality(CompareOp op, Expr left, Expr right)
    {
        if (TryGetAttributeName(left, out var name) && right is LiteralExpr literal)
        {
            return new AttributeEqualsExpr(name, literal.Value, negate: op == CompareOp.Neq);
        }
        if (TryGetAttributeName(right, out name) && left is LiteralExpr literalLeft)
        {
            return new AttributeEqualsExpr(name, literalLeft.Value, negate: op == CompareOp.Neq);
        }

        return new CompareExpr(op, left, right);
    }

    /// <summary>Whether the expression is a bare <c>@name</c> (one attribute step, no predicates).</summary>
    private static bool TryGetAttributeName(Expr expr, out string name)
    {
        if (expr is PathExpr { Start: null, Absolute: false, Steps: [{ Axis: Axis.Attribute, Predicates.Length: 0 } step] }
            && step.Test.Kind == NodeTest.TestKind.Name)
        {
            name = step.Test.Name!;
            return true;
        }

        name = "";
        return false;
    }

    private Expr ParseRelational()
    {
        var left = ParseAdditive();
        while (true)
        {
            CompareOp op;
            if (Accept(T.Lt)) op = CompareOp.Lt;
            else if (Accept(T.Le)) op = CompareOp.Le;
            else if (Accept(T.Gt)) op = CompareOp.Gt;
            else if (Accept(T.Ge)) op = CompareOp.Ge;
            else return left;

            left = new CompareExpr(op, left, ParseAdditive());
        }
    }

    private Expr ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (true)
        {
            if (Accept(T.Plus)) left = new ArithExpr(ArithOp.Add, left, ParseMultiplicative());
            else if (Accept(T.Minus)) left = new ArithExpr(ArithOp.Sub, left, ParseMultiplicative());
            else return left;
        }
    }

    private Expr ParseMultiplicative()
    {
        var left = ParseUnary();
        while (true)
        {
            if (Accept(T.Multiply)) left = new ArithExpr(ArithOp.Mul, left, ParseUnary());
            else if (Accept(T.Div)) left = new ArithExpr(ArithOp.Div, left, ParseUnary());
            else if (Accept(T.Mod)) left = new ArithExpr(ArithOp.Mod, left, ParseUnary());
            else return left;
        }
    }

    private Expr ParseUnary()
    {
        if (Accept(T.Minus)) return new NegateExpr(ParseUnary());
        return ParseUnion();
    }

    private Expr ParseUnion()
    {
        var left = ParsePath();
        while (Peek().Kind == T.Pipe)
        {
            var position = Advance().Position;
            var right = ParsePath();
            if (left.Type != XType.NodeSet || right.Type != XType.NodeSet) throw Error("'|' needs node-sets on both sides", position);
            left = new UnionExpr(left, right);
        }
        return left;
    }

    private static readonly HashSet<string> NodeTypes = ["node", "text", "comment", "processing-instruction"];

    private Expr ParsePath()
    {
        var token = Peek();
        var isFilter = token.Kind is T.Literal or T.Number or T.LParen or T.Dollar
            || token.Kind == T.Name && Peek(1).Kind == T.LParen && !NodeTypes.Contains(token.Text!);

        if (!isFilter) return ParseLocationPath();

        var position = token.Position;
        var primary = ParsePrimary();
        var predicates = ParsePredicates();
        if (predicates.Length > 0)
        {
            if (primary.Type != XType.NodeSet) throw Error("a predicate needs a node-set", position);
            primary = new FilterExpr(primary, predicates);
        }

        if (Peek().Kind is not (T.Slash or T.DoubleSlash)) return primary;

        if (primary.Type != XType.NodeSet) throw Error("'/' needs a node-set on the left", position);

        var steps = new List<Step>();
        ParseRelativeSteps(steps, afterSlash: true);
        return new PathExpr(primary, absolute: false, Optimize(steps));
    }

    private Expr ParseLocationPath()
    {
        var steps = new List<Step>();
        var absolute = false;

        if (Accept(T.Slash))
        {
            absolute = true;
            if (CanStartStep(Peek())) ParseRelativeSteps(steps, afterSlash: false);
        }
        else if (Peek().Kind == T.DoubleSlash)
        {
            absolute = true;
            ParseRelativeSteps(steps, afterSlash: true);
        }
        else
        {
            if (!CanStartStep(Peek())) throw Error($"unexpected {Describe(Peek())}");
            ParseRelativeSteps(steps, afterSlash: false);
        }

        return new PathExpr(null, absolute, Optimize(steps));
    }

    private static bool CanStartStep(Token token) => token.Kind is T.Dot or T.DotDot or T.At or T.Star or T.Name;

    /// <summary>Steps separated by '/' or '//'; <paramref name="afterSlash"/>: the next token is a '/' or '//' to read first.</summary>
    private void ParseRelativeSteps(List<Step> steps, bool afterSlash)
    {
        if (!afterSlash) steps.Add(ParseStep());

        while (true)
        {
            if (Accept(T.Slash))
            {
                steps.Add(ParseStep());
            }
            else if (Accept(T.DoubleSlash))
            {
                steps.Add(new Step(Axis.DescendantOrSelf, NodeTest.AnyNode, []));
                steps.Add(ParseStep());
            }
            else
            {
                return;
            }
        }
    }

    private static readonly Dictionary<string, Axis> Axes = new()
    {
        ["child"] = Axis.Child,
        ["descendant"] = Axis.Descendant,
        ["descendant-or-self"] = Axis.DescendantOrSelf,
        ["self"] = Axis.Self,
        ["parent"] = Axis.Parent,
        ["ancestor"] = Axis.Ancestor,
        ["ancestor-or-self"] = Axis.AncestorOrSelf,
        ["following-sibling"] = Axis.FollowingSibling,
        ["preceding-sibling"] = Axis.PrecedingSibling,
        ["following"] = Axis.Following,
        ["preceding"] = Axis.Preceding,
        ["attribute"] = Axis.Attribute,
    };

    private Step ParseStep()
    {
        if (Accept(T.Dot)) return new Step(Axis.Self, NodeTest.AnyNode, []);
        if (Accept(T.DotDot)) return new Step(Axis.Parent, NodeTest.AnyNode, []);

        var axis = Axis.Child;
        if (Accept(T.At))
        {
            axis = Axis.Attribute;
        }
        else if (Peek().Kind == T.Name && Peek(1).Kind == T.ColonColon)
        {
            var name = Advance();
            if (!Axes.TryGetValue(name.Text!, out axis))
            {
                throw Error(name.Text == "namespace" ? "the namespace axis is not supported" : $"unknown axis '{name.Text}'", name.Position);
            }
            Advance();
        }

        var test = ParseNodeTest();
        return new Step(axis, test, ParsePredicates());
    }

    private NodeTest ParseNodeTest()
    {
        if (Accept(T.Star)) return new NodeTest(NodeTest.TestKind.Wildcard, null);

        var token = Peek();
        if (token.Kind != T.Name) throw Error($"expected a node test, found {Describe(token)}");
        Advance();

        var name = token.Text!;
        if (Peek().Kind == T.LParen && NodeTypes.Contains(name))
        {
            Advance();
            if (name == "processing-instruction") Accept(T.Literal);
            Expect(T.RParen, "')'");

            return name switch
            {
                "node" => NodeTest.AnyNode,
                "text" => new NodeTest(NodeTest.TestKind.Text, null),
                "comment" => new NodeTest(NodeTest.TestKind.Comment, null),
                _ => new NodeTest(NodeTest.TestKind.Pi, null),
            };
        }

        if (name.EndsWith(":*", StringComparison.Ordinal)) return new NodeTest(NodeTest.TestKind.Prefix, name[..^1]);

        return new NodeTest(NodeTest.TestKind.Name, name);
    }

    private Expr[] ParsePredicates()
    {
        if (Peek().Kind != T.LBracket) return [];

        var predicates = new List<Expr>();
        while (Accept(T.LBracket))
        {
            var predicate = ParseExpr();
            Expect(T.RBracket, "']'");

            // [@name]: an existence check instead of a node-set
            if (TryGetAttributeName(predicate, out var name)) predicate = new AttributeExistsExpr(name);

            predicates.Add(predicate);
        }
        return [.. predicates];
    }

    private Expr ParsePrimary()
    {
        var token = Advance();
        switch (token.Kind)
        {
            case T.Literal:
                return new LiteralExpr(token.Text!);

            case T.Number:
                return new NumberExpr(token.Number);

            case T.LParen:
            {
                var expr = ParseExpr();
                Expect(T.RParen, "')'");
                return expr;
            }

            case T.Dollar:
                throw Error("variables are not supported", token.Position);

            default: // function call
            {
                var name = token.Text!;
                if (!FunctionExpr.Functions.TryGetValue(name, out var function)) throw Error($"unknown function '{name}()'", token.Position);

                Expect(T.LParen, "'('");
                var args = new List<Expr>();
                if (!Accept(T.RParen))
                {
                    do args.Add(ParseExpr());
                    while (Accept(T.Comma));
                    Expect(T.RParen, "')'");
                }

                if (args.Count < function.Min || args.Count > function.Max)
                {
                    throw Error($"wrong number of arguments for {name}()", token.Position);
                }
                if (function.Fn is Fn.Count or Fn.Sum or Fn.Name or Fn.LocalName && args.Count > 0 && args[0].Type != XType.NodeSet)
                {
                    throw Error($"{name}() needs a node-set argument", token.Position);
                }

                return new FunctionExpr(function.Fn, [.. args], function.Type);
            }
        }
    }

    /// <summary>
    /// <c>//x</c> is <c>descendant-or-self::node()/child::x</c>. Without positional predicates on x it is the same
    /// as <c>descendant::x</c>: one linear scan of the tag list instead of the children of every node.
    /// </summary>
    private static Step[] Optimize(List<Step> steps)
    {
        for (int i = 0; i < steps.Count - 1; i++)
        {
            var step = steps[i];
            if (step.Axis != Axis.DescendantOrSelf || step.Test != NodeTest.AnyNode || step.Predicates.Length > 0) continue;

            var next = steps[i + 1];
            if (next.Axis == Axis.Child && !next.HasPositionalPredicates)
            {
                steps[i] = new Step(Axis.Descendant, next.Test, next.Predicates);
                steps.RemoveAt(i + 1);
            }
            else if (next.Axis is Axis.Child or Axis.Attribute)
            {
                // only the root and elements have children or attributes: skip the text nodes
                steps[i] = new Step(Axis.DescendantOrSelf, NodeTest.Containers, []);
            }
        }

        return [.. steps];
    }
}
