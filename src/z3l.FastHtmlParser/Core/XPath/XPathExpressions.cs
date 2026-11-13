using System.Globalization;
using System.Text;

namespace z3l.FastHtmlParser.Core.XPath;

internal enum XType : byte
{
    NodeSet,
    Number,
    String,
    Boolean,
}

/// <summary>Per-evaluation state.</summary>
internal sealed class EvalState
{
    private List<XNode>? _all;

    public EvalState(HtmlParser document)
    {
        Document = document;
    }

    public HtmlParser Document { get; }

    /// <summary>Every element, comment and text node in document order (for following / preceding with text()).</summary>
    public List<XNode> AllNodes()
    {
        if (_all == null)
        {
            _all = [];
            XNav.AddDescendants(XNode.Root(Document), NodeTest.AnyNode, _all);
        }
        return _all;
    }
}

internal readonly struct Ctx
{
    public readonly XNode Node;
    public readonly int Position;
    public readonly int Size;
    public readonly EvalState State;

    public Ctx(in XNode node, int position, int size, EvalState state)
    {
        Node = node;
        Position = position;
        Size = size;
        State = state;
    }
}

/// <summary>Conversions between the four XPath value types: double, string, bool, List&lt;XNode&gt; (document order).</summary>
internal static class XValue
{
    public static readonly object True = true;
    public static readonly object False = false;

    public static object Box(bool value) => value ? True : False;

    public static bool ToBoolean(object value) => value switch
    {
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        List<XNode> nodes => nodes.Count > 0,
        _ => false,
    };

    public static double ToNumber(object value) => value switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        string s => ToNumber(s),
        List<XNode> nodes => nodes.Count == 0 ? double.NaN : ToNumber(XNav.StringValue(nodes[0])),
        _ => double.NaN,
    };

    public static double ToNumber(string value)
    {
        var s = value.AsSpan().Trim();
        if (s.IsEmpty || s[0] == '+') return double.NaN;

        return double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
            ? d
            : double.NaN;
    }

    public static string ToString(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        double d => NumberToString(d),
        List<XNode> nodes => nodes.Count == 0 ? "" : XNav.StringValue(nodes[0]),
        _ => "",
    };

    public static string NumberToString(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == 0) return "0";

        // the shortest round-trip digits, written without an exponent (XPath has none)
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        var e = s.IndexOf('E');
        if (e < 0) return s;

        var exponent = int.Parse(s.AsSpan(e + 1), CultureInfo.InvariantCulture);
        var mantissa = s.AsSpan(0, e);
        var negative = mantissa[0] == '-';
        if (negative) mantissa = mantissa[1..];

        var point = mantissa.IndexOf('.');
        var digits = point < 0 ? mantissa.ToString() : string.Concat(mantissa[..point], mantissa[(point + 1)..]);
        var pointAt = (point < 0 ? mantissa.Length : point) + exponent; // digits before the decimal point

        var result = pointAt <= 0 ? "0." + new string('0', -pointAt) + digits
            : pointAt >= digits.Length ? digits + new string('0', pointAt - digits.Length)
            : digits[..pointAt] + "." + digits[pointAt..];

        return negative ? "-" + result : result;
    }

    /// <summary>Document order, no duplicates.</summary>
    public static void SortUnique(List<XNode> nodes)
    {
        if (nodes.Count < 2) return;

        var sorted = true;
        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i - 1].Key >= nodes[i].Key)
            {
                sorted = false;
                break;
            }
        }
        if (sorted) return;

        nodes.Sort(static (a, b) => a.Key.CompareTo(b.Key));

        var w = 1;
        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].Key != nodes[w - 1].Key) nodes[w++] = nodes[i];
        }
        nodes.RemoveRange(w, nodes.Count - w);
    }
}

internal abstract class Expr
{
    public abstract XType Type { get; }

    /// <summary>Whether position() or last() is used in the current context (not inside nested predicates).</summary>
    public virtual bool UsesPosition => false;

