using System;
using System.IO;
using AurShell.Core;
using AurShell.Utils;
using System.Text;

namespace AurShell.Commands;

public static class AurshFallbackCommand
{
    public static int Execute(SimpleCommandNode cmd)
    {
        Console.Error.WriteLine($"aursh: {cmd.Name}: builtin not implemented");
        return 1;
    }

    private static int ParseIntCompare(string left, string right, Func<int, int, bool> predicate)
    {
        if (int.TryParse(left, out int l) && int.TryParse(right, out int r))
            return predicate(l, r) ? 0 : 1;
        return 2;
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


