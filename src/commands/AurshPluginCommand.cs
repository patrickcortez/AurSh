using System;
using System.IO;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshPluginCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.WriteLine("Usage: aursh-plugin <list|add|del|init|debug|update|unload> [args]");
            Console.WriteLine("  list                      List installed plugins");
            Console.WriteLine("  add <path>                Install plugin from directory");
            Console.WriteLine("  del <name>                Remove a plugin");
            Console.WriteLine("  init <name> [--type lua|fsharp]  Create a new plugin template");
            Console.WriteLine("  debug <file>              Check script for syntax errors");
            Console.WriteLine("  update <name>             Unload and reload a plugin");
            Console.WriteLine("  unload <name>             Unload a plugin from memory");
            Console.WriteLine("  unload -d <name>          Unload, delete directory, and reload");
            return 0;
        }

        string action = cmd.Args[0].ToLowerInvariant();
        var pm = env.PluginManager;
        if (pm == null)
        {
            Console.Error.WriteLine("aursh: plugin system not initialized");
            return 1;
        }

        switch (action)
        {
            case "list":
                var plugins = pm.Plugins;
                if (plugins.Count == 0)
                {
                    Console.WriteLine("No plugins installed.");
                    Console.WriteLine($"Plugin directory: {pm.PluginsDirectory}");
                    return 0;
                }
                foreach (var p in plugins)
                {
                    string cmds = p.RegisteredCommands.Count > 0
                        ? string.Join(", ", p.RegisteredCommands.Keys)
                        : (p.Manifest.Commands.Count > 0
                            ? string.Join(", ", p.Manifest.Commands)
                            : "(none)");
                    Console.WriteLine($"  {p.Manifest.Name} v{p.Manifest.Version} by {p.Manifest.Author}");
                    Console.WriteLine($"    Type: {p.Manifest.Type}");
                    Console.WriteLine($"    {p.Manifest.Description}");
                    Console.WriteLine($"    Commands: {cmds}");
                }
                Console.WriteLine($"\nPlugin directory: {pm.PluginsDirectory}");
                return 0;

            case "add":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin add <path>"); return 1; }
                return pm.InstallPlugin(cmd.Args[1]);

            case "del":
            case "remove":
            case "rm":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin del <name>"); return 1; }
                return pm.RemovePlugin(cmd.Args[1]);

            case "init":
            case "create":
            case "new":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin init <name> [--type lua|fsharp]"); return 1; }
                string pluginType = "lua";
                for (int i = 2; i < cmd.Args.Count; i++)
                {
                    if (cmd.Args[i] == "--type" && i + 1 < cmd.Args.Count)
                    {
                        pluginType = cmd.Args[i + 1];
                        break;
                    }
                }
                return pm.InitPlugin(cmd.Args[1], workingDirectory, pluginType);

            case "debug":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin debug <file_or_plugin>"); return 1; }
                return pm.DebugPlugin(cmd.Args[1], workingDirectory);

            case "update":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin update <name>"); return 1; }
                return pm.UpdatePlugin(cmd.Args[1]);

            case "unload":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin unload <name>"); return 1; }
                if (cmd.Args[1] == "-d" && cmd.Args.Count < 3) { Console.Error.WriteLine("aursh: aursh-plugin unload -d <name>"); return 1; }
                {
                    bool deleteDir = cmd.Args[1] == "-d";
                    string name = deleteDir ? cmd.Args[2] : cmd.Args[1];
                    if (pm.UnloadPlugin(name))
                    {
                        if (deleteDir)
                        {
                            string pluginDir = Path.Combine(pm.PluginsDirectory, name);
                            if (Directory.Exists(pluginDir))
                            {
                                try
                                {
                                    Directory.Delete(pluginDir, true);
                                    Console.WriteLine($"Unloaded and deleted plugin '{name}'");
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine($"aursh: failed to delete plugin '{name}': {ex.Message}");
                                    return 1;
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Unloaded plugin '{name}'");
                            }
                            return pm.UpdatePlugin(name);
                        }
                        Console.WriteLine($"Unloaded plugin '{name}'");
                        return 0;
                    }
                    Console.Error.WriteLine($"aursh: plugin '{name}' not loaded");
                    return 1;
                }

            default:
                Console.Error.WriteLine($"aursh: aursh-plugin: unknown action '{action}'");
                return 1;
        }
    }
}
