namespace AurShell.Lua;

public abstract class Node { public int Line; }

public class BlockNode : Node { public List<Node> Stmts = new(); }
public class NumberNode : Node { public double Value; }
public class StringNode : Node { public string Value = ""; }
public class BoolNode : Node { public bool Value; }
public class NilNode : Node { }
public class NameNode : Node { public string Name = ""; }
public class VarArgsNode : Node { }
public class BinOpNode : Node { public string Op = ""; public Node Left = null!; public Node Right = null!; }
public class UnOpNode : Node { public string Op = ""; public Node Operand = null!; }
public class ConcatNode : Node { public List<Node> Parts = new(); }
public class TableCtorNode : Node { public List<TableField> Fields = new(); }
public class TableField { public Node? Key; public Node Value = null!; }
public class FieldAccessNode : Node { public Node Object = null!; public string Field = ""; }
public class IndexAccessNode : Node { public Node Object = null!; public Node Key = null!; }
public class FuncCallNode : Node { public Node Func = null!; public List<Node> Args = new(); }
public class MethodCallNode : Node { public Node Object = null!; public string Method = ""; public List<Node> Args = new(); }
public class FuncDefNode : Node { public List<string> Params = new(); public bool HasVarArgs; public BlockNode Body = null!; }
public class AssignNode : Node { public List<Node> Targets = new(); public List<Node> Values = new(); }
public class LocalDeclNode : Node { public List<string> Names = new(); public List<Node> Values = new(); }
public class LocalFuncNode : Node { public string Name = ""; public FuncDefNode Func = null!; }
public class IfNode : Node { public List<(Node Cond, BlockNode Body)> Branches = new(); public BlockNode? ElseBody; }
public class WhileNode : Node { public Node Cond = null!; public BlockNode Body = null!; }
public class NumForNode : Node { public string Var = ""; public Node Start = null!; public Node Stop = null!; public Node? Step; public BlockNode Body = null!; }
public class GenForNode : Node { public List<string> Vars = new(); public List<Node> Iterators = new(); public BlockNode Body = null!; }
public class ReturnNode : Node { public List<Node> Values = new(); }
public class BreakNode : Node { }

public class LuaParser
{
    private readonly List<Tk> _tokens;
    private int _pos;

    public LuaParser(List<Tk> tokens) { _tokens = tokens; }

    private Tk Cur => _pos < _tokens.Count ? _tokens[_pos] : _tokens[_tokens.Count - 1];
    private int Ln => Cur.Line;
    private Tk Advance() { var t = Cur; _pos++; return t; }
    private bool Check(TkType t) => Cur.Type == t;
    private bool Match(TkType t) { if (Check(t)) { _pos++; return true; } return false; }

    private Tk Expect(TkType t)
    {
        if (Cur.Type != t) throw new LuaError($"expected '{TkName(t)}', got '{Cur.Value}'", Ln);
        return Advance();
    }

    private static string TkName(TkType t) => t switch
    {
        TkType.Name => "name", TkType.Eof => "eof", TkType.End => "end",
        TkType.Then => "then", TkType.Do => "do", TkType.RParen => ")",
        TkType.RBracket => "]", TkType.Assign => "=", TkType.Comma => ",",
        _ => t.ToString().ToLowerInvariant()
    };

    public BlockNode ParseBlock()
    {
        var block = new BlockNode { Line = Ln };
        while (!IsBlockEnd())
        {
            var stmt = ParseStatement();
            if (stmt != null) block.Stmts.Add(stmt);
            Match(TkType.Semi);
        }
        return block;
    }

    private bool IsBlockEnd() => Cur.Type is TkType.Eof or TkType.End or TkType.Else or TkType.Elseif;

    private Node? ParseStatement()
    {
        switch (Cur.Type)
        {
            case TkType.If: return ParseIf();
            case TkType.While: return ParseWhile();
            case TkType.For: return ParseFor();
            case TkType.Return: return ParseReturn();
            case TkType.Break: Advance(); return new BreakNode { Line = Ln };
            case TkType.Local: return ParseLocal();
            case TkType.Function: return ParseFuncStatement();
            default: return ParseExprStatement();
        }
    }

