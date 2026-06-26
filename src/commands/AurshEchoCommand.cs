using System.Linq;
using System.Text;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshEchoCommand
{
    public static int Execute(SimpleCommandNode cmd)
    {
        bool noNewline = false;
        bool interpretEscapes = false;
        int startIdx = 0;

        while (startIdx < cmd.Args.Count)
        {
            string arg = cmd.Args[startIdx];
            if (arg == "-n")
            {
                noNewline = true;
                startIdx++;
            }
            else if (arg == "-e")
            {
                interpretEscapes = true;
                startIdx++;
            }
            else if (arg == "-E")
            {
                interpretEscapes = false;
                startIdx++;
            }
            else
            {
                break;
            }
        }

        string output = string.Join(" ", cmd.Args.Skip(startIdx));

        if (interpretEscapes)
            output = InterpretEscapes(output);

        if (noNewline)
            Core.BuiltinCommands.WriteOut(output, false);
        else
            Core.BuiltinCommands.WriteOut(output, true);

        return 0;
    }

    private static string InterpretEscapes(string input)
    {
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                char next = input[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 'a': sb.Append('\a'); i++; break;
                    case 'b': sb.Append('\b'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case '0': sb.Append('\0'); i++; break;
                    case 'e': sb.Append('\x1b'); i++; break;
                    default: sb.Append('\\'); sb.Append(next); i++; break;
                }
            }
            else
            {
                sb.Append(input[i]);
            }
        }
        return sb.ToString();
    }
}
