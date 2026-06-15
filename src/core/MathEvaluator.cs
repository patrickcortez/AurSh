using System;
using System.Collections.Generic;
using System.Globalization;

namespace AurShell.Core;

public static class MathEvaluator
{
    public static double Evaluate(string expression, ShellEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(expression)) return 0;
        
        var tokens = Tokenize(expression);
        int pos = 0;
        return ParseTernary(tokens, ref pos, env);
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

    private static double ParseTernary(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        double condition = ParseLogical(tokens, ref pos, env);
        if (pos < tokens.Count && tokens[pos] == "?")
        {
            pos++;
            double trueBranch = ParseTernary(tokens, ref pos, env);
            if (pos < tokens.Count && tokens[pos] == ":")
            {
                pos++;
                double falseBranch = ParseTernary(tokens, ref pos, env);
                return condition != 0 ? trueBranch : falseBranch;
            }
            return condition != 0 ? trueBranch : 0;
        }
        return condition;
    }

    private static double ParseLogical(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        return ParseComparison(tokens, ref pos, env);
    }

    private static double ParseComparison(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        double left = ParseAddSub(tokens, ref pos, env);
        while (pos < tokens.Count)
        {
            string op = tokens[pos];
            if (op == "==" || op == "!=" || op == "<" || op == "<=" || op == ">" || op == ">=")
            {
                pos++;
                double right = ParseAddSub(tokens, ref pos, env);
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

    private static double ParseAddSub(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        double left = ParseMulDivMod(tokens, ref pos, env);
        while (pos < tokens.Count)
        {
            string op = tokens[pos];
            if (op == "+" || op == "-")
            {
                pos++;
                double right = ParseMulDivMod(tokens, ref pos, env);
                if (op == "+") left += right;
                else left -= right;
            }
            else break;
        }
        return left;
    }

    private static double ParseMulDivMod(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        double left = ParsePower(tokens, ref pos, env);
        while (pos < tokens.Count)
        {
            string op = tokens[pos];
            if (op == "*" || op == "/" || op == "%")
            {
                pos++;
                double right = ParsePower(tokens, ref pos, env);
                if (op == "*") left *= right;
                else if (op == "/") left = right != 0 ? left / right : 0;
                else if (op == "%") left = right != 0 ? left % right : 0;
            }
            else break;
        }
        return left;
    }

    private static double ParsePower(List<string> tokens, ref int pos, ShellEnvironment env)
    {
        double left = ParsePrimary(tokens, ref pos, env);
        while (pos < tokens.Count)
        {
            if (tokens[pos] == "**")
            {
                pos++;
                double right = ParsePrimary(tokens, ref pos, env);
                left = Math.Pow(left, right);
            }
            else break;
        }
        return left;
    }

    private static double ParsePrimary(List<string> tokens, ref int pos, ShellEnvironment env)
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
            double val = ParseTernary(tokens, ref pos, env);
            if (pos < tokens.Count && tokens[pos] == ")") pos++;
            return val;
        }

        pos++;
        if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
            return num;

        string varName = token.StartsWith("$") ? token.Substring(1) : token;
        string envVal = env.Get(varName) ?? "";
        if (double.TryParse(envVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double envNum))
            return envNum;

        return 0;
    }
}