    public abstract object Eval(in Ctx ctx);

    public virtual bool EvalBool(in Ctx ctx) => XValue.ToBoolean(Eval(ctx));

    public double EvalNumber(in Ctx ctx) => XValue.ToNumber(Eval(ctx));

    public string EvalString(in Ctx ctx) => XValue.ToString(Eval(ctx));

    public List<XNode> EvalNodes(in Ctx ctx) =>
        Eval(ctx) as List<XNode> ?? throw new InvalidOperationException("The XPath expression does not give a node-set.");

    /// <summary>A predicate: a number compares with the position, anything else is converted to boolean.</summary>
    public bool EvalPredicate(in Ctx ctx)
    {
        if (Type == XType.Boolean) return EvalBool(ctx);

        var value = Eval(ctx);
        return value is double d ? d == ctx.Position : XValue.ToBoolean(value);
    }

    /// <summary>Whether the predicate depends on the position of the node (a number, position(), last()).</summary>
    public bool IsPositional => Type == XType.Number || UsesPosition;
}

internal sealed class LiteralExpr(string value) : Expr
{
    public string Value { get; } = value;
    public override XType Type => XType.String;
    public override object Eval(in Ctx ctx) => Value;
}

internal sealed class NumberExpr(double value) : Expr
{
    private readonly object _boxed = value;
    public override XType Type => XType.Number;
    public override object Eval(in Ctx ctx) => _boxed;
}

internal sealed class OrExpr(Expr left, Expr right) : Expr
{
    public override XType Type => XType.Boolean;
    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;
    public override object Eval(in Ctx ctx) => XValue.Box(EvalBool(ctx));
    public override bool EvalBool(in Ctx ctx) => left.EvalBool(ctx) || right.EvalBool(ctx);
}

internal sealed class AndExpr(Expr left, Expr right) : Expr
{
    public override XType Type => XType.Boolean;
    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;
    public override object Eval(in Ctx ctx) => XValue.Box(EvalBool(ctx));
    public override bool EvalBool(in Ctx ctx) => left.EvalBool(ctx) && right.EvalBool(ctx);
}

internal enum CompareOp : byte
{
    Eq,
    Neq,
    Lt,
    Le,
    Gt,
    Ge,
}

internal sealed class CompareExpr(CompareOp op, Expr left, Expr right) : Expr
{
    public override XType Type => XType.Boolean;
    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;
    public override object Eval(in Ctx ctx) => XValue.Box(EvalBool(ctx));
    public override bool EvalBool(in Ctx ctx) => Compare(op, left.Eval(ctx), right.Eval(ctx));

    private static bool Compare(CompareOp op, object a, object b)
    {
        if (a is List<XNode> nodesA)
        {
            if (b is List<XNode> nodesB)
            {
                if (nodesA.Count == 0 || nodesB.Count == 0) return false;

                var valuesB = new string[nodesB.Count];
                for (int i = 0; i < valuesB.Length; i++) valuesB[i] = XNav.StringValue(nodesB[i]);

                foreach (var node in nodesA)
                {
                    var value = XNav.StringValue(node);
                    foreach (var other in valuesB)
                    {
                        if (CompareAtoms(op, value, other)) return true;
                    }
                }
                return false;
            }

            return CompareWithNodes(op, nodesA, b, nodesOnLeft: true);
        }

        if (b is List<XNode> nodes) return CompareWithNodes(op, nodes, a, nodesOnLeft: false);

        return CompareAtoms(op, a, b);
    }

    private static bool CompareWithNodes(CompareOp op, List<XNode> nodes, object other, bool nodesOnLeft)
    {
        if (other is bool)
        {
            var asBool = XValue.Box(nodes.Count > 0);
            return nodesOnLeft ? CompareAtoms(op, asBool, other) : CompareAtoms(op, other, asBool);
        }

        foreach (var node in nodes)
        {
            object value = XNav.StringValue(node);
            if (other is double) value = XValue.ToNumber((string)value);

            if (nodesOnLeft ? CompareAtoms(op, value, other) : CompareAtoms(op, other, value)) return true;
        }
        return false;
    }

