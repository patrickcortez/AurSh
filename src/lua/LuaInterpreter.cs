namespace AurShell.Lua;

public class LuaInterpreter
{
    private readonly LuaScope _globals = new();
    private LuaScope _scope;
    private LuaValue[] _varArgs = Array.Empty<LuaValue>();
    private const int MaxLoopIterations = 1000000;

    public LuaInterpreter()
    {
        _scope = _globals;
        RegisterStdLib();
    }

    public LuaScope Globals => _globals;

    public void SetGlobal(string name, LuaValue value) => _globals.SetLocal(name, value);

    public void SetGlobalFunc(string name, Func<LuaValue[], LuaValue[]> func) =>
        _globals.SetLocal(name, LuaValue.FromFunc(new LuaCSharpFunc(func)));

    public LuaValue GetGlobal(string name) => _globals.Get(name);

    public void Execute(string source, string sourceName = "input")
    {
        try
        {
            var lexer = new LuaLexer(source);
            var tokens = lexer.Tokenize();
            var parser = new LuaParser(tokens);
            var block = parser.ParseBlock();
            ExecBlock(block);
        }
        catch (ReturnSignal) { }
        catch (BreakSignal) { }
        catch (LuaError) { throw; }
        catch (Exception ex) { throw new LuaError($"{sourceName}: {ex.Message}"); }
    }

    private void ExecBlock(BlockNode block)
    {
        var prev = _scope;
        _scope = new LuaScope(_scope);
        try { foreach (var stmt in block.Stmts) ExecStmt(stmt); }
        finally { _scope = prev; }
    }

    private void ExecStmt(Node node)
    {
        switch (node)
        {
            case AssignNode a: ExecAssign(a); break;
            case LocalDeclNode ld: ExecLocalDecl(ld); break;
            case LocalFuncNode lf:
                var closure = MakeClosure(lf.Func);
                _scope.SetLocal(lf.Name, LuaValue.FromFunc(closure));
                break;
            case IfNode i: ExecIf(i); break;
            case WhileNode w: ExecWhile(w); break;
            case NumForNode nf: ExecNumFor(nf); break;
            case GenForNode gf: ExecGenFor(gf); break;
            case ReturnNode r:
                var vals = r.Values.Select(Eval).ToArray();
                throw new ReturnSignal(vals);
            case BreakNode: throw new BreakSignal();
            case FuncCallNode fc: EvalCall(fc); break;
            case MethodCallNode mc: EvalMethodCall(mc); break;
            default: Eval(node); break;
        }
    }

    private void ExecAssign(AssignNode a)
    {
        var values = a.Values.Select(Eval).ToArray();
        for (int i = 0; i < a.Targets.Count; i++)
        {
            var val = i < values.Length ? values[i] : LuaValue.Nil;
            AssignTarget(a.Targets[i], val);
        }
    }

    private void AssignTarget(Node target, LuaValue value)
    {
        switch (target)
        {
            case NameNode n:
                if (!_scope.Set(n.Name, value))
                    _globals.SetLocal(n.Name, value);
                break;
            case FieldAccessNode fa:
                var obj = Eval(fa.Object);
                if (obj.Type != LuaType.Table) throw new LuaError("attempt to index a non-table value", fa.Line);
                obj.TableVal!.SetField(fa.Field, value);
                break;
            case IndexAccessNode ia:
                var tbl = Eval(ia.Object);
                if (tbl.Type != LuaType.Table) throw new LuaError("attempt to index a non-table value", ia.Line);
                tbl.TableVal!.Set(Eval(ia.Key), value);
                break;
        }
    }

    private void ExecLocalDecl(LocalDeclNode ld)
    {
        var values = ld.Values.Select(Eval).ToArray();
        for (int i = 0; i < ld.Names.Count; i++)
            _scope.SetLocal(ld.Names[i], i < values.Length ? values[i] : LuaValue.Nil);
    }

    private void ExecIf(IfNode node)
    {
        foreach (var (cond, body) in node.Branches)
        {
            if (Eval(cond).IsTruthy) { ExecBlock(body); return; }
        }
        if (node.ElseBody != null) ExecBlock(node.ElseBody);
    }

