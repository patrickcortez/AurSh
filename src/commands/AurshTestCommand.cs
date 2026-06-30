using System;
using System.IO;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshTestCommand
{
    public static int Execute(SimpleCommandNode cmd)
    {
        var args = cmd.Args.ToList();
        if (cmd.Name == "[" && args.Count > 0 && args.Last() == "]")
        {
            args.RemoveAt(args.Count - 1);
        }
        else if (cmd.Name == "[[" && args.Count > 0 && args.Last() == "]]")
        {
            args.RemoveAt(args.Count - 1);
        }

        if (args.Count == 0)
            return 1;

        if (args.Count == 1)
            return string.IsNullOrEmpty(args[0]) ? 1 : 0;

        if (args.Count == 2)
        {
            string op = args[0];
            string operand = args[1];

            return op switch
            {
                "-z" => string.IsNullOrEmpty(operand) ? 0 : 1,
                "-n" => !string.IsNullOrEmpty(operand) ? 0 : 1,
                "-f" => File.Exists(operand) ? 0 : 1,
                "-d" => Directory.Exists(operand) ? 0 : 1,
                "-e" => (File.Exists(operand) || Directory.Exists(operand)) ? 0 : 1,
                "-r" => File.Exists(operand) ? 0 : 1,
                "-w" => File.Exists(operand) ? 0 : 1,
                "-x" => File.Exists(operand) ? 0 : 1,
                "-s" => (File.Exists(operand) && new FileInfo(operand).Length > 0) ? 0 : 1,
                "!" => string.IsNullOrEmpty(operand) ? 0 : 1,
                _ => 1
            };
        }

        if (args.Count == 3)
        {
            string left = args[0];
            string op = args[1];
            string right = args[2];

            return op switch
            {
                "=" or "==" => cmd.Name == "[[" ? (System.Text.RegularExpressions.Regex.IsMatch(left, WordExpander.GlobSegmentToRegex(right)) ? 0 : 1) : (left == right ? 0 : 1),
                "!=" => cmd.Name == "[[" ? (!System.Text.RegularExpressions.Regex.IsMatch(left, WordExpander.GlobSegmentToRegex(right)) ? 0 : 1) : (left != right ? 0 : 1),
                "-eq" => ParseIntCompare(left, right, (a, b) => a == b),
                "-ne" => ParseIntCompare(left, right, (a, b) => a != b),
                "-lt" => ParseIntCompare(left, right, (a, b) => a < b),
                "-le" => ParseIntCompare(left, right, (a, b) => a <= b),
                "-gt" => ParseIntCompare(left, right, (a, b) => a > b),
                "-ge" => ParseIntCompare(left, right, (a, b) => a >= b),
                _ => 1
            };
        }

        return 1;
    }

    private static int ParseIntCompare(string left, string right, Func<int, int, bool> predicate)
    {
        bool leftParsed = int.TryParse(left, out int l);
        bool rightParsed = int.TryParse(right, out int r);

        if (!leftParsed)
        {
            Console.Error.WriteLine($"aursh: [: {left}: integer expression expected");
        }
        else if (!rightParsed)
        {
            Console.Error.WriteLine($"aursh: [: {right}: integer expression expected");
        }

        if (leftParsed && rightParsed)
            return predicate(l, r) ? 0 : 1;

        return 2;
    }
}