    private Node ParseIf()
    {
        var node = new IfNode { Line = Ln };
        Advance();
        var cond = ParseExpr();
        Expect(TkType.Then);
        var body = ParseBlock();
        node.Branches.Add((cond, body));
        while (Match(TkType.Elseif))
        {
            cond = ParseExpr();
            Expect(TkType.Then);
            body = ParseBlock();
            node.Branches.Add((cond, body));
        }
        if (Match(TkType.Else))
            node.ElseBody = ParseBlock();
        Expect(TkType.End);
        return node;
    }

    private Node ParseWhile()
    {
        int ln = Ln; Advance();
        var cond = ParseExpr();
        Expect(TkType.Do);
        var body = ParseBlock();
        Expect(TkType.End);
        return new WhileNode { Line = ln, Cond = cond, Body = body };
    }

    private Node ParseFor()
    {
        int ln = Ln; Advance();
        string firstName = Expect(TkType.Name).Value;
        if (Match(TkType.Assign))
        {
            var start = ParseExpr();
            Expect(TkType.Comma);
            var stop = ParseExpr();
            Node? step = null;
            if (Match(TkType.Comma)) step = ParseExpr();
            Expect(TkType.Do);
            var body = ParseBlock();
            Expect(TkType.End);
            return new NumForNode { Line = ln, Var = firstName, Start = start, Stop = stop, Step = step, Body = body };
        }
        var vars = new List<string> { firstName };
        while (Match(TkType.Comma))
            vars.Add(Expect(TkType.Name).Value);
        Expect(TkType.In);
        var iters = new List<Node> { ParseExpr() };
        while (Match(TkType.Comma))
            iters.Add(ParseExpr());
        Expect(TkType.Do);
        var genBody = ParseBlock();
        Expect(TkType.End);
        return new GenForNode { Line = ln, Vars = vars, Iterators = iters, Body = genBody };
    }

    private Node ParseReturn()
    {
        int ln = Ln; Advance();
        var ret = new ReturnNode { Line = ln };
        if (!IsBlockEnd() && Cur.Type != TkType.Semi)
        {
            ret.Values.Add(ParseExpr());
            while (Match(TkType.Comma))
                ret.Values.Add(ParseExpr());
        }
        Match(TkType.Semi);
        return ret;
    }

    private Node ParseLocal()
    {
        int ln = Ln; Advance();
        if (Check(TkType.Function))
        {
            Advance();
            string name = Expect(TkType.Name).Value;
            var func = ParseFuncBody(ln);
            return new LocalFuncNode { Line = ln, Name = name, Func = func };
        }
        var names = new List<string> { Expect(TkType.Name).Value };
        while (Match(TkType.Comma))
            names.Add(Expect(TkType.Name).Value);
        var values = new List<Node>();
        if (Match(TkType.Assign))
        {
            values.Add(ParseExpr());
            while (Match(TkType.Comma))
                values.Add(ParseExpr());
        }
        return new LocalDeclNode { Line = ln, Names = names, Values = values };
    }

    private Node ParseFuncStatement()
    {
        int ln = Ln; Advance();
        var name = Expect(TkType.Name).Value;
        Node target = new NameNode { Line = ln, Name = name };
        bool isMethod = false;
        while (Match(TkType.Dot))
        {
            string field = Expect(TkType.Name).Value;
            target = new FieldAccessNode { Line = ln, Object = target, Field = field };
        }
        if (Match(TkType.Colon))
        {
            string method = Expect(TkType.Name).Value;
            target = new FieldAccessNode { Line = ln, Object = target, Field = method };
            isMethod = true;
        }
        var func = ParseFuncBody(ln);
        if (isMethod)
            func.Params.Insert(0, "self");
        return new AssignNode { Line = ln, Targets = new List<Node> { target }, Values = new List<Node> { func } };
    }

