using System;
using System.IO;
using AurShell.Core;
using AurShell.Utils;

namespace AurShell.Commands;

public static class AurshBlackBoxDemoCommand
{
    public static int Execute(SimpleCommandNode cmd)
    {
        string[] args = cmd.Args.ToArray();
        return AurShell.BlackBoxView.BlackBoxDemo.Run(args);
    }

}

