using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AurShell.Core.Types;

namespace AurShell.Core;

public static class AurValueParser
{
    public static AurValue Parse(string input)
    {
        input = input.Trim();
        if (string.IsNullOrEmpty(input))
            return new AurString("");

        if (input == "true") return new AurBool(true);
        if (input == "false") return new AurBool(false);
        if (input == "null") return new AurNull();

        if (long.TryParse(input, out long lval)) return new AurInt(lval);
        if (double.TryParse(input, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dval)) return new AurFloat(dval);

        if (input.StartsWith("[") && input.EndsWith("]"))
        {
            return ParseList(input);
        }

        if (input.StartsWith("{") && input.EndsWith("}"))
        {
            return ParseObject(input);
        }

        // If quoted string
        if ((input.StartsWith("\"") && input.EndsWith("\"")) || (input.StartsWith("'") && input.EndsWith("'")))
        {
            return new AurString(input.Substring(1, input.Length - 2));
        }

        return new AurString(input);
    }

    private static AurList ParseList(string input)
    {
        var list = new AurList();
        string content = input.Substring(1, input.Length - 2).Trim();
        if (string.IsNullOrEmpty(content)) return list;

        var tokens = TokenizeSequence(content);
        foreach (var token in tokens)
        {
            list.Values.Add(Parse(token));
        }
        return list;
    }

    private static AurObject ParseObject(string input)
    {
        var obj = new AurObject();
        string content = input.Substring(1, input.Length - 2).Trim();
        if (string.IsNullOrEmpty(content)) return obj;

        var tokens = TokenizeSequence(content);
        foreach (var token in tokens)
        {
            int colonIdx = token.IndexOf(':');
            if (colonIdx > 0)
            {
                string key = token.Substring(0, colonIdx).Trim();
                if ((key.StartsWith("\"") && key.EndsWith("\"")) || (key.StartsWith("'") && key.EndsWith("'")))
                {
                    key = key.Substring(1, key.Length - 2);
                }
                string valStr = token.Substring(colonIdx + 1).Trim();
                obj.Properties[key] = Parse(valStr);
            }
        }
        return obj;
    }

    private static List<string> TokenizeSequence(string input)
    {
        var tokens = new List<string>();
        int bracketDepth = 0;
        int braceDepth = 0;
        bool inDoubleQuotes = false;
        bool inSingleQuotes = false;
        
        int start = 0;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"' && !inSingleQuotes) inDoubleQuotes = !inDoubleQuotes;
            else if (c == '\'' && !inDoubleQuotes) inSingleQuotes = !inSingleQuotes;
            else if (!inDoubleQuotes && !inSingleQuotes)
            {
                if (c == '[') bracketDepth++;
                else if (c == ']') bracketDepth--;
                else if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == ',' && bracketDepth == 0 && braceDepth == 0)
                {
                    tokens.Add(input.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
        }
        if (start < input.Length)
        {
            tokens.Add(input.Substring(start).Trim());
        }
        return tokens;
    }
}
