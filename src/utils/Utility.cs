using System.Text;
using AurShell.Core;

namespace AurShell.Utils;

internal static class Utility
{
    public static string ResolveSubCommand(ShellEnvironment env, string workingDirectory, string input)
    {
        if (string.IsNullOrEmpty(input) || input.IndexOf("$(") < 0)
            return input;

        StringBuilder result = new StringBuilder();
        int i = 0;
        while (i < input.Length)
        {
            if (input[i] == '`')
            {
                int close = input.IndexOf('`', i + 1);
                if (close > i)
                {
                    string command = input.Substring(i + 1, close - i - 1);
                    Executor ex = new Executor(env, workingDirectory);
                    var (_, output) = ex.ExecuteCapture(command);
                    result.Append(output);
                    i = close + 1;
                    continue;
                }
            }

            if (input[i] == '$' && i + 1 < input.Length && input[i + 1] == '(')
            {
                if (i + 2 < input.Length && input[i + 2] == '(')
                {
                    int start = i + 3;
                    int depth = 2;
                    int j = start;
                    while (j < input.Length && depth > 0)
                    {
                        if (input[j] == '(') depth++;
                        else if (input[j] == ')') depth--;
                        if (depth > 0) j++;
                    }

                    if (depth == 0)
                    {
                        string mathExpr = input.Substring(start, j - start - 1);
                        string mathOutput = MathEvaluator.Evaluate(mathExpr, env).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        result.Append(mathOutput);
                        i = j + 1;
                        continue;
                    }
                }
                else
                {
                    int start = i + 2;
                    int depth = 1;
                    int j = start;
                    while (j < input.Length && depth > 0)
                    {
                        if (input[j] == '(') depth++;
                        else if (input[j] == ')') depth--;
                        if (depth > 0) j++;
                    }

                    if (depth == 0)
                    {
                        string command = input.Substring(start, j - start);
                        Executor ex = new Executor(env, workingDirectory);
                        var (_, output) = ex.ExecuteCapture(command);
                        result.Append(output);
                        i = j + 1;
                        continue;
                    }
                }
            }

            result.Append(input[i]);
            i++;
        }

        return result.ToString();
    }
}