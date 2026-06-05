using System.Text;
using System.Text.RegularExpressions;

namespace AurShell.Core;

public class ShellEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private int _lastExitCode;

    public int LastExitCode
    {
        get => _lastExitCode;
        set => _lastExitCode = value;
    }

    public int ShellPid => System.Environment.ProcessId;

    public JobTable Jobs { get; } = new();

    public Plugins.PluginManager? PluginManager { get; set; }

    public SuggestionProvider? Suggestions { get; set; }

    public FileAssociator Associator { get; } = new();

    public bool SshAvailable { get; set; }

    public IReadOnlyDictionary<string, string> Variables => _variables;
    public IReadOnlyDictionary<string, Dictionary<string, string>> Objects => _objects;
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

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
        _variables[name] = value;
    }

    public string? Get(string name)
    {
        if (_variables.TryGetValue(name, out string? val))
            return val;
        return System.Environment.GetEnvironmentVariable(name);
    }

    public bool Unset(string name)
    {
        bool removed = _variables.Remove(name);
        _objects.Remove(name);
        return removed;
    }

    public void ExportToSystem(string name)
    {
        if (_variables.TryGetValue(name, out string? val))
            System.Environment.SetEnvironmentVariable(name, val);
    }

    public void SetObject(string name, Dictionary<string, string> obj)
    {
        _objects[name] = new Dictionary<string, string>(obj, StringComparer.Ordinal);
        _variables[name] = SerializeObject(obj);
    }

    public Dictionary<string, string>? GetObject(string name)
    {
        return _objects.TryGetValue(name, out var obj) ? obj : null;
    }

    public string? GetObjectField(string name, string field)
    {
        if (_objects.TryGetValue(name, out var obj) && obj.TryGetValue(field, out string? val))
            return val;
        return null;
    }

    public void SetAlias(string name, string command)
    {
        _aliases[name] = command;
    }

    public string? GetAlias(string name)
    {
        return _aliases.TryGetValue(name, out string? val) ? val : null;
    }

    public bool UnsetAlias(string name)
    {
        return _aliases.Remove(name);
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
        foreach (var kv in _variables)
            clone._variables[kv.Key] = kv.Value;
        foreach (var kv in _objects)
            clone._objects[kv.Key] = new Dictionary<string, string>(kv.Value, StringComparer.Ordinal);
        foreach (var kv in _aliases)
            clone._aliases[kv.Key] = kv.Value;
        clone._lastExitCode = _lastExitCode;
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
            return _lastExitCode.ToString();
        }

        if (input[i] == '$')
        {
            i++;
            return ShellPid.ToString();
        }

        if (input[i] == '{')
            return ExpandBracedVariable(input, ref i);

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