    private static bool CompareAtoms(CompareOp op, object a, object b)
    {
        if (op is CompareOp.Eq or CompareOp.Neq)
        {
            bool equal;
            if (a is bool || b is bool) equal = XValue.ToBoolean(a) == XValue.ToBoolean(b);
            else if (a is double || b is double) equal = XValue.ToNumber(a) == XValue.ToNumber(b);
            else equal = string.Equals(XValue.ToString(a), XValue.ToString(b), StringComparison.Ordinal);

            return op == CompareOp.Eq ? equal : !equal;
        }

        double x = XValue.ToNumber(a), y = XValue.ToNumber(b);
        return op switch
        {
            CompareOp.Lt => x < y,
            CompareOp.Le => x <= y,
            CompareOp.Gt => x > y,
            _ => x >= y,
        };
    }
}

internal enum ArithOp : byte
{
    Add,
    Sub,
    Mul,
    Div,
    Mod,
}

internal sealed class ArithExpr(ArithOp op, Expr left, Expr right) : Expr
{
    public override XType Type => XType.Number;
    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;

    public override object Eval(in Ctx ctx)
    {
        double x = left.EvalNumber(ctx), y = right.EvalNumber(ctx);
        return op switch
        {
            ArithOp.Add => x + y,
            ArithOp.Sub => x - y,
            ArithOp.Mul => x * y,
            ArithOp.Div => x / y,
            _ => x % y,
        };
    }
}

internal sealed class NegateExpr(Expr operand) : Expr
{
    public override XType Type => XType.Number;
    public override bool UsesPosition => operand.UsesPosition;
    public override object Eval(in Ctx ctx) => -operand.EvalNumber(ctx);
}

internal sealed class UnionExpr(Expr left, Expr right) : Expr
{
    public override XType Type => XType.NodeSet;
    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;

    public override object Eval(in Ctx ctx)
    {
        var result = new List<XNode>(left.EvalNodes(ctx));
        result.AddRange(right.EvalNodes(ctx));
        XValue.SortUnique(result);
        return result;
    }
}

/// <summary><c>@name = 'literal'</c>: compares the attribute in place, without building a node-set.</summary>
internal sealed class AttributeEqualsExpr(string name, string value, bool negate) : Expr
{
    public override XType Type => XType.Boolean;
    public override object Eval(in Ctx ctx) => XValue.Box(EvalBool(ctx));

    public override bool EvalBool(in Ctx ctx)
    {
        // '!=' is true when the attribute exists and differs (a missing attribute is an empty node-set)
        if (!XNav.TryGetAttribute(ctx.Node, name, out var raw)) return false;

        return SpanHandlingCore.AttributeEquals(raw, value) != negate;
    }
}

/// <summary><c>[@name]</c>: whether the element has the attribute.</summary>
internal sealed class AttributeExistsExpr(string name) : Expr
{
    public override XType Type => XType.Boolean;
    public override object Eval(in Ctx ctx) => XValue.Box(EvalBool(ctx));
    public override bool EvalBool(in Ctx ctx) => XNav.TryGetAttribute(ctx.Node, name, out _);
}

internal enum Axis : byte
{
    Child,
    Descendant,
    DescendantOrSelf,
    Self,
    Parent,
    Ancestor,
    AncestorOrSelf,
    FollowingSibling,
    PrecedingSibling,
    Following,
    Preceding,
    Attribute,
}

internal sealed class Step(Axis axis, NodeTest test, Expr[] predicates)
{
    public Axis Axis { get; } = axis;
    public NodeTest Test { get; } = test;
    public Expr[] Predicates { get; } = predicates;

    private bool IsReverse => Axis is Axis.Ancestor or Axis.AncestorOrSelf or Axis.Preceding or Axis.PrecedingSibling;

