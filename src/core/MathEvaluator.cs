using System;
using System.Collections.Generic;
using System.Globalization;

namespace AurShell.Core;

public static class MathEvaluator
{
    public static long Evaluate(string expression, ShellEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(expression)) return 0;

        env.PushFrame(new StackFrame("math evaluation", 0, 0, "", FrameType.Arithmetic));
        try
        {
            var tokens = Tokenize(expression);
            int pos = 0;
            return ParseTernary(tokens, ref pos, env);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: arithmetic syntax error: {ex.Message}");
            env.PrintCallStack();
            return 0; // return 0 gracefully on evaluation failure
        }
        finally
        {
            env.PopFrame();
        }
    }

    private static List<string> Tokenize(string expr)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < expr.Length)
        {
            char c = expr[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsDigit(c) || c == '.')
            {
                int start = i;
                while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.')) i++;
                tokens.Add(expr.Substring(start, i - start));
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                int start = i;
                i++;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_')) i++;
                tokens.Add(expr.Substring(start, i - start));
                continue;
            }

            if (i + 1 < expr.Length)
            {
                string two = expr.Substring(i, 2);
                if (two == "**" || two == "==" || two == "!=" || two == "<=" || two == ">=")
                {
                    tokens.Add(two);
                    i += 2;
                    continue;
                }
            }

            tokens.Add(c.ToString());
            i++;
        }
        return tokens;
    }

    private static long ParseTernary(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        long condition = ParseLogical(tokens, ref pos, env);
        if (pos < tokens.Count && tokens[pos] == "?")
        {
            pos++;
            long trueBranch = ParseTernary(tokens, ref pos, env);
            if (pos < tokens.Count && tokens[pos] == ":")
            {
                pos++;
                long falseBranch = ParseTernary(tokens, ref pos, env);
                return condition != 0 ? trueBranch : falseBranch;
            }
            return condition != 0 ? trueBranch : 0;
        }
        return condition;
    }

    private static long ParseLogical(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        return ParseComparison(tokens, ref pos, env);
    }

    private static long ParseComparison(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        long left = ParseAddSub(tokens, ref pos, env);
        while (pos < tokens.Count)
        {
            string op = tokens[pos];
            if (op == "==" || op == "!=" || op == "<" || op == "<=" || op == ">" || op == ">=")
            {
                pos++;
                long right = ParseAddSub(tokens, ref pos, env);
                bool res = false;
                if (op == "==") res = left == right;
                else if (op == "!=") res = left != right;
                else if (op == "<") res = left < right;
                else if (op == "<=") res = left <= right;
                else if (op == ">") res = left > right;
                else if (op == ">=") res = left >= right;
                left = res ? 1 : 0;
            }
            else break;
        }
        return left;
    }

    private static long ParseAddSub(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        long left = ParseMulDivMod(tokens, ref pos, env);
        while (pos < tokens.Count)
        {
            string op = tokens[pos];
            if (op == "+" || op == "-")
            {
                pos++;
                long right = ParseMulDivMod(tokens, ref pos, env);
                if (op == "+") left += right;
                else left -= right;
            }
            else break;
        }
        return left;
    }

    private static long ParseMulDivMod(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        long left = ParsePower(tokens, ref pos, env);
        while (pos < tokens.Count)
        {
            string op = tokens[pos];
            if (op == "*" || op == "/" || op == "%")
            {
                pos++;
                long right = ParsePower(tokens, ref pos, env);
                if (op == "*") left *= right;
                else if (op == "/") left = right != 0 ? left / right : 0;
                else if (op == "%") left = right != 0 ? left % right : 0;
            }
            else break;
        }
        return left;
    }

    private static long ParsePower(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        long left = ParsePrimary(tokens, ref pos, env);
        while (pos < tokens.Count)
        {
            if (tokens[pos] == "**")
            {
                pos++;
                long right = ParsePrimary(tokens, ref pos, env);
                left = (long)Math.Pow(left, right);
            }
            else break;
        }
        return left;
    }

    private static long ParsePrimary(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        if (pos >= tokens.Count) return 0;
        string token = tokens[pos];

        if (token == "-")
        {
            pos++;
            return -ParsePrimary(tokens, ref pos, env);
        }
        if (token == "+")
        {
            pos++;
            return ParsePrimary(tokens, ref pos, env);
        }
        if (token == "!")
        {
            pos++;
            return ParsePrimary(tokens, ref pos, env) == 0 ? 1 : 0;
        }

        if (token == "(")
        {
            pos++;
            long val = ParseTernary(tokens, ref pos, env);
            if (pos < tokens.Count && tokens[pos] == ")") pos++;
            return val;
        }

        pos++;
        if (long.TryParse(token, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long num))
            return num;

        string varName = token.StartsWith("$") ? token.Substring(1) : token;
        string envVal = env.Get(varName) ?? "";
        if (long.TryParse(envVal, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long envNum))
            return envNum;

        return 0;
    }
}

