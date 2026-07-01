using System.Text;
using System.Text.RegularExpressions;
using AurShell.Core.Types;

namespace AurShell.Core;

public class ShellEnvironment
{
    public ShellEnvironment? Parent { get; set; }
    
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);
    private readonly Stack<Dictionary<string, string>> _localScopes = new();
    
    // Phase 1: Rich Type System Storage
    private readonly Dictionary<string, AurValue> _values = new(StringComparer.Ordinal);
    private readonly Stack<Dictionary<string, AurValue>> _localValueScopes = new();
    
    private readonly Dictionary<string, Dictionary<string, string>> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ICommandNode> _functions = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<string>> _arrays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _assocArrays = new(StringComparer.Ordinal);
    private readonly HashSet<string> _readonlyVars = new(StringComparer.Ordinal);

    // Phase 6: Modularity
    public Dictionary<string, AurValue> Exports { get; } = new(StringComparer.Ordinal);

    private int _lastExitCode;

    // Phase 3: First-Class Returns
    public AurValue? LastReturnValue { get; set; }

    public Stack<StackFrame> CallStack { get; } = new();

    public int LastExitCode
    {
        get => _lastExitCode;
        set => _lastExitCode = value;
    }

    public int ShellPid => System.Environment.ProcessId;

    public int BackgroundPid { get; set; } = 0;

    private readonly Stack<List<string>> _positionalArgsStack = new();
    private readonly List<string> _globalPositionalArgs = new();

    public List<string> PositionalArguments => _positionalArgsStack.Count > 0 ? _positionalArgsStack.Peek() : _globalPositionalArgs;

    public void PushPositionalArguments(IEnumerable<string> args)
    {
        _positionalArgsStack.Push(new List<string>(args));
    }

    public void PopPositionalArguments()
    {
        if (_positionalArgsStack.Count > 0)
            _positionalArgsStack.Pop();
    }

    public void ShiftPositionalArguments(int count)
    {
        var args = PositionalArguments;
        if (count > args.Count) count = args.Count;
        if (count > 0) args.RemoveRange(0, count);
    }

    public bool StopOnError { get; set; } = false;

    public JobTable Jobs { get; } = new();

    public Plugins.PluginManager? PluginManager { get; set; }
    
    public DebuggerClient? Debugger { get; set; }

    public SuggestionProvider? Suggestions { get; set; }

    public FileAssociator Associator { get; } = new();

    public bool SshAvailable { get; set; }

    public Func<string, ShellEnvironment, string>? SubshellEvaluator { get; set; }
    public Func<string, ShellEnvironment, int>? ExitCodeSubshellEvaluator { get; set; }
    public Func<string, bool, ShellEnvironment, string>? ProcessSubstitutionEvaluator { get; set; }

    public IReadOnlyDictionary<string, string> Variables => _variables;
    public IReadOnlyDictionary<string, Dictionary<string, string>> Objects => _objects;
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    public void PushScope()
    {
        _localScopes.Push(new Dictionary<string, string>(StringComparer.Ordinal));
        _localValueScopes.Push(new Dictionary<string, AurValue>(StringComparer.Ordinal));
    }

    public void PopScope()
    {
        if (_localScopes.Count > 0)
            _localScopes.Pop();
        if (_localValueScopes.Count > 0)
            _localValueScopes.Pop();
    }

    public void PushFrame(StackFrame frame)
    {
        if (CallStack.Count > 1000)
            throw new Exception("aursh: max recursion depth exceeded");
        CallStack.Push(frame);
    }

    public void PopFrame()
    {
        if (CallStack.Count > 0)
            CallStack.Pop();
    }

    public void PrintCallStack()
    {
        if (CallStack.Count == 0) return;
        Console.Error.WriteLine("Call Stack (most recent call first):");
        foreach (var frame in CallStack)
        {
            Console.Error.WriteLine($"  {frame}");
        }
    }

    public bool IsReadonly(string name)
    {
        return _readonlyVars.Contains(name);
    }

    public void MarkReadonly(string name)
    {
        _readonlyVars.Add(name);
    }

    private bool CheckReadonly(string name)
    {
        if (IsReadonly(name))
        {
            Console.Error.WriteLine($"aursh: {name}: readonly variable");
            return true;
        }
        return false;
    }

    public void SetLocal(string name, string value)
    {
        if (CheckReadonly(name)) return;
        if (_localScopes.Count > 0)
        {
            _localScopes.Peek()[name] = value;
        }
        else
        {
            _variables[name] = value;
        }
    }

    public void ImportFromSystem()
    {
        var sysEnv = System.Environment.GetEnvironmentVariables();
        foreach (System.Collections.DictionaryEntry entry in sysEnv)
        {
            string key = entry.Key?.ToString() ?? "";
            string val = entry.Value?.ToString() ?? "";
            if (!string.IsNullOrEmpty(key))
                _variables[key] = val;
        }
    }

    public void Set(string name, string value)
    {
        if (CheckReadonly(name)) return;
        foreach (var scope in _localScopes)
        {
            if (scope.ContainsKey(name))
            {
                scope[name] = value;
                return;
            }
        }

        if (_variables.ContainsKey(name))
        {
            _variables[name] = value;
            return;
        }

        if (Parent != null && Parent.Get(name) != null)
        {
            Parent.Set(name, value);
            return;
        }

        _variables[name] = value;
    }

    public void SetLocalAurValue(string name, AurValue value)
    {
        if (CheckReadonly(name)) return;
        if (_localValueScopes.Count > 0)
        {
            _localValueScopes.Peek()[name] = value;
        }
        else
        {
            _values[name] = value;
        }
        // Backwards compatibility layer
        SetLocal(name, value?.ToString() ?? "");
    }

    public void SetAurValue(string name, AurValue value)
    {
        if (CheckReadonly(name)) return;
        foreach (var scope in _localValueScopes)
        {
            if (scope.ContainsKey(name))
            {
                scope[name] = value;
                Set(name, value?.ToString() ?? "");
                return;
            }
        }
        
        if (_values.ContainsKey(name))
        {
            _values[name] = value;
            Set(name, value?.ToString() ?? "");
            return;
        }

        if (Parent != null && Parent.GetAurValue(name) != null)
        {
            Parent.SetAurValue(name, value);
            return;
        }

        _values[name] = value;
        Set(name, value?.ToString() ?? "");
    }

    public AurValue? GetAurValue(string name)
    {
        foreach (var scope in _localValueScopes)
        {
            if (scope.TryGetValue(name, out AurValue? val))
                return val;
        }
        if (_values.TryGetValue(name, out AurValue? globalVal))
            return globalVal;
            
        if (Parent != null)
        {
            var pVal = Parent.GetAurValue(name);
            if (pVal != null) return pVal;
        }
            
        // Fallback to string env
        var strVal = Get(name);
        return strVal != null ? new AurString(strVal) : null;
    }

    public string? Get(string name)
    {
        if (name.EndsWith("]"))
        {
            int openBracket = name.IndexOf('[');
            if (openBracket > 0)
            {
                string arrName = name.Substring(0, openBracket);
                string key = name.Substring(openBracket + 1, name.Length - openBracket - 2);

                if (key == "@" || key == "*")
                {
                    if (_arrays.TryGetValue(arrName, out var a)) return string.Join(" ", a);
                    if (_assocArrays.TryGetValue(arrName, out var m)) return string.Join(" ", m.Values);
                    return null;
                }

                if (_assocArrays.TryGetValue(arrName, out var dict))
                {
                    return dict.TryGetValue(key, out string? val) ? val : null;
                }

                if (_arrays.TryGetValue(arrName, out var list))
                {
                    if (int.TryParse(key, out int idx) && idx >= 0 && idx < list.Count)
                        return list[idx];
                    return null;
                }
                return null;
            }
        }

        foreach (var scope in _localScopes)
        {
            if (scope.TryGetValue(name, out string? val))
                return val;
        }

        if (name == "@" || name == "*")
            return string.Join(" ", PositionalArguments);

        if (name == "#")
            return PositionalArguments.Count.ToString();

        if (int.TryParse(name, out int index))
        {
            if (index > 0 && index <= PositionalArguments.Count)
                return PositionalArguments[index - 1];
            if (index == 0) return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "aursh";
            return "";
        }

        if (_variables.TryGetValue(name, out string? globalVal))
            return globalVal;

        if (Parent != null)
        {
            var pVal = Parent.Get(name);
            if (pVal != null) return pVal;
        }

        return System.Environment.GetEnvironmentVariable(name);
    }

    public bool Unset(string name)
    {
        if (CheckReadonly(name)) return false;
        bool removed = false;
        foreach (var scope in _localScopes)
        {
            if (scope.Remove(name))
                removed = true;
        }

        if (_variables.Remove(name))
            removed = true;

        _objects.Remove(name);
        _arrays.Remove(name);
        _assocArrays.Remove(name);
        return removed;
    }

    public void SetArray(string name, List<string> values)
    {
        if (CheckReadonly(name)) return;
        _arrays[name] = new List<string>(values);
    }

    public List<string>? GetArray(string name)
    {
        if (_arrays.TryGetValue(name, out var arr)) return arr;
        if (Parent != null) return Parent.GetArray(name);
        return null;
    }

    public void SetArrayElement(string name, int index, string value)
    {
        if (CheckReadonly(name)) return;
        if (!_arrays.TryGetValue(name, out var arr))
        {
            if (Parent != null && Parent.GetArray(name) != null)
            {
                Parent.SetArrayElement(name, index, value);
                return;
            }
            arr = new List<string>();
            _arrays[name] = arr;
        }
        while (arr.Count <= index)
            arr.Add("");
        arr[index] = value;
    }

    public string? GetArrayElement(string name, int index)
    {
        if (_arrays.TryGetValue(name, out var arr) && index >= 0 && index < arr.Count)
            return arr[index];
        if (Parent != null) return Parent.GetArrayElement(name, index);
        return null;
    }

    public int GetArrayLength(string name)
    {
        if (_arrays.TryGetValue(name, out var arr))
            return arr.Count;
        if (Parent != null) return Parent.GetArrayLength(name);
        return 0;
    }

    public void SetAssocArray(string name, Dictionary<string, string> values)
    {
        if (CheckReadonly(name)) return;
        if (_assocArrays.ContainsKey(name)) { _assocArrays[name] = new Dictionary<string, string>(values, StringComparer.Ordinal); return; }
        if (Parent != null && Parent.GetAssocArray(name) != null) { Parent.SetAssocArray(name, values); return; }
        _assocArrays[name] = new Dictionary<string, string>(values, StringComparer.Ordinal);
    }

    public Dictionary<string, string>? GetAssocArray(string name)
    {
        if (_assocArrays.TryGetValue(name, out var dict)) return dict;
        if (Parent != null) return Parent.GetAssocArray(name);
        return null;
    }

    public void SetAssocElement(string name, string key, string value)
    {
        if (CheckReadonly(name)) return;
        if (!_assocArrays.TryGetValue(name, out var dict))
        {
            if (Parent != null && Parent.GetAssocArray(name) != null)
            {
                Parent.SetAssocElement(name, key, value);
                return;
            }
            dict = new Dictionary<string, string>(StringComparer.Ordinal);
            _assocArrays[name] = dict;
        }
        dict[key] = value;
    }

    public string? GetAssocElement(string name, string key)
    {
        if (_assocArrays.TryGetValue(name, out var dict) && dict.TryGetValue(key, out var val))
            return val;
        if (Parent != null) return Parent.GetAssocElement(name, key);
        return null;
    }

    public void ExportToSystem(string name)
    {
        if (_variables.TryGetValue(name, out string? val))
            System.Environment.SetEnvironmentVariable(name, val);
        else if (Parent != null)
            Parent.ExportToSystem(name);
    }

    public void SetObject(string name, Dictionary<string, string> obj)
    {
        if (_objects.ContainsKey(name))
        {
            _objects[name] = new Dictionary<string, string>(obj, StringComparer.Ordinal);
            _variables[name] = SerializeObject(obj);
            return;
        }
        if (Parent != null && Parent.GetObject(name) != null)
        {
            Parent.SetObject(name, obj);
            return;
        }
        _objects[name] = new Dictionary<string, string>(obj, StringComparer.Ordinal);
        _variables[name] = SerializeObject(obj);
    }

    public Dictionary<string, string>? GetObject(string name)
    {
        if (_objects.TryGetValue(name, out var obj)) return obj;
        if (Parent != null) return Parent.GetObject(name);
        return null;
    }

    public string? GetObjectField(string name, string field)
    {
        if (_objects.TryGetValue(name, out var obj) && obj.TryGetValue(field, out string? val))
            return val;
        if (Parent != null) return Parent.GetObjectField(name, field);
        return null;
    }

    public void SetAlias(string name, string command)
    {
        _aliases[name] = command;
    }

    public string? GetAlias(string name)
    {
        if (_aliases.TryGetValue(name, out string? val)) return val;
        if (Parent != null) return Parent.GetAlias(name);
        return null;
    }

    public bool UnsetAlias(string name)
    {
        return _aliases.Remove(name);
    }

    public void SetFunction(string name, ICommandNode body)
    {
        _functions[name] = body;
    }

    public ICommandNode? GetFunction(string name)
    {
        if (_functions.TryGetValue(name, out var body))
            return body;
            
        if (Parent != null)
            return Parent.GetFunction(name);
            
        return null;
    }

    public string Expand(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        if (!input.Contains('$') && !input.StartsWith('~'))
            return input;

        if (input == "~" || input.StartsWith("~/") || input.StartsWith("~\\"))
            input = Utils.Platform.ExpandTilde(input);

        var sb = new StringBuilder(input.Length * 2);
        int i = 0;

        while (i < input.Length)
        {
            if (input[i] == '\\' && i + 1 < input.Length && input[i + 1] == '$')
            {
                sb.Append('$');
                i += 2;
                continue;
            }

            if (input[i] == '`')
            {
                i++;
                int start = i;
                while (i < input.Length && input[i] != '`') i++;
                string cmd = input.Substring(start, i - start);
                if (i < input.Length) i++;
                sb.Append(SubshellEvaluator?.Invoke(cmd, this) ?? "");
                continue;
            }

            if (input[i] == '$')
            {
                string expanded = ExpandVariable(input, ref i);
                sb.Append(expanded);
                continue;
            }

            sb.Append(input[i]);
            i++;
        }

        return sb.ToString();
    }

    public string ExpandDoubleQuoted(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        if (!input.Contains('$'))
            return input;

        var sb = new StringBuilder(input.Length * 2);
        int i = 0;

        while (i < input.Length)
        {
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                char next = input[i + 1];
                if (next == '$' || next == '"' || next == '\\' || next == '`')
                {
                    sb.Append(next);
                    i += 2;
                    continue;
                }
            }

            if (input[i] == '`')
            {
                i++;
                int start = i;
                while (i < input.Length && input[i] != '`') i++;
                string cmd = input.Substring(start, i - start);
                if (i < input.Length) i++;
                sb.Append(SubshellEvaluator?.Invoke(cmd, this) ?? "");
                continue;
            }

            if (input[i] == '$')
            {
                string expanded = ExpandVariable(input, ref i);
                sb.Append(expanded);
                continue;
            }

            sb.Append(input[i]);
            i++;
        }

        return sb.ToString();
    }

    public Dictionary<string, string>? ParseObjectLiteral(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('{') || !text.EndsWith('}'))
            return null;

        string inner = text.Substring(1, text.Length - 2).Trim();
        if (string.IsNullOrEmpty(inner))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var pairs = SplitObjectPairs(inner);

        foreach (string pair in pairs)
        {
            string trimmed = pair.Trim();
            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0)
                return null;

            string key = trimmed.Substring(0, colonIdx).Trim();
            string val = trimmed.Substring(colonIdx + 1).Trim();

            if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                val = val.Substring(1, val.Length - 2);

            result[key] = Expand(val);
        }

        return result;
    }

    public ShellEnvironment Clone()
    {
        var clone = new ShellEnvironment();
        clone.Parent = this;
        clone._lastExitCode = _lastExitCode;
        clone.SubshellEvaluator = SubshellEvaluator;
        clone.ProcessSubstitutionEvaluator = ProcessSubstitutionEvaluator;
        clone.Suggestions = Suggestions;
        clone.Debugger = Debugger;
        return clone;
    }

    private string ExpandVariable(string input, ref int i)
    {
        i++;

        if (i >= input.Length)
            return "$";

        if (input[i] == '?')
        {
            i++;
            if (i < input.Length && input[i] == '(')
            {
                i++;
                int start = i;
                int depth = 1;
                while (i < input.Length && depth > 0)
                {
                    if (input[i] == '(') depth++;
                    else if (input[i] == ')') depth--;
                    if (depth > 0) i++;
                }
                if (depth == 0)
                {
                    string cmd = input.Substring(start, i - start - 1);
                    i++; // skip last )
                    int exitCode = ExitCodeSubshellEvaluator?.Invoke(cmd, this) ?? 0;
                    return exitCode.ToString();
                }
                // Unbalanced, fallback
                i = start - 1;
            }
            return _lastExitCode.ToString();
        }

        if (input[i] == '$')
        {
            i++;
            return ShellPid.ToString();
        }

        if (input[i] == '!')
        {
            i++;
            return BackgroundPid.ToString();
        }

        if (input[i] == '{')
            return ExpandBracedVariable(input, ref i);

        if (input[i] == '(' && i + 1 < input.Length && input[i + 1] == '(')
        {
            i += 2;
            int start = i;
            int depth = 2;
            while (i < input.Length && depth > 0)
            {
                if (input[i] == '(') depth++;
                else if (input[i] == ')') depth--;
                if (depth > 0) i++;
            }
            if (depth == 0)
            {
                string mathExpr = input.Substring(start, i - start - 1);
                i++; // skip last )
                return MathEvaluator.Evaluate(mathExpr, this).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            // If unbalanced, fallback
            i = start - 2;
        }

        if (input[i] == '(')
        {
            i++;
            int start = i;
            int depth = 1;
            while (i < input.Length && depth > 0)
            {
                if (input[i] == '(') depth++;
                else if (input[i] == ')') depth--;
                if (depth > 0) i++;
            }
            if (depth == 0)
            {
                string cmd = input.Substring(start, i - start - 1);
                i++; // skip last )
                return SubshellEvaluator?.Invoke(cmd, this) ?? "";
            }
            i = start - 1;
        }

        var nameBuf = new StringBuilder();

        while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
        {
            nameBuf.Append(input[i]);
            i++;
        }

        if (i < input.Length && input[i] == '.' && nameBuf.Length > 0)
        {
            string objName = nameBuf.ToString();
            if (_objects.ContainsKey(objName))
            {
                i++;
                var fieldBuf = new StringBuilder();
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                {
                    fieldBuf.Append(input[i]);
                    i++;
                }
                string? fieldVal = GetObjectField(objName, fieldBuf.ToString());
                return fieldVal ?? "";
            }
        }

        string name = nameBuf.ToString();
        if (string.IsNullOrEmpty(name))
            return "$";

        return Get(name) ?? "";
    }

    private string ExpandBracedVariable(string input, ref int i)
    {
        i++;
        int start = i;
        int depth = 1;

        while (i < input.Length && depth > 0)
        {
            if (input[i] == '{') depth++;
            else if (input[i] == '}') depth--;
            if (depth > 0) i++;
        }

        if (depth != 0)
            return "${";

        string content = input.Substring(start, i - start);
        i++;

        if (content.StartsWith("#"))
        {
            string varName = content.Substring(1);
            if (varName.EndsWith("[@]") || varName.EndsWith("[*]"))
            {
                string arrName = varName.Substring(0, varName.Length - 3);
                if (_arrays.TryGetValue(arrName, out var a)) return a.Count.ToString();
                if (_assocArrays.TryGetValue(arrName, out var m)) return m.Count.ToString();
                return "0";
            }
            string? v = Get(varName);
            return v != null ? v.Length.ToString() : "0";
        }

        int colonDash = content.IndexOf(":-", StringComparison.Ordinal);
        if (colonDash >= 0)
        {
            string name = content.Substring(0, colonDash);
            string def = content.Substring(colonDash + 2);
            string? val = Get(name);
            return string.IsNullOrEmpty(val) ? Expand(def) : val;
        }

        int colonEquals = content.IndexOf(":=", StringComparison.Ordinal);
        if (colonEquals >= 0)
        {
            string name = content.Substring(0, colonEquals);
            string def = content.Substring(colonEquals + 2);
            string? val = Get(name);
            if (string.IsNullOrEmpty(val))
            {
                val = Expand(def);
                Set(name, val);
            }
            return val;
        }

        int colonPlus = content.IndexOf(":+", StringComparison.Ordinal);
        if (colonPlus >= 0)
        {
            string name = content.Substring(0, colonPlus);
            string alt = content.Substring(colonPlus + 2);
            string? val = Get(name);
            return !string.IsNullOrEmpty(val) ? Expand(alt) : "";
        }

        int colonQuestion = content.IndexOf(":?", StringComparison.Ordinal);
        if (colonQuestion >= 0)
        {
            string name = content.Substring(0, colonQuestion);
            string err = content.Substring(colonQuestion + 2);
            string? val = Get(name);
            if (string.IsNullOrEmpty(val))
            {
                Console.Error.WriteLine($"aursh: {name}: {(string.IsNullOrEmpty(err) ? "parameter not set" : err)}");
                return "";
            }
            return val;
        }

        int dotIdx = content.IndexOf('.');
        if (dotIdx > 0)
        {
            string objName = content.Substring(0, dotIdx);
            string field = content.Substring(dotIdx + 1);
            string? fieldVal = GetObjectField(objName, field);
            return fieldVal ?? "";
        }

        return Get(content) ?? "";
    }

    private static List<string> SplitObjectPairs(string inner)
    {
        var pairs = new List<string>();
        var current = new StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                current.Append(c);
            }
            else if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                current.Append(c);
            }
            else if (c == ',' && !inSingleQuote && !inDoubleQuote)
            {
                pairs.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            pairs.Add(current.ToString());

        return pairs;
    }

    private static string SerializeObject(Dictionary<string, string> obj)
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in obj)
        {
            if (!first) sb.Append(", ");
            sb.Append(kv.Key).Append(':').Append(kv.Value);
            first = false;
        }
        sb.Append('}');
        return sb.ToString();
    }
}