    private FuncDefNode ParseFuncBody(int ln)
    {
        Expect(TkType.LParen);
        var parms = new List<string>();
        bool varArgs = false;
        if (!Check(TkType.RParen))
        {
            if (Check(TkType.Dots)) { varArgs = true; Advance(); }
            else
            {
                parms.Add(Expect(TkType.Name).Value);
                while (Match(TkType.Comma))
                {
                    if (Check(TkType.Dots)) { varArgs = true; Advance(); break; }
                    parms.Add(Expect(TkType.Name).Value);
                }
            }
        }
        Expect(TkType.RParen);
        var body = ParseBlock();
        Expect(TkType.End);
        return new FuncDefNode { Line = ln, Params = parms, HasVarArgs = varArgs, Body = body };
    }

    private Node ParseExprStatement()
    {
        var expr = ParseSuffixExpr();
        if (expr is FuncCallNode || expr is MethodCallNode)
            return expr;
        if (Check(TkType.Assign) || Check(TkType.Comma))
        {
            var targets = new List<Node> { expr };
            while (Match(TkType.Comma))
                targets.Add(ParseSuffixExpr());
            Expect(TkType.Assign);
            var values = new List<Node> { ParseExpr() };
            while (Match(TkType.Comma))
                values.Add(ParseExpr());
            return new AssignNode { Line = expr.Line, Targets = targets, Values = values };
        }
        return expr;
    }

    public Node ParseExpr() => ParseOr();

    private Node ParseOr()
    {
        var left = ParseAnd();
        while (Match(TkType.Or))
            left = new BinOpNode { Line = left.Line, Op = "or", Left = left, Right = ParseAnd() };
        return left;
    }

    private Node ParseAnd()
    {
        var left = ParseComparison();
        while (Match(TkType.And))
            left = new BinOpNode { Line = left.Line, Op = "and", Left = left, Right = ParseComparison() };
        return left;
    }

    private Node ParseComparison()
    {
        var left = ParseConcat();
        while (Cur.Type is TkType.Lt or TkType.Le or TkType.Gt or TkType.Ge or TkType.Eq or TkType.Neq)
        {
            string op = Advance().Value;
            left = new BinOpNode { Line = left.Line, Op = op, Left = left, Right = ParseConcat() };
        }
        return left;
    }

    private Node ParseConcat()
    {
        var left = ParseAdd();
        if (Check(TkType.DotDot))
        {
            var parts = new ConcatNode { Line = left.Line };
            parts.Parts.Add(left);
            while (Match(TkType.DotDot))
                parts.Parts.Add(ParseAdd());
            return parts;
        }
        return left;
    }

    private Node ParseAdd()
    {
        var left = ParseMul();
        while (Cur.Type is TkType.Plus or TkType.Minus)
        {
            string op = Advance().Value;
            left = new BinOpNode { Line = left.Line, Op = op, Left = left, Right = ParseMul() };
        }
        return left;
    }

    private Node ParseMul()
    {
        var left = ParseUnary();
        while (Cur.Type is TkType.Star or TkType.Slash or TkType.Percent)
        {
            string op = Advance().Value;
            left = new BinOpNode { Line = left.Line, Op = op, Left = left, Right = ParseUnary() };
        }
        return left;
    }

    private Node ParseUnary()
    {
        if (Check(TkType.Not)) { int ln = Ln; Advance(); return new UnOpNode { Line = ln, Op = "not", Operand = ParseUnary() }; }
        if (Check(TkType.Hash)) { int ln = Ln; Advance(); return new UnOpNode { Line = ln, Op = "#", Operand = ParseUnary() }; }
        if (Check(TkType.Minus)) { int ln = Ln; Advance(); return new UnOpNode { Line = ln, Op = "-", Operand = ParseUnary() }; }
        return ParsePower();
    }

    private Node ParsePower()
    {
        var left = ParseSuffixExpr();
        if (Match(TkType.Caret))
            left = new BinOpNode { Line = left.Line, Op = "^", Left = left, Right = ParseUnary() };
        return left;
    }

