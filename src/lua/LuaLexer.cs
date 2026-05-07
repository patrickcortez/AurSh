using System.Text;

namespace AurShell.Lua;

public enum TkType
{
    Number, String, Name,
    And, Break, Do, Else, Elseif, End, False, For, Function,
    If, In, Local, Nil, Not, Or, Return, Then, True, While,
    Plus, Minus, Star, Slash, Percent, Caret, Hash,
    Eq, Neq, Lt, Le, Gt, Ge,
    Assign, LParen, RParen, LBrace, RBrace, LBracket, RBracket,
    Semi, Colon, Comma, Dot, DotDot, Dots,
    Eof
}

public class Tk
{
    public TkType Type;
    public string Value;
    public int Line;
    public Tk(TkType type, string value, int line) { Type = type; Value = value; Line = line; }
}

public class LuaLexer
{
    private readonly string _src;
    private int _pos;
    private int _line = 1;

    private static readonly Dictionary<string, TkType> Keywords = new(StringComparer.Ordinal)
    {
        ["and"] = TkType.And, ["break"] = TkType.Break, ["do"] = TkType.Do,
        ["else"] = TkType.Else, ["elseif"] = TkType.Elseif, ["end"] = TkType.End,
        ["false"] = TkType.False, ["for"] = TkType.For, ["function"] = TkType.Function,
        ["if"] = TkType.If, ["in"] = TkType.In, ["local"] = TkType.Local,
        ["nil"] = TkType.Nil, ["not"] = TkType.Not, ["or"] = TkType.Or,
        ["return"] = TkType.Return, ["then"] = TkType.Then, ["true"] = TkType.True,
        ["while"] = TkType.While
    };

    public LuaLexer(string source) { _src = source; _pos = 0; }

    public List<Tk> Tokenize()
    {
        var tokens = new List<Tk>();
        while (true)
        {
            var tk = Next();
            tokens.Add(tk);
            if (tk.Type == TkType.Eof) break;
        }
        return tokens;
    }

    private Tk Next()
    {
        SkipWhitespaceAndComments();
        if (_pos >= _src.Length) return new Tk(TkType.Eof, "", _line);

        char c = _src[_pos];
        int ln = _line;

        if (c == '\n') { _line++; _pos++; return Next(); }

        if (char.IsDigit(c) || (c == '.' && _pos + 1 < _src.Length && char.IsDigit(_src[_pos + 1])))
            return ReadNumber(ln);

        if (c == '"' || c == '\'')
            return ReadString(c, ln);

        if (c == '[' && _pos + 1 < _src.Length && (_src[_pos + 1] == '[' || _src[_pos + 1] == '='))
        {
            int eqCount = 0;
            int probe = _pos + 1;
            while (probe < _src.Length && _src[probe] == '=') { eqCount++; probe++; }
            if (probe < _src.Length && _src[probe] == '[')
                return ReadLongString(eqCount, ln);
        }

        if (char.IsLetter(c) || c == '_')
            return ReadName(ln);

        _pos++;
        switch (c)
        {
            case '+': return new Tk(TkType.Plus, "+", ln);
            case '*': return new Tk(TkType.Star, "*", ln);
            case '/': return new Tk(TkType.Slash, "/", ln);
            case '%': return new Tk(TkType.Percent, "%", ln);
            case '^': return new Tk(TkType.Caret, "^", ln);
            case '#': return new Tk(TkType.Hash, "#", ln);
            case '(': return new Tk(TkType.LParen, "(", ln);
            case ')': return new Tk(TkType.RParen, ")", ln);
            case '{': return new Tk(TkType.LBrace, "{", ln);
            case '}': return new Tk(TkType.RBrace, "}", ln);
            case '[': return new Tk(TkType.LBracket, "[", ln);
            case ']': return new Tk(TkType.RBracket, "]", ln);
            case ';': return new Tk(TkType.Semi, ";", ln);
            case ':': return new Tk(TkType.Colon, ":", ln);
            case ',': return new Tk(TkType.Comma, ",", ln);
            case '-':
                return new Tk(TkType.Minus, "-", ln);
            case '.':
                if (_pos < _src.Length && _src[_pos] == '.')
                {
                    _pos++;
                    if (_pos < _src.Length && _src[_pos] == '.') { _pos++; return new Tk(TkType.Dots, "...", ln); }
                    return new Tk(TkType.DotDot, "..", ln);
                }
                return new Tk(TkType.Dot, ".", ln);
            case '=':
                if (_pos < _src.Length && _src[_pos] == '=') { _pos++; return new Tk(TkType.Eq, "==", ln); }
                return new Tk(TkType.Assign, "=", ln);
            case '~':
                if (_pos < _src.Length && _src[_pos] == '=') { _pos++; return new Tk(TkType.Neq, "~=", ln); }
                throw new LuaError($"unexpected character '~'", ln);
            case '<':
                if (_pos < _src.Length && _src[_pos] == '=') { _pos++; return new Tk(TkType.Le, "<=", ln); }
                return new Tk(TkType.Lt, "<", ln);
            case '>':
                if (_pos < _src.Length && _src[_pos] == '=') { _pos++; return new Tk(TkType.Ge, ">=", ln); }
                return new Tk(TkType.Gt, ">", ln);
        }

        throw new LuaError($"unexpected character '{c}'", ln);
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c == ' ' || c == '\t' || c == '\r') { _pos++; continue; }
            if (c == '\n') break;

