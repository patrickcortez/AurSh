using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using AurShell.Utils;

namespace AurShell.Core;

public static class BuiltinCommands
{

    public readonly static string Version = "3.0";
    public static readonly HashSet<string> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "export", "unset", "exit", "history", "echo",
        "pwd", "type", "alias", "unalias", "source", "set", "env",
        "true", "false", "shift", "read", "test", "return", "aursh-context",
        "jobs", "fg", "kill", "aursh-plugin", "aursh-assoc", "aursh-reload", "aursh-history","aursh-about","aursh-ls","aursh-cat", "aursh-update", "aursh-net", "aursh-view", "aursh-music", "aursh-ssh", "local", "declare", "readonly", "help", "grm", "[", "[[", "clear"
    };

    public static bool IsBuiltin(string name) => Builtins.Contains(name);

    public static void WriteOut(string text, bool newline = true)
    {
        var stream = AstEvaluator.OutStream;
        if (stream != null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text + (newline ? "\n" : ""));
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
        else
        {
            if (newline) Console.WriteLine(text);
            else Console.Write(text);
        }
    }

    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        return cmd.Name.ToLowerInvariant() switch
        {
            "clear" => AurShell.Commands.AurshClearCommand.Execute(),
            "cd" => AurShell.Commands.AurshCdCommand.Execute(cmd, env, ref workingDirectory),
            "export" => AurShell.Commands.AurshExportCommand.Execute(cmd, env),
            "local" => AurShell.Commands.AurshLocalCommand.Execute(cmd, env),
            "declare" => AurShell.Commands.AurshDeclareCommand.Execute(cmd, env),
            "readonly" => AurShell.Commands.AurshReadonlyCommand.Execute(cmd, env),
            "unset" => AurShell.Commands.AurshUnsetCommand.Execute(cmd, env),
            "exit" => AurShell.Commands.AurshExitCommand.Execute(cmd),
            "history" or "aursh-history" => AurShell.Commands.AurshHistoryCommand.Execute(cmd, env, workingDirectory),
            "echo" => AurShell.Commands.AurshEchoCommand.Execute(cmd),
            "pwd" => AurShell.Commands.AurshPwdCommand.Execute(workingDirectory),
            "type" => AurShell.Commands.AurshTypeCommand.Execute(cmd, env, workingDirectory),
            "alias" => AurShell.Commands.AurshAliasCommand.Execute(cmd, env),
            "unalias" => AurShell.Commands.AurshUnaliasCommand.Execute(cmd, env),
            "source" => AurShell.Commands.AurshSourceCommand.Execute(cmd, env, ref workingDirectory),
            "set" => AurShell.Commands.AurshSetCommand.Execute(cmd, env),
            "env" => AurShell.Commands.AurshEnvCommand.Execute(env),
            "true" => 0,
            "false" => 1,
            "read" => AurShell.Commands.AurshReadCommand.Execute(cmd, env),
            "test" or "[" or "[[" => AurShell.Commands.AurshTestCommand.Execute(cmd),
            "return" => AurShell.Commands.AurshReturnCommand.Execute(cmd, env),
            "jobs" => AurShell.Commands.AurshJobsCommand.Execute(cmd, env),
            "fg" => AurShell.Commands.AurshFgCommand.Execute(cmd, env),
            "kill" => AurShell.Commands.AurshKillCommand.Execute(cmd, env),
            "aursh-plugin" => AurShell.Commands.AurshPluginCommand.Execute(cmd, env, workingDirectory),
            "aursh-assoc" => AurShell.Commands.AurshAssocCommand.Execute(cmd, env),
            "aursh-reload" => AurShell.Commands.AurshReloadCommand.Execute(env),
            "aursh-about" => AurShell.Commands.AurshAboutCommand.Execute(cmd),
            "aursh-ls" => AurShell.Commands.AurshLsCommand.Execute(cmd, env, ref workingDirectory),
            "aursh-cat" => AurShell.Commands.AurshCatCommand.Execute(cmd, env, ref workingDirectory),
            "aursh-update" => AurShell.Commands.AurshUpdateCommand.Execute(cmd),
            "aursh-context" => AurShell.Commands.AurshContextCommand.Execute(cmd),
            "aursh-net" => AurshNetCommand.Execute(cmd, env, ref workingDirectory),
            "aursh-view" => AurShell.Commands.AurshViewCommand.Execute(cmd, env, workingDirectory),
            "aursh-music" => AurShell.Commands.AurshMusicCommand.Execute(cmd),
            "aursh-ssh" => AurShell.Commands.AurshSshCommand.Execute(cmd, env, workingDirectory),
            "help" => AurShell.Commands.AurshHelpCommand.Execute(),
            "grm" => AurShell.Grm.GrmController.Execute(cmd, env, ref workingDirectory),
            _ => AurShell.Commands.AurshFallbackCommand.Execute(cmd)
        };
    }

}
