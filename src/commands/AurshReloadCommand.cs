using System;
using AurShell.Core;

namespace AurShell.Commands;

public static class AurshReloadCommand
{
    public static int Execute(ShellEnvironment env)
    {
        Console.WriteLine("Reloading plugins...");
        if (env.PluginManager != null)
        {
            env.PluginManager.UnloadAll();
            env.PluginManager.LoadAll();
        }
        Console.WriteLine("Reload complete.");
        return 0;
    }
}