    public bool HasPositionalPredicates => Predicates.Any(p => p.IsPositional);

    public List<XNode> Apply(List<XNode> input, EvalState state)
    {
        var result = new List<XNode>();

        if (input.Count == 1)
        {
            Select(input[0], result, state);
            return result;
        }

        var buffer = new List<XNode>();
        foreach (var node in input)
        {
            buffer.Clear();
            Select(node, buffer, state);
            result.AddRange(buffer);
        }

        XValue.SortUnique(result);
        return result;
    }

    /// <summary>The nodes of the axis that pass the test and the predicates, in document order.</summary>
    private void Select(in XNode node, List<XNode> output, EvalState state)
    {
        var start = output.Count;
        Collect(node, output, state);

        if (Predicates.Length > 0 || IsReverse)
        {
            var selected = start == 0 ? output : output.GetRange(start, output.Count - start);
            Predicate.Filter(selected, Predicates, state);
            if (IsReverse) selected.Reverse();

            if (start != 0)
            {
                output.RemoveRange(start, output.Count - start);
                output.AddRange(selected);
            }
        }
    }

    /// <summary>The nodes of the axis that pass the node test, in axis order (reverse axes: nearest first).</summary>
    private void Collect(in XNode node, List<XNode> output, EvalState state)
    {
        var test = Test;
        switch (Axis)
        {
            case Axis.Child:
                XNav.AddChildren(node, test, output);
                break;

            case Axis.Descendant:
                XNav.AddDescendants(node, test, output);
                break;

            case Axis.DescendantOrSelf:
                if (test.Matches(node, attributeAxis: false)) output.Add(node);
                XNav.AddDescendants(node, test, output);
                break;

            case Axis.Self:
                if (test.Matches(node, attributeAxis: false)) output.Add(node);
                break;

            case Axis.Parent:
                if (XNav.TryGetParent(node, out var parent) && test.Matches(parent, attributeAxis: false)) output.Add(parent);
                break;

            case Axis.Ancestor:
            case Axis.AncestorOrSelf:
            {
                var current = node;
                if (Axis == Axis.AncestorOrSelf && test.Matches(current, attributeAxis: false)) output.Add(current);
                while (XNav.TryGetParent(current, out current))
                {
                    if (test.Matches(current, attributeAxis: false)) output.Add(current);
                }
                break;
            }

            case Axis.FollowingSibling:
            case Axis.PrecedingSibling:
                CollectSiblings(node, test, output, following: Axis == Axis.FollowingSibling);
                break;

            case Axis.Following:
                CollectFollowing(node, test, output, state);
                break;

            case Axis.Preceding:
                CollectPreceding(node, test, output, state);
                break;

            case Axis.Attribute:
                XNav.AddAttributes(node, test, output);
                break;
        }
    }

