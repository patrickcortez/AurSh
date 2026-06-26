using System;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshReadCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        string varName = cmd.Args.Count > 0 ? cmd.Args[0] : "REPLY";
        string prompt = "";

        for (int i = 0; i < cmd.Args.Count - 1; i++)
        {
            if (cmd.Args[i] == "-p" && i + 1 < cmd.Args.Count)
            {
                prompt = cmd.Args[i + 1];
                varName = cmd.Args.Count > i + 2 ? cmd.Args[i + 2] : "REPLY";
                break;
            }
        }

        if (!string.IsNullOrEmpty(prompt))
            Console.Write(prompt);

        string? line = Console.ReadLine();
        env.Set(varName, line ?? "");

        return line == null ? 1 : 0;
    }
}