            if (c == '-' && _pos + 1 < _src.Length && _src[_pos + 1] == '-')
            {
                _pos += 2;
                if (_pos < _src.Length && _src[_pos] == '[')
                {
                    int eqCount = 0;
                    int probe = _pos + 1;
                    while (probe < _src.Length && _src[probe] == '=') { eqCount++; probe++; }
                    if (probe < _src.Length && _src[probe] == '[')
                    {
                        _pos = probe + 1;
                        SkipLongStringContent(eqCount);
                        continue;
                    }
                }
                while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
                continue;
            }
            break;
        }
    }

    private Tk ReadNumber(int ln)
    {
        var sb = new StringBuilder();
        if (_pos + 1 < _src.Length && _src[_pos] == '0' && (_src[_pos + 1] == 'x' || _src[_pos + 1] == 'X'))
        {
            sb.Append(_src[_pos++]); sb.Append(_src[_pos++]);
            while (_pos < _src.Length && IsHexDigit(_src[_pos])) sb.Append(_src[_pos++]);
            return new Tk(TkType.Number, sb.ToString(), ln);
        }
        while (_pos < _src.Length && char.IsDigit(_src[_pos])) sb.Append(_src[_pos++]);
        if (_pos < _src.Length && _src[_pos] == '.')
        {
            sb.Append(_src[_pos++]);
            while (_pos < _src.Length && char.IsDigit(_src[_pos])) sb.Append(_src[_pos++]);
        }
        if (_pos < _src.Length && (_src[_pos] == 'e' || _src[_pos] == 'E'))
        {
            sb.Append(_src[_pos++]);
            if (_pos < _src.Length && (_src[_pos] == '+' || _src[_pos] == '-')) sb.Append(_src[_pos++]);
            while (_pos < _src.Length && char.IsDigit(_src[_pos])) sb.Append(_src[_pos++]);
        }
        return new Tk(TkType.Number, sb.ToString(), ln);
    }

    private Tk ReadString(char quote, int ln)
    {
        _pos++;
        var sb = new StringBuilder();
        while (_pos < _src.Length && _src[_pos] != quote)
        {
            if (_src[_pos] == '\\')
            {
                _pos++;
                if (_pos >= _src.Length) break;
                char esc = _src[_pos++];
                sb.Append(esc switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '\\' => '\\', '\'' => '\'', '"' => '"', 'a' => '\a', 'b' => '\b', '0' => '\0', _ => esc });
            }
            else
            {
                if (_src[_pos] == '\n') _line++;
                sb.Append(_src[_pos++]);
            }
        }
        if (_pos < _src.Length) _pos++;
        return new Tk(TkType.String, sb.ToString(), ln);
    }

    private Tk ReadLongString(int eqCount, int ln)
    {
        _pos += 2 + eqCount;
        var sb = new StringBuilder();
        if (_pos < _src.Length && _src[_pos] == '\n') { _pos++; _line++; }
        while (_pos < _src.Length)
        {
            if (_src[_pos] == ']')
            {
                int closeEq = 0;
                int probe = _pos + 1;
                while (probe < _src.Length && _src[probe] == '=') { closeEq++; probe++; }
                if (closeEq == eqCount && probe < _src.Length && _src[probe] == ']')
                {
                    _pos = probe + 1;
                    return new Tk(TkType.String, sb.ToString(), ln);
                }
            }
            if (_src[_pos] == '\n') _line++;
            sb.Append(_src[_pos++]);
        }
        throw new LuaError("unfinished long string", ln);
    }

    private void SkipLongStringContent(int eqCount)
    {
        while (_pos < _src.Length)
        {
            if (_src[_pos] == ']')
            {
                int closeEq = 0;
                int probe = _pos + 1;
                while (probe < _src.Length && _src[probe] == '=') { closeEq++; probe++; }
                if (closeEq == eqCount && probe < _src.Length && _src[probe] == ']')
                {
                    _pos = probe + 1;
                    return;
                }
            }
            if (_src[_pos] == '\n') _line++;
            _pos++;
        }
    }

    private Tk ReadName(int ln)
    {
        var sb = new StringBuilder();
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            sb.Append(_src[_pos++]);
        string name = sb.ToString();
        if (Keywords.TryGetValue(name, out var kw))
            return new Tk(kw, name, ln);
        return new Tk(TkType.Name, name, ln);
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