    private void ExecWhile(WhileNode node)
    {
        int iter = 0;
        while (Eval(node.Cond).IsTruthy && iter++ < MaxLoopIterations)
        {
            try { ExecBlock(node.Body); }
            catch (BreakSignal) { break; }
        }
    }

    private void ExecNumFor(NumForNode node)
    {
        double start = Eval(node.Start).AsNumber();
        double stop = Eval(node.Stop).AsNumber();
        double step = node.Step != null ? Eval(node.Step).AsNumber() : 1;
        if (step == 0) throw new LuaError("'for' step is zero", node.Line);
        var prev = _scope;
        _scope = new LuaScope(_scope);
        try
        {
            int iter = 0;
            for (double i = start; step > 0 ? i <= stop : i >= stop; i += step)
            {
                if (iter++ > MaxLoopIterations) break;
                _scope.SetLocal(node.Var, LuaValue.FromNumber(i));
                try { ExecBlock(node.Body); }
                catch (BreakSignal) { break; }
            }
        }
        finally { _scope = prev; }
    }

    private void ExecGenFor(GenForNode node)
    {
        var iterVals = node.Iterators.Select(Eval).ToArray();
        if (iterVals.Length == 0 || iterVals[0].Type != LuaType.Function)
            throw new LuaError("'for' iterator must be a function", node.Line);
        var iterFunc = iterVals[0].FuncVal!;
        var state = iterVals.Length > 1 ? iterVals[1] : LuaValue.Nil;
        var control = iterVals.Length > 2 ? iterVals[2] : LuaValue.Nil;
        var prev = _scope;
        _scope = new LuaScope(_scope);
        try
        {
            int iter = 0;
            while (iter++ < MaxLoopIterations)
            {
                var results = iterFunc.Call(new[] { state, control });
                if (results.Length == 0 || results[0].Type == LuaType.Nil) break;
                control = results[0];
                for (int i = 0; i < node.Vars.Count; i++)
                    _scope.SetLocal(node.Vars[i], i < results.Length ? results[i] : LuaValue.Nil);
                try { ExecBlock(node.Body); }
                catch (BreakSignal) { break; }
            }
        }
        finally { _scope = prev; }
    }

    public LuaValue Eval(Node node)
    {
        return node switch
        {
            NumberNode n => LuaValue.FromNumber(n.Value),
            StringNode s => LuaValue.FromString(s.Value),
            BoolNode b => LuaValue.FromBool(b.Value),
            NilNode => LuaValue.Nil,
            NameNode nm => _scope.Get(nm.Name),
            VarArgsNode => _varArgs.Length > 0 ? _varArgs[0] : LuaValue.Nil,
            BinOpNode bo => EvalBinOp(bo),
            UnOpNode uo => EvalUnOp(uo),
            ConcatNode cc => EvalConcat(cc),
            TableCtorNode tc => EvalTableCtor(tc),
            FieldAccessNode fa => EvalFieldAccess(fa),
            IndexAccessNode ia => EvalIndexAccess(ia),
            FuncCallNode fc => EvalCall(fc),
            MethodCallNode mc => EvalMethodCall(mc),
            FuncDefNode fd => LuaValue.FromFunc(MakeClosure(fd)),
            _ => LuaValue.Nil
        };
    }