    private static void CollectSiblings(in XNode node, NodeTest test, List<XNode> output, bool following)
    {
        if (node.Kind is XNodeKind.Root or XNodeKind.Attribute) return;

        if (!test.MatchesText && node.Kind != XNodeKind.Text)
        {
            // tags only: walk the flat list; deeper tags are inside the siblings, a shallower one ends the parent
            var tag = node.Tag!;
            var data = tag.Document.Data;
            var depth = tag.Depth;
            var step = following ? 1 : -1;
            for (int i = tag.Index + step; i >= 0 && i < data.Count; i += step)
            {
                var item = data[i];
                if (item.Depth < depth) break;
                if (item.Depth == depth && test.Matches(item)) output.Add(XNode.FromTag(item));
            }
            return;
        }

        if (!XNav.TryGetParent(node, out var parent)) return;

        var siblings = new List<XNode>();
        XNav.AddChildren(parent, NodeTest.AnyNode, siblings);

        var key = node.Key;
        var index = siblings.FindIndex(s => s.Key == key);
        if (index < 0) return;

        if (following)
        {
            for (int i = index + 1; i < siblings.Count; i++)
            {
                if (test.Matches(siblings[i], attributeAxis: false)) output.Add(siblings[i]);
            }
        }
        else
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (test.Matches(siblings[i], attributeAxis: false)) output.Add(siblings[i]);
            }
        }
    }

    private static void CollectFollowing(in XNode node, NodeTest test, List<XNode> output, EvalState state)
    {
        if (node.Kind == XNodeKind.Root) return;
        var after = node.RangeEnd;

        if (test.MatchesText)
        {
            foreach (var item in state.AllNodes())
            {
                if (item.RangeStart > after && test.Matches(item, attributeAxis: false)) output.Add(item);
            }
            return;
        }

        var data = state.Document.Data;
        var first = node.Kind is XNodeKind.Element or XNodeKind.Comment ? node.Tag!.Index + 1 : 0;
        for (int i = first; i < data.Count; i++)
        {
            var tag = data[i];
            if (tag.Start > after && test.Matches(tag)) output.Add(XNode.FromTag(tag));
        }
    }

    private static void CollectPreceding(in XNode node, NodeTest test, List<XNode> output, EvalState state)
    {
        if (node.Kind == XNodeKind.Root) return;
        var before = node.RangeStart;

        if (test.MatchesText)
        {
            var all = state.AllNodes();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var item = all[i];
                if (item.RangeEnd < before && test.Matches(item, attributeAxis: false)) output.Add(item);
            }
            return;
        }

        var data = state.Document.Data;
        var last = node.Kind is XNodeKind.Element or XNodeKind.Comment ? node.Tag!.Index - 1 : data.Count - 1;
        for (int i = last; i >= 0; i--)
        {
            var tag = data[i];
            if (tag.End < before && test.Matches(tag)) output.Add(XNode.FromTag(tag));
        }
    }
}

internal static class Predicate
{
    /// <summary>Applies the predicates one after another; positions are 1-based in the order of the list.</summary>
    public static void Filter(List<XNode> nodes, Expr[] predicates, EvalState state)
    {
        foreach (var predicate in predicates)
        {
            var size = nodes.Count;
            var w = 0;
            for (int i = 0; i < size; i++)
            {
                if (predicate.EvalPredicate(new Ctx(nodes[i], i + 1, size, state))) nodes[w++] = nodes[i];
            }
            nodes.RemoveRange(w, size - w);
        }
    }
}

/// <summary>A location path, absolute or relative, optionally starting from a filter expression: <c>(//a)[1]/@href</c>.</summary>
internal sealed class PathExpr(Expr? start, bool absolute, Step[] steps) : Expr
{
    public Expr? Start { get; } = start;
    public bool Absolute { get; } = absolute;
    public Step[] Steps { get; } = steps;

    public override XType Type => XType.NodeSet;
    public override bool UsesPosition => Start?.UsesPosition ?? false;

    public override object Eval(in Ctx ctx)
    {
        List<XNode> current;
        if (Start != null) current = Start.EvalNodes(ctx);
        else if (Absolute) current = [XNode.Root(ctx.State.Document)];
        else current = [ctx.Node];

        foreach (var step in Steps)
        {
            if (current.Count == 0) break;
            current = step.Apply(current, ctx.State);
        }

        return current;
    }
}

/// <summary>A primary expression with predicates: <c>(//li)[last()]</c>.</summary>
internal sealed class FilterExpr(Expr primary, Expr[] predicates) : Expr
{
    public override XType Type => XType.NodeSet;
    public override bool UsesPosition => primary.UsesPosition;

    public override object Eval(in Ctx ctx)
    {
        var nodes = new List<XNode>(primary.EvalNodes(ctx));
        Predicate.Filter(nodes, predicates, ctx.State);
        return nodes;
    }
}

internal enum Fn : byte
{
    Last, Position, Count, Name, LocalName,
    String, Concat, StartsWith, EndsWith, Contains, SubstringBefore, SubstringAfter, Substring,
    StringLength, NormalizeSpace, Translate, LowerCase, UpperCase,
    Not, True, False, Boolean,
    Number, Sum, Floor, Ceiling, Round,
}

