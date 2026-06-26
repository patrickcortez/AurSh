using System;
using AurShell.Core;
using AurShell.Parser;
using AurShell.Core;

namespace AurShell.Commands;

public static class AurshSshCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (!env.SshAvailable)
        {
            Console.Error.WriteLine("aursh: aursh-ssh: ssh is not installed");
            return 127;
        }

        return SshTui.Run(workingDirectory);
    }
}