    private LuaValue EvalBinOp(BinOpNode bo)
    {
        if (bo.Op == "and") { var l = Eval(bo.Left); return l.IsTruthy ? Eval(bo.Right) : l; }
        if (bo.Op == "or") { var l = Eval(bo.Left); return l.IsTruthy ? l : Eval(bo.Right); }
        var left = Eval(bo.Left);
        var right = Eval(bo.Right);
        return bo.Op switch
        {
            "+" => LuaValue.FromNumber(left.AsNumber() + right.AsNumber()),
            "-" => LuaValue.FromNumber(left.AsNumber() - right.AsNumber()),
            "*" => LuaValue.FromNumber(left.AsNumber() * right.AsNumber()),
            "/" => LuaValue.FromNumber(left.AsNumber() / right.AsNumber()),
            "%" => LuaValue.FromNumber(left.AsNumber() % right.AsNumber()),
            "^" => LuaValue.FromNumber(Math.Pow(left.AsNumber(), right.AsNumber())),
            "==" => LuaValue.FromBool(left.Equals(right)),
            "~=" => LuaValue.FromBool(!left.Equals(right)),
            "<" => LuaValue.FromBool(CompareValues(left, right) < 0),
            "<=" => LuaValue.FromBool(CompareValues(left, right) <= 0),
            ">" => LuaValue.FromBool(CompareValues(left, right) > 0),
            ">=" => LuaValue.FromBool(CompareValues(left, right) >= 0),
            _ => LuaValue.Nil
        };
    }

    private static int CompareValues(LuaValue a, LuaValue b)
    {
        if (a.Type == LuaType.Number && b.Type == LuaType.Number)
            return a.NumVal.CompareTo(b.NumVal);
        if (a.Type == LuaType.String && b.Type == LuaType.String)
            return string.Compare(a.StrVal, b.StrVal, StringComparison.Ordinal);
        throw new LuaError($"attempt to compare {a.TypeName()} with {b.TypeName()}");
    }

    private LuaValue EvalUnOp(UnOpNode uo)
    {
        var operand = Eval(uo.Operand);
        return uo.Op switch
        {
            "-" => LuaValue.FromNumber(-operand.AsNumber()),
            "not" => LuaValue.FromBool(!operand.IsTruthy),
            "#" => operand.Type == LuaType.String
                ? LuaValue.FromNumber(operand.StrVal!.Length)
                : operand.Type == LuaType.Table
                    ? LuaValue.FromNumber(operand.TableVal!.ArrayLength)
                    : throw new LuaError($"attempt to get length of a {operand.TypeName()} value"),
            _ => LuaValue.Nil
        };
    }