internal sealed class FunctionExpr : Expr
{
    // name → (function, min args, max args, result type)
    internal static readonly Dictionary<string, (Fn Fn, int Min, int Max, XType Type)> Functions = new()
    {
        ["last"] = (Fn.Last, 0, 0, XType.Number),
        ["position"] = (Fn.Position, 0, 0, XType.Number),
        ["count"] = (Fn.Count, 1, 1, XType.Number),
        ["name"] = (Fn.Name, 0, 1, XType.String),
        ["local-name"] = (Fn.LocalName, 0, 1, XType.String),
        ["string"] = (Fn.String, 0, 1, XType.String),
        ["concat"] = (Fn.Concat, 2, int.MaxValue, XType.String),
        ["starts-with"] = (Fn.StartsWith, 2, 2, XType.Boolean),
        ["ends-with"] = (Fn.EndsWith, 2, 2, XType.Boolean),
        ["contains"] = (Fn.Contains, 2, 2, XType.Boolean),
        ["substring-before"] = (Fn.SubstringBefore, 2, 2, XType.String),
        ["substring-after"] = (Fn.SubstringAfter, 2, 2, XType.String),
        ["substring"] = (Fn.Substring, 2, 3, XType.String),
        ["string-length"] = (Fn.StringLength, 0, 1, XType.Number),
        ["normalize-space"] = (Fn.NormalizeSpace, 0, 1, XType.String),
        ["translate"] = (Fn.Translate, 3, 3, XType.String),
        ["lower-case"] = (Fn.LowerCase, 1, 1, XType.String),
        ["upper-case"] = (Fn.UpperCase, 1, 1, XType.String),
        ["not"] = (Fn.Not, 1, 1, XType.Boolean),
        ["true"] = (Fn.True, 0, 0, XType.Boolean),
        ["false"] = (Fn.False, 0, 0, XType.Boolean),
        ["boolean"] = (Fn.Boolean, 1, 1, XType.Boolean),
        ["number"] = (Fn.Number, 0, 1, XType.Number),
        ["sum"] = (Fn.Sum, 1, 1, XType.Number),
        ["floor"] = (Fn.Floor, 1, 1, XType.Number),
        ["ceiling"] = (Fn.Ceiling, 1, 1, XType.Number),
        ["round"] = (Fn.Round, 1, 1, XType.Number),
    };

    private readonly Fn _fn;
    private readonly Expr[] _args;
    private readonly XType _type;

    public FunctionExpr(Fn fn, Expr[] args, XType type)
    {
        _fn = fn;
        _args = args;
        _type = type;
    }

    public override XType Type => _type;

    public override bool UsesPosition => _fn is Fn.Position or Fn.Last || _args.Any(a => a.UsesPosition);

    public override bool EvalBool(in Ctx ctx) => _type == XType.Boolean ? Call(ctx) is true : base.EvalBool(ctx);

    public override object Eval(in Ctx ctx) => Call(ctx);

    private string ArgString(int index, in Ctx ctx) => _args[index].EvalString(ctx);

    private string ContextOrArgString(in Ctx ctx) => _args.Length == 0 ? XNav.StringValue(ctx.Node) : ArgString(0, ctx);

