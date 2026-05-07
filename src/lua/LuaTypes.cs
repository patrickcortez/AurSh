namespace AurShell.Lua;

public class LuaError : Exception
{
    public LuaError(string message) : base(message) { }
    public LuaError(string message, int line) : base($"[line {line}] {message}") { }
}

public enum LuaType { Nil, Boolean, Number, String, Table, Function }

public class LuaValue
{
    public static readonly LuaValue Nil = new() { Type = LuaType.Nil };
    public static readonly LuaValue True = new() { Type = LuaType.Boolean, BoolVal = true };
    public static readonly LuaValue False = new() { Type = LuaType.Boolean, BoolVal = false };

    public LuaType Type;
    public bool BoolVal;
    public double NumVal;
    public string? StrVal;
    public LuaTable? TableVal;
    public LuaCallable? FuncVal;

    public static LuaValue FromNumber(double n) => new() { Type = LuaType.Number, NumVal = n };
    public static LuaValue FromString(string s) => new() { Type = LuaType.String, StrVal = s };
    public static LuaValue FromBool(bool b) => b ? True : False;
    public static LuaValue FromTable(LuaTable t) => new() { Type = LuaType.Table, TableVal = t };
    public static LuaValue FromFunc(LuaCallable f) => new() { Type = LuaType.Function, FuncVal = f };

    public bool IsTruthy => Type != LuaType.Nil && !(Type == LuaType.Boolean && !BoolVal);

    public double AsNumber()
    {
        if (Type == LuaType.Number) return NumVal;
        if (Type == LuaType.String && double.TryParse(StrVal, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
        throw new LuaError($"attempt to perform arithmetic on a {TypeName()} value");
    }

    public string AsString()
    {
        return Type switch
        {
            LuaType.String => StrVal ?? "",
            LuaType.Number => NumVal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LuaType.Boolean => BoolVal ? "true" : "false",
            LuaType.Nil => "nil",
            LuaType.Table => "table: 0x" + (TableVal?.GetHashCode() ?? 0).ToString("x8"),
            LuaType.Function => "function: 0x" + (FuncVal?.GetHashCode() ?? 0).ToString("x8"),
            _ => "nil"
        };
    }

    public string TypeName() => Type switch
    {
        LuaType.Nil => "nil",
        LuaType.Boolean => "boolean",
        LuaType.Number => "number",
        LuaType.String => "string",
        LuaType.Table => "table",
        LuaType.Function => "function",
        _ => "nil"
    };

    public override bool Equals(object? obj)
    {
        if (obj is not LuaValue other) return false;
        if (Type != other.Type) return false;
        return Type switch
        {
            LuaType.Nil => true,
            LuaType.Boolean => BoolVal == other.BoolVal,
            LuaType.Number => NumVal == other.NumVal,
            LuaType.String => StrVal == other.StrVal,
            _ => ReferenceEquals(this, other)
        };
    }

    public override int GetHashCode()
    {
        return Type switch
        {
            LuaType.Nil => 0,
            LuaType.Boolean => BoolVal.GetHashCode(),
            LuaType.Number => NumVal.GetHashCode(),
            LuaType.String => (StrVal ?? "").GetHashCode(),
            _ => base.GetHashCode()
        };
    }
}

public class LuaTable
{
    private readonly List<LuaValue> _array = new();
    private readonly Dictionary<string, LuaValue> _hash = new(StringComparer.Ordinal);

    public int ArrayLength => _array.Count;

    public LuaValue Get(LuaValue key)
    {
        if (key.Type == LuaType.Number)
        {
            int idx = (int)key.NumVal;
            if (idx == key.NumVal && idx >= 1 && idx <= _array.Count)
                return _array[idx - 1];
        }
        if (key.Type == LuaType.String && key.StrVal != null)
        {
            if (_hash.TryGetValue(key.StrVal, out var val))
                return val;
        }
        return LuaValue.Nil;
    }

    public LuaValue GetField(string name)
    {
        return _hash.TryGetValue(name, out var val) ? val : LuaValue.Nil;
    }

    public void Set(LuaValue key, LuaValue value)
    {
        if (key.Type == LuaType.Number)
        {
            int idx = (int)key.NumVal;
            if (idx == key.NumVal && idx >= 1)
            {
                if (idx <= _array.Count)
                {
                    if (value.Type == LuaType.Nil)
                        _array[idx - 1] = LuaValue.Nil;
                    else
                        _array[idx - 1] = value;
                    return;
                }
                if (idx == _array.Count + 1)
                {
                    _array.Add(value);
                    return;
                }
            }
        }
        if (key.Type == LuaType.String && key.StrVal != null)
        {
            if (value.Type == LuaType.Nil)
                _hash.Remove(key.StrVal);
            else
                _hash[key.StrVal] = value;
            return;
        }
        if (key.Type == LuaType.Nil)
            throw new LuaError("table index is nil");
    }

    public void SetField(string name, LuaValue value)
    {
        if (value.Type == LuaType.Nil)
            _hash.Remove(name);
        else
            _hash[name] = value;
    }

    public void Append(LuaValue value)
    {
        _array.Add(value);
    }

    public void Insert(int pos, LuaValue value)
    {
        if (pos < 1) pos = 1;
        if (pos > _array.Count + 1) pos = _array.Count + 1;
        _array.Insert(pos - 1, value);
    }

    public LuaValue Remove(int pos)
    {
        if (pos < 1 || pos > _array.Count) return LuaValue.Nil;
        var val = _array[pos - 1];
        _array.RemoveAt(pos - 1);
        return val;
    }

    public List<LuaValue> GetArray() => _array;
    public Dictionary<string, LuaValue> GetHash() => _hash;

    public List<(LuaValue Key, LuaValue Value)> AllPairs()
    {
        var pairs = new List<(LuaValue, LuaValue)>();
        for (int i = 0; i < _array.Count; i++)
            pairs.Add((LuaValue.FromNumber(i + 1), _array[i]));
        foreach (var kv in _hash)
            pairs.Add((LuaValue.FromString(kv.Key), kv.Value));
        return pairs;
    }
}

public abstract class LuaCallable
{
    public abstract LuaValue[] Call(LuaValue[] args);
}

public class LuaCSharpFunc : LuaCallable
{
    private readonly Func<LuaValue[], LuaValue[]> _func;
    public LuaCSharpFunc(Func<LuaValue[], LuaValue[]> func) { _func = func; }
    public override LuaValue[] Call(LuaValue[] args) => _func(args);
}

public class LuaScope
{
    private readonly Dictionary<string, LuaValue> _locals = new(StringComparer.Ordinal);
    public LuaScope? Parent { get; }

    public LuaScope(LuaScope? parent = null) { Parent = parent; }

    public LuaValue Get(string name)
    {
        if (_locals.TryGetValue(name, out var val)) return val;
        return Parent?.Get(name) ?? LuaValue.Nil;
    }

    public void SetLocal(string name, LuaValue value) => _locals[name] = value;

    public bool Set(string name, LuaValue value)
    {
        if (_locals.ContainsKey(name)) { _locals[name] = value; return true; }
        if (Parent != null) return Parent.Set(name, value);
        return false;
    }

    public bool Has(string name)
    {
        if (_locals.ContainsKey(name)) return true;
        return Parent?.Has(name) ?? false;
    }
}

public class BreakSignal : Exception { public BreakSignal() : base() { } }

public class ReturnSignal : Exception
{
    public LuaValue[] Values { get; }
    public ReturnSignal(LuaValue[] values) : base() { Values = values; }
}