    private LuaValue EvalConcat(ConcatNode cc)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in cc.Parts) sb.Append(Eval(part).AsString());
        return LuaValue.FromString(sb.ToString());
    }

    private LuaValue EvalTableCtor(TableCtorNode tc)
    {
        var table = new LuaTable();
        int arrayIdx = 1;
        foreach (var field in tc.Fields)
        {
            var val = Eval(field.Value);
            if (field.Key == null) { table.Set(LuaValue.FromNumber(arrayIdx++), val); }
            else { table.Set(Eval(field.Key), val); }
        }
        return LuaValue.FromTable(table);
    }

    private LuaValue EvalFieldAccess(FieldAccessNode fa)
    {
        var obj = Eval(fa.Object);
        if (obj.Type != LuaType.Table)
            throw new LuaError($"attempt to index a {obj.TypeName()} value (field '{fa.Field}')", fa.Line);
        return obj.TableVal!.GetField(fa.Field);
    }

    private LuaValue EvalIndexAccess(IndexAccessNode ia)
    {
        var obj = Eval(ia.Object);
        if (obj.Type != LuaType.Table)
            throw new LuaError($"attempt to index a {obj.TypeName()} value", ia.Line);
        return obj.TableVal!.Get(Eval(ia.Key));
    }

    private LuaValue EvalCall(FuncCallNode fc)
    {
        var func = Eval(fc.Func);
        if (func.Type != LuaType.Function)
            throw new LuaError($"attempt to call a {func.TypeName()} value", fc.Line);
        var args = fc.Args.Select(Eval).ToArray();
        try { var r = func.FuncVal!.Call(args); return r.Length > 0 ? r[0] : LuaValue.Nil; }
        catch (LuaError) { throw; }
        catch (ReturnSignal) { throw; }
        catch (Exception ex) { throw new LuaError(ex.Message, fc.Line); }
    }

    private LuaValue EvalMethodCall(MethodCallNode mc)
    {
        var obj = Eval(mc.Object);
        if (obj.Type != LuaType.Table)
            throw new LuaError($"attempt to index a {obj.TypeName()} value", mc.Line);
        var method = obj.TableVal!.GetField(mc.Method);
        if (method.Type != LuaType.Function)
            throw new LuaError($"attempt to call a {method.TypeName()} value (method '{mc.Method}')", mc.Line);
        var args = new List<LuaValue> { obj };
        args.AddRange(mc.Args.Select(Eval));
        try { var r = method.FuncVal!.Call(args.ToArray()); return r.Length > 0 ? r[0] : LuaValue.Nil; }
        catch (LuaError) { throw; }
        catch (ReturnSignal) { throw; }
        catch (Exception ex) { throw new LuaError(ex.Message, mc.Line); }
    }

    private LuaCallable MakeClosure(FuncDefNode fd)
    {
        var capturedScope = _scope;
        var interpreter = this;
        return new LuaCSharpFunc(args =>
        {
            var prev = interpreter._scope;
            var prevVarArgs = interpreter._varArgs;
            interpreter._scope = new LuaScope(capturedScope);
            try
            {
                for (int i = 0; i < fd.Params.Count; i++)
                    interpreter._scope.SetLocal(fd.Params[i], i < args.Length ? args[i] : LuaValue.Nil);
                if (fd.HasVarArgs)
                    interpreter._varArgs = args.Skip(fd.Params.Count).ToArray();
                interpreter.ExecBlock(fd.Body);
                return new[] { LuaValue.Nil };
            }
            catch (ReturnSignal ret) { return ret.Values; }
            finally { interpreter._scope = prev; interpreter._varArgs = prevVarArgs; }
        });
    }

    private void RegisterStdLib()
    {
        SetGlobalFunc("print", args => { Console.WriteLine(string.Join("\t", args.Select(a => a.AsString()))); return Array.Empty<LuaValue>(); });
        SetGlobalFunc("tostring", args => new[] { LuaValue.FromString(args.Length > 0 ? args[0].AsString() : "nil") });
        SetGlobalFunc("tonumber", args =>
        {
            if (args.Length == 0) return new[] { LuaValue.Nil };
            if (args[0].Type == LuaType.Number) return new[] { args[0] };
            if (args[0].Type == LuaType.String && double.TryParse(args[0].StrVal,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                return new[] { LuaValue.FromNumber(d) };
            return new[] { LuaValue.Nil };
        });
        SetGlobalFunc("type", args => new[] { LuaValue.FromString(args.Length > 0 ? args[0].TypeName() : "nil") });
        SetGlobalFunc("error", args => { throw new LuaError(args.Length > 0 ? args[0].AsString() : "error"); });
        SetGlobalFunc("pcall", args =>
        {
            if (args.Length == 0) return new[] { LuaValue.False, LuaValue.FromString("no function") };
            if (args[0].Type != LuaType.Function) return new[] { LuaValue.False, LuaValue.FromString("not a function") };
            try
            {
                var result = args[0].FuncVal!.Call(args.Skip(1).ToArray());
                var ret = new List<LuaValue> { LuaValue.True };
                ret.AddRange(result);
                return ret.ToArray();
            }
            catch (Exception ex) { return new[] { LuaValue.False, LuaValue.FromString(ex.Message) }; }
        });
        SetGlobalFunc("pairs", args =>
        {
            if (args.Length == 0 || args[0].Type != LuaType.Table) throw new LuaError("bad argument to 'pairs' (table expected)");
            var allPairs = args[0].TableVal!.AllPairs();
            int idx = -1;
            var iterFunc = new LuaCSharpFunc(_ =>
            {
                idx++;
                if (idx >= allPairs.Count) return new[] { LuaValue.Nil };
                return new[] { allPairs[idx].Key, allPairs[idx].Value };
            });
            return new[] { LuaValue.FromFunc(iterFunc), args[0], LuaValue.Nil };
        });
        SetGlobalFunc("ipairs", args =>
        {
            if (args.Length == 0 || args[0].Type != LuaType.Table) throw new LuaError("bad argument to 'ipairs' (table expected)");
            var arr = args[0].TableVal!.GetArray();
            int idx = -1;
            var iterFunc = new LuaCSharpFunc(_ =>
            {
                idx++;
                if (idx >= arr.Count) return new[] { LuaValue.Nil };
                return new[] { LuaValue.FromNumber(idx + 1), arr[idx] };
            });
            return new[] { LuaValue.FromFunc(iterFunc), args[0], LuaValue.FromNumber(0) };
        });
        SetGlobalFunc("select", args =>
        {
            if (args.Length == 0) return Array.Empty<LuaValue>();
            if (args[0].Type == LuaType.String && args[0].StrVal == "#")
                return new[] { LuaValue.FromNumber(args.Length - 1) };
            int n = (int)args[0].AsNumber();
            if (n < 1) n = 1;
            return args.Skip(n).ToArray();
        });
        SetGlobalFunc("unpack", args =>
        {
            if (args.Length == 0 || args[0].Type != LuaType.Table) return Array.Empty<LuaValue>();
            return args[0].TableVal!.GetArray().ToArray();
        });

        var tableTbl = new LuaTable();
        tableTbl.SetField("insert", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length < 2 || args[0].Type != LuaType.Table) return Array.Empty<LuaValue>();
            if (args.Length == 2) { args[0].TableVal!.Append(args[1]); }
            else { args[0].TableVal!.Insert((int)args[1].AsNumber(), args[2]); }
            return Array.Empty<LuaValue>();
        })));
        tableTbl.SetField("remove", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length == 0 || args[0].Type != LuaType.Table) return Array.Empty<LuaValue>();
            int pos = args.Length > 1 ? (int)args[1].AsNumber() : args[0].TableVal!.ArrayLength;
            return new[] { args[0].TableVal!.Remove(pos) };
        })));
        tableTbl.SetField("concat", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length == 0 || args[0].Type != LuaType.Table) return new[] { LuaValue.FromString("") };
            string sep = args.Length > 1 ? args[1].AsString() : "";
            var arr = args[0].TableVal!.GetArray();
            return new[] { LuaValue.FromString(string.Join(sep, arr.Select(v => v.AsString()))) };
        })));
        SetGlobal("table", LuaValue.FromTable(tableTbl));

        var stringTbl = new LuaTable();
        stringTbl.SetField("sub", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length < 2) return new[] { LuaValue.FromString("") };
            string s = args[0].AsString();
            int i = (int)args[1].AsNumber();
            if (i < 0) i = Math.Max(1, s.Length + i + 1);
            if (i < 1) i = 1;
            int j = args.Length > 2 ? (int)args[2].AsNumber() : s.Length;
            if (j < 0) j = s.Length + j + 1;
            if (j > s.Length) j = s.Length;
            if (i > j) return new[] { LuaValue.FromString("") };
            return new[] { LuaValue.FromString(s.Substring(i - 1, j - i + 1)) };
        })));
        stringTbl.SetField("len", LuaValue.FromFunc(new LuaCSharpFunc(args =>
            new[] { LuaValue.FromNumber(args.Length > 0 ? args[0].AsString().Length : 0) })));
        stringTbl.SetField("upper", LuaValue.FromFunc(new LuaCSharpFunc(args =>
            new[] { LuaValue.FromString(args.Length > 0 ? args[0].AsString().ToUpperInvariant() : "") })));
        stringTbl.SetField("lower", LuaValue.FromFunc(new LuaCSharpFunc(args =>
            new[] { LuaValue.FromString(args.Length > 0 ? args[0].AsString().ToLowerInvariant() : "") })));
        stringTbl.SetField("rep", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length < 2) return new[] { LuaValue.FromString("") };
            string s = args[0].AsString();
            int n = (int)args[1].AsNumber();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(s);
            return new[] { LuaValue.FromString(sb.ToString()) };
        })));
        stringTbl.SetField("find", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length < 2) return new[] { LuaValue.Nil };
            string s = args[0].AsString();
            string pattern = args[1].AsString();
            int init = args.Length > 2 ? (int)args[2].AsNumber() - 1 : 0;
            if (init < 0) init = Math.Max(0, s.Length + init + 1);
            int idx = s.IndexOf(pattern, init, StringComparison.Ordinal);
            if (idx < 0) return new[] { LuaValue.Nil };
            return new[] { LuaValue.FromNumber(idx + 1), LuaValue.FromNumber(idx + pattern.Length) };
        })));
        stringTbl.SetField("format", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length == 0) return new[] { LuaValue.FromString("") };
            string fmt = args[0].AsString();
            var sb = new System.Text.StringBuilder();
            int argIdx = 1;
            for (int i = 0; i < fmt.Length; i++)
            {
                if (fmt[i] == '%' && i + 1 < fmt.Length)
                {
                    char spec = fmt[i + 1];
                    if (spec == '%') { sb.Append('%'); i++; continue; }
                    string argVal = argIdx < args.Length ? args[argIdx].AsString() : "";
                    double argNum = argIdx < args.Length && args[argIdx].Type == LuaType.Number ? args[argIdx].NumVal : 0;
                    switch (spec)
                    {
                        case 's': sb.Append(argVal); break;
                        case 'd': sb.Append((int)argNum); break;
                        case 'f': sb.Append(argNum.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)); break;
                        case 'x': sb.Append(((int)argNum).ToString("x")); break;
                        case 'X': sb.Append(((int)argNum).ToString("X")); break;
                        case 'q': sb.Append('"'); sb.Append(argVal.Replace("\\", "\\\\").Replace("\"", "\\\"")); sb.Append('"'); break;
                        default: sb.Append('%'); sb.Append(spec); argIdx--; break;
                    }
                    argIdx++; i++;
                }
                else sb.Append(fmt[i]);
            }
            return new[] { LuaValue.FromString(sb.ToString()) };
        })));
        SetGlobal("string", LuaValue.FromTable(stringTbl));

        var mathTbl = new LuaTable();
        mathTbl.SetField("floor", LuaValue.FromFunc(new LuaCSharpFunc(a => new[] { LuaValue.FromNumber(Math.Floor(a[0].AsNumber())) })));
        mathTbl.SetField("ceil", LuaValue.FromFunc(new LuaCSharpFunc(a => new[] { LuaValue.FromNumber(Math.Ceiling(a[0].AsNumber())) })));
        mathTbl.SetField("abs", LuaValue.FromFunc(new LuaCSharpFunc(a => new[] { LuaValue.FromNumber(Math.Abs(a[0].AsNumber())) })));
        mathTbl.SetField("max", LuaValue.FromFunc(new LuaCSharpFunc(a =>
        {
            double m = a[0].AsNumber();
            for (int i = 1; i < a.Length; i++) m = Math.Max(m, a[i].AsNumber());
            return new[] { LuaValue.FromNumber(m) };
        })));
        mathTbl.SetField("min", LuaValue.FromFunc(new LuaCSharpFunc(a =>
        {
            double m = a[0].AsNumber();
            for (int i = 1; i < a.Length; i++) m = Math.Min(m, a[i].AsNumber());
            return new[] { LuaValue.FromNumber(m) };
        })));
        mathTbl.SetField("sqrt", LuaValue.FromFunc(new LuaCSharpFunc(a => new[] { LuaValue.FromNumber(Math.Sqrt(a[0].AsNumber())) })));
        mathTbl.SetField("random", LuaValue.FromFunc(new LuaCSharpFunc(a =>
        {
            if (a.Length == 0) return new[] { LuaValue.FromNumber(Random.Shared.NextDouble()) };
            if (a.Length == 1) return new[] { LuaValue.FromNumber(Random.Shared.Next(1, (int)a[0].AsNumber() + 1)) };
            return new[] { LuaValue.FromNumber(Random.Shared.Next((int)a[0].AsNumber(), (int)a[1].AsNumber() + 1)) };
        })));
        mathTbl.SetField("pi", LuaValue.FromNumber(Math.PI));
        mathTbl.SetField("huge", LuaValue.FromNumber(double.PositiveInfinity));
        SetGlobal("math", LuaValue.FromTable(mathTbl));
    }
}