    private object Call(in Ctx ctx)
    {
        switch (_fn)
        {
            case Fn.Last: return (double)ctx.Size;
            case Fn.Position: return (double)ctx.Position;
            case Fn.Count: return (double)_args[0].EvalNodes(ctx).Count;

            case Fn.Name:
            case Fn.LocalName:
            {
                string name;
                if (_args.Length == 0) name = XNav.NodeName(ctx.Node);
                else
                {
                    var nodes = _args[0].EvalNodes(ctx);
                    name = nodes.Count == 0 ? "" : XNav.NodeName(nodes[0]);
                }
                return _fn == Fn.LocalName ? name[(name.LastIndexOf(':') + 1)..] : name;
            }

            case Fn.String: return ContextOrArgString(ctx);

            case Fn.Concat:
            {
                var builder = new StringBuilder();
                foreach (var arg in _args) builder.Append(arg.EvalString(ctx));
                return builder.ToString();
            }

            case Fn.StartsWith: return XValue.Box(ArgString(0, ctx).StartsWith(ArgString(1, ctx), StringComparison.Ordinal));
            case Fn.EndsWith: return XValue.Box(ArgString(0, ctx).EndsWith(ArgString(1, ctx), StringComparison.Ordinal));
            case Fn.Contains: return XValue.Box(ArgString(0, ctx).Contains(ArgString(1, ctx), StringComparison.Ordinal));

            case Fn.SubstringBefore:
            {
                var s = ArgString(0, ctx);
                var index = s.IndexOf(ArgString(1, ctx), StringComparison.Ordinal);
                return index < 0 ? "" : s[..index];
            }

            case Fn.SubstringAfter:
            {
                var s = ArgString(0, ctx);
                var t = ArgString(1, ctx);
                var index = s.IndexOf(t, StringComparison.Ordinal);
                return index < 0 ? "" : s[(index + t.Length)..];
            }

            case Fn.Substring: return Substring(ctx);

            case Fn.StringLength: return (double)ContextOrArgString(ctx).Length;

            case Fn.NormalizeSpace:
                return string.Join(' ', ContextOrArgString(ctx).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            case Fn.Translate:
            {
                var s = ArgString(0, ctx);
                var from = ArgString(1, ctx);
                var to = ArgString(2, ctx);
                var builder = new StringBuilder(s.Length);
                foreach (var c in s)
                {
                    var index = from.IndexOf(c);
                    if (index < 0) builder.Append(c);
                    else if (index < to.Length) builder.Append(to[index]);
                }
                return builder.ToString();
            }

            case Fn.LowerCase: return ArgString(0, ctx).ToLowerInvariant();
            case Fn.UpperCase: return ArgString(0, ctx).ToUpperInvariant();

            case Fn.Not: return XValue.Box(!_args[0].EvalBool(ctx));
            case Fn.True: return XValue.True;
            case Fn.False: return XValue.False;
            case Fn.Boolean: return XValue.Box(_args[0].EvalBool(ctx));

            case Fn.Number: return _args.Length == 0 ? XValue.ToNumber(XNav.StringValue(ctx.Node)) : _args[0].EvalNumber(ctx);

            case Fn.Sum:
            {
                double sum = 0;
                foreach (var node in _args[0].EvalNodes(ctx)) sum += XValue.ToNumber(XNav.StringValue(node));
                return sum;
            }

            case Fn.Floor: return Math.Floor(_args[0].EvalNumber(ctx));
            case Fn.Ceiling: return Math.Ceiling(_args[0].EvalNumber(ctx));
            case Fn.Round: return Round(_args[0].EvalNumber(ctx));

            default: throw new InvalidOperationException();
        }
    }

    private static double Round(double value) => double.IsNaN(value) || double.IsInfinity(value) ? value : Math.Floor(value + 0.5);

    private string Substring(in Ctx ctx)
    {
        // XPath 1.0: the chars at 1-based positions p with round(start) <= p < round(start) + round(length)
        var s = ArgString(0, ctx);
        var from = Round(_args[1].EvalNumber(ctx));
        var to = _args.Length > 2 ? from + Round(_args[2].EvalNumber(ctx)) : double.PositiveInfinity;
        if (double.IsNaN(from) || double.IsNaN(to)) return "";

        var first = from <= 1 ? 0 : from > s.Length ? s.Length : (int)from - 1;
        var end = to >= s.Length + 1 ? s.Length : to <= 1 ? 0 : (int)to - 1;

        return end > first ? s[first..end] : "";
    }
}