    private Node ParseSuffixExpr()
    {
        var expr = ParsePrimary();
        while (true)
        {
            if (Match(TkType.Dot))
            {
                string field = Expect(TkType.Name).Value;
                expr = new FieldAccessNode { Line = expr.Line, Object = expr, Field = field };
            }
            else if (Check(TkType.LBracket))
            {
                Advance();
                var key = ParseExpr();
                Expect(TkType.RBracket);
                expr = new IndexAccessNode { Line = expr.Line, Object = expr, Key = key };
            }
            else if (Check(TkType.Colon))
            {
                Advance();
                string method = Expect(TkType.Name).Value;
                var args = ParseCallArgs();
                expr = new MethodCallNode { Line = expr.Line, Object = expr, Method = method, Args = args };
            }
            else if (Check(TkType.LParen) || Check(TkType.LBrace) || Check(TkType.String))
            {
                var args = ParseCallArgs();
                expr = new FuncCallNode { Line = expr.Line, Func = expr, Args = args };
            }
            else break;
        }
        return expr;
    }

    private List<Node> ParseCallArgs()
    {
        if (Match(TkType.LParen))
        {
            var args = new List<Node>();
            if (!Check(TkType.RParen))
            {
                args.Add(ParseExpr());
                while (Match(TkType.Comma))
                    args.Add(ParseExpr());
            }
            Expect(TkType.RParen);
            return args;
        }
        if (Check(TkType.LBrace))
            return new List<Node> { ParseTableCtor() };
        if (Check(TkType.String))
            return new List<Node> { new StringNode { Line = Ln, Value = Advance().Value } };
        throw new LuaError("function arguments expected", Ln);
    }

    private Node ParsePrimary()
    {
        switch (Cur.Type)
        {
            case TkType.Number:
                double num = ParseDouble(Advance().Value);
                return new NumberNode { Line = Ln, Value = num };
            case TkType.String:
                return new StringNode { Line = Ln, Value = Advance().Value };
            case TkType.True: Advance(); return new BoolNode { Line = Ln, Value = true };
            case TkType.False: Advance(); return new BoolNode { Line = Ln, Value = false };
            case TkType.Nil: Advance(); return new NilNode { Line = Ln };
            case TkType.Name: return new NameNode { Line = Ln, Name = Advance().Value };
            case TkType.Dots: Advance(); return new VarArgsNode { Line = Ln };
            case TkType.Function:
                int ln = Ln; Advance();
                return ParseFuncBody(ln);
            case TkType.LBrace:
                return ParseTableCtor();
            case TkType.LParen:
                Advance();
                var expr = ParseExpr();
                Expect(TkType.RParen);
                return expr;
            default:
                throw new LuaError($"unexpected symbol '{Cur.Value}'", Ln);
        }
    }

    private Node ParseTableCtor()
    {
        int ln = Ln; Expect(TkType.LBrace);
        var node = new TableCtorNode { Line = ln };
        while (!Check(TkType.RBrace))
        {
            if (Check(TkType.LBracket))
            {
                Advance();
                var key = ParseExpr();
                Expect(TkType.RBracket);
                Expect(TkType.Assign);
                var val = ParseExpr();
                node.Fields.Add(new TableField { Key = key, Value = val });
            }
            else if (Check(TkType.Name) && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TkType.Assign)
            {
                string name = Advance().Value;
                Advance();
                var val = ParseExpr();
                node.Fields.Add(new TableField { Key = new StringNode { Line = ln, Value = name }, Value = val });
            }
            else
            {
                var val = ParseExpr();
                node.Fields.Add(new TableField { Key = null, Value = val });
            }
            if (!Match(TkType.Comma) && !Match(TkType.Semi)) break;
        }
        Expect(TkType.RBrace);
        return node;
    }

    private static double ParseDouble(string s)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(s, 16);
        return double.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
    }
}
