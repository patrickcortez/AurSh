using System.Diagnostics;

namespace AurShell.Core;

public static class Pipeline
{
    public static int Execute(PipelineNode pipeline, ShellEnvironment env, string workingDirectory)
    {
        if (pipeline.Commands.Count == 0)
            return 0;

        if (pipeline.Commands.Count == 1)
            return ExecuteSingle(pipeline.Commands[0], env, workingDirectory, pipeline.Background);

        return ExecutePiped(pipeline, env, workingDirectory);
    }

    private static int ExecuteSingle(CommandNode cmd, ShellEnvironment env, string workingDirectory, bool background)
    {
        if (BuiltinCommands.IsBuiltin(cmd.Name))
        {
            Stream? stdoutStream = null;
            Stream? stderrStream = null;
            Stream? stdinStream = null;
            TextWriter? originalOut = null;
            TextWriter? originalErr = null;
            TextReader? originalIn = null;

            try
            {
                foreach (var redir in cmd.Redirections)
                {
                    string target = Utils.FileSystem.ResolvePath(redir.Target, workingDirectory);
                    switch (redir.Type)
                    {
                        case RedirectType.Out:
                            stdoutStream = new FileStream(target, FileMode.Create, FileAccess.Write);
                            originalOut = Console.Out;
                            Console.SetOut(new StreamWriter(stdoutStream) { AutoFlush = true });
                            break;
                        case RedirectType.Append:
                            stdoutStream = new FileStream(target, FileMode.Append, FileAccess.Write);
                            originalOut = Console.Out;
                            Console.SetOut(new StreamWriter(stdoutStream) { AutoFlush = true });
                            break;
                        case RedirectType.In:
                            stdinStream = new FileStream(target, FileMode.Open, FileAccess.Read);
                            originalIn = Console.In;
                            Console.SetIn(new StreamReader(stdinStream));
                            break;
                        case RedirectType.Err:
                            stderrStream = new FileStream(target, FileMode.Create, FileAccess.Write);
                            originalErr = Console.Error;
                            Console.SetError(new StreamWriter(stderrStream) { AutoFlush = true });
                            break;
                        case RedirectType.ErrAppend:
                            stderrStream = new FileStream(target, FileMode.Append, FileAccess.Write);
                            originalErr = Console.Error;
                            Console.SetError(new StreamWriter(stderrStream) { AutoFlush = true });
                            break;
                        case RedirectType.ErrToOut:
                            originalErr = Console.Error;
                            Console.SetError(Console.Out);
                            break;
                    }
                }

                return BuiltinCommands.Execute(cmd, env, ref workingDirectory);
            }
            finally
            {
                if (originalOut != null) Console.SetOut(originalOut);
                if (originalErr != null) Console.SetError(originalErr);
                if (originalIn != null) Console.SetIn(originalIn);
                stdoutStream?.Dispose();
                stderrStream?.Dispose();
                stdinStream?.Dispose();
            }
        }

        return ExecuteExternal(cmd, env, workingDirectory, background);
    }

    private static int ExecuteExternal(CommandNode cmd, ShellEnvironment env, string workingDirectory, bool background)
    {
        string? executable = ResolveCommand(cmd.Name, workingDirectory);
        if (executable == null)
            return ExecuteViaShell(cmd, env, workingDirectory, background);

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        foreach (string arg in cmd.Args)
            psi.ArgumentList.Add(arg);

        foreach (var kv in env.Variables)
            psi.Environment[kv.Key] = kv.Value;

        FileStream? stdoutFile = null;
        FileStream? stderrFile = null;
        FileStream? stdinFile = null;
        bool redirectStdout = false;
        bool redirectStderr = false;
        bool redirectStdin = false;
        bool errToOut = false;

        foreach (var redir in cmd.Redirections)
        {
            string target = Utils.FileSystem.ResolvePath(redir.Target, workingDirectory);
            switch (redir.Type)
            {
                case RedirectType.Out:
                    stdoutFile = new FileStream(target, FileMode.Create, FileAccess.Write);
                    psi.RedirectStandardOutput = true;
                    redirectStdout = true;
                    break;
                case RedirectType.Append:
                    stdoutFile = new FileStream(target, FileMode.Append, FileAccess.Write);
                    psi.RedirectStandardOutput = true;
                    redirectStdout = true;
                    break;
                case RedirectType.In:
                    stdinFile = new FileStream(target, FileMode.Open, FileAccess.Read);
                    psi.RedirectStandardInput = true;
                    redirectStdin = true;
                    break;
                case RedirectType.Err:
                    stderrFile = new FileStream(target, FileMode.Create, FileAccess.Write);
                    psi.RedirectStandardError = true;
                    redirectStderr = true;
                    break;
                case RedirectType.ErrAppend:
                    stderrFile = new FileStream(target, FileMode.Append, FileAccess.Write);
                    psi.RedirectStandardError = true;
                    redirectStderr = true;
                    break;
                case RedirectType.ErrToOut:
                    psi.RedirectStandardError = true;
                    errToOut = true;
                    break;
            }
        }

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine($"aursh: {cmd.Name}: failed to start process");
                return 126;
            }

            if (redirectStdin && stdinFile != null)
            {
                stdinFile.CopyTo(process.StandardInput.BaseStream);
                process.StandardInput.Close();
            }

            if (background)
            {
                Console.WriteLine($"[bg] {process.Id}");
                return 0;
            }

            if (redirectStdout && stdoutFile != null)
                process.StandardOutput.BaseStream.CopyTo(stdoutFile);

            if (errToOut)
                process.StandardError.BaseStream.CopyTo(Console.OpenStandardOutput());
            else if (redirectStderr && stderrFile != null)
                process.StandardError.BaseStream.CopyTo(stderrFile);

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: {cmd.Name}: {ex.Message}");
            return 126;
        }
        finally
        {
            stdoutFile?.Dispose();
            stderrFile?.Dispose();
            stdinFile?.Dispose();
        }
    }

    private static int ExecutePiped(PipelineNode pipeline, ShellEnvironment env, string workingDirectory)
    {
        var commands = pipeline.Commands;
        int count = commands.Count;
        var processes = new Process?[count];
        var fileStreams = new List<FileStream>();

        try
        {
            for (int i = 0; i < count; i++)
            {
                var cmd = commands[i];
                bool isFirst = i == 0;
                bool isLast = i == count - 1;

                if (isFirst && BuiltinCommands.IsBuiltin(cmd.Name))
                {
                    var pipePsi = new ProcessStartInfo
                    {
                        FileName = Utils.Platform.DefaultShell,
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    var tempPipe = new System.IO.Pipes.AnonymousPipeServerStream(
                        System.IO.Pipes.PipeDirection.Out);

                    var builtinTask = System.Threading.Tasks.Task.Run(() =>
                    {
                        var writer = new StreamWriter(tempPipe) { AutoFlush = true };
                        var origOut = Console.Out;
                        Console.SetOut(writer);
                        BuiltinCommands.Execute(cmd, env, ref workingDirectory);
                        Console.SetOut(origOut);
                        writer.Flush();
                        tempPipe.DisposeLocalCopyOfClientHandle();
                        tempPipe.Close();
                    });

                    if (i + 1 < count)
                    {
                        var nextCmd = commands[i + 1];
                        string? nextExe = ResolveCommand(nextCmd.Name, workingDirectory);
                        if (nextExe == null)
                        {
                            Console.Error.WriteLine($"aursh: {nextCmd.Name}: command not found");
                            builtinTask.Wait();
                            return 127;
                        }

                        var nextPsi = CreateProcessStartInfo(nextExe, nextCmd, env, workingDirectory);
                        nextPsi.RedirectStandardInput = true;
                        ApplyRedirections(nextCmd, nextPsi, workingDirectory, fileStreams, i == count - 2);

                        var nextProc = Process.Start(nextPsi);
                        if (nextProc != null)
                        {
                            processes[i + 1] = nextProc;
                            var clientStream = new System.IO.Pipes.AnonymousPipeClientStream(
                                System.IO.Pipes.PipeDirection.In,
                                tempPipe.GetClientHandleAsString());
                            clientStream.CopyTo(nextProc.StandardInput.BaseStream);
                            nextProc.StandardInput.Close();
                        }

                        builtinTask.Wait();
                        i++;
                    }
                    else
                    {
                        builtinTask.Wait();
                    }

                    continue;
                }

                string? executable = ResolveCommand(cmd.Name, workingDirectory);
                if (executable == null)
                {
                    executable = Utils.Platform.DefaultShell;
                    var shellPsi = CreateShellDelegatedStartInfo(cmd, env, workingDirectory);

                    if (!isFirst)
                        shellPsi.RedirectStandardInput = true;

                    if (!isLast)
                        shellPsi.RedirectStandardOutput = true;

                    ApplyRedirections(cmd, shellPsi, workingDirectory, fileStreams, isLast);

                    processes[i] = Process.Start(shellPsi);
                    if (processes[i] == null)
                    {
                        Console.Error.WriteLine($"aursh: {cmd.Name}: command not found");
                        return 127;
                    }
                    continue;
                }

                var psi = CreateProcessStartInfo(executable, cmd, env, workingDirectory);

                if (!isFirst)
                    psi.RedirectStandardInput = true;

                if (!isLast)
                    psi.RedirectStandardOutput = true;

                ApplyRedirections(cmd, psi, workingDirectory, fileStreams, isLast);

                processes[i] = Process.Start(psi);
                if (processes[i] == null)
                {
                    Console.Error.WriteLine($"aursh: {cmd.Name}: failed to start process");
                    return 126;
                }
            }

            for (int i = 0; i < count - 1; i++)
            {
                if (processes[i] != null && processes[i + 1] != null)
                {
                    processes[i]!.StandardOutput.BaseStream.CopyTo(processes[i + 1]!.StandardInput.BaseStream);
                    processes[i + 1]!.StandardInput.Close();
                }
            }

            int lastExit = 0;
            for (int i = 0; i < count; i++)
            {
                if (processes[i] != null)
                {
                    processes[i]!.WaitForExit();
                    lastExit = processes[i]!.ExitCode;
                }
            }

            if (pipeline.Background)
            {
                var lastProc = processes[count - 1];
                if (lastProc != null)
                    Console.WriteLine($"[bg] {lastProc.Id}");
            }

            return lastExit;
        }
        finally
        {
            foreach (var p in processes)
                p?.Dispose();
            foreach (var fs in fileStreams)
                fs.Dispose();
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(
        string executable, CommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        foreach (string arg in cmd.Args)
            psi.ArgumentList.Add(arg);

        foreach (var kv in env.Variables)
            psi.Environment[kv.Key] = kv.Value;

        return psi;
    }

    private static void ApplyRedirections(
        CommandNode cmd, ProcessStartInfo psi, string workingDirectory, List<FileStream> streams, bool isLast)
    {
        foreach (var redir in cmd.Redirections)
        {
            string target = Utils.FileSystem.ResolvePath(redir.Target, workingDirectory);
            switch (redir.Type)
            {
                case RedirectType.Out:
                    psi.RedirectStandardOutput = true;
                    break;
                case RedirectType.Append:
                    psi.RedirectStandardOutput = true;
                    break;
                case RedirectType.In:
                    psi.RedirectStandardInput = true;
                    break;
                case RedirectType.Err:
                    psi.RedirectStandardError = true;
                    break;
                case RedirectType.ErrAppend:
                    psi.RedirectStandardError = true;
                    break;
                case RedirectType.ErrToOut:
                    psi.RedirectStandardError = true;
                    break;
            }
        }
    }

    public static string? ResolveCommand(string name, string workingDirectory)
    {
        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains('/'))
        {
            string full = Utils.FileSystem.ResolvePath(name, workingDirectory);
            return File.Exists(full) ? full : null;
        }

        return Utils.Platform.FindExecutableInPath(name);
    }

    private static int ExecuteViaShell(CommandNode cmd, ShellEnvironment env, string workingDirectory, bool background)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(cmd.Name);
        foreach (string arg in cmd.Args)
        {
            sb.Append(' ');
            if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\''))
            {
                sb.Append('"');
                sb.Append(arg.Replace("\"", "\\\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }

        string fullCommand = sb.ToString();

        var psi = new ProcessStartInfo
        {
            FileName = Utils.Platform.DefaultShell,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        psi.ArgumentList.Add(Utils.Platform.ShellFlag);
        psi.ArgumentList.Add(fullCommand);

        foreach (var kv in env.Variables)
            psi.Environment[kv.Key] = kv.Value;

        FileStream? stdoutFile = null;
        FileStream? stderrFile = null;
        FileStream? stdinFile = null;
        bool redirectStdout = false;
        bool redirectStderr = false;
        bool redirectStdin = false;
        bool errToOut = false;

        foreach (var redir in cmd.Redirections)
        {
            string target = Utils.FileSystem.ResolvePath(redir.Target, workingDirectory);
            switch (redir.Type)
            {
                case RedirectType.Out:
                    stdoutFile = new FileStream(target, FileMode.Create, FileAccess.Write);
                    psi.RedirectStandardOutput = true;
                    redirectStdout = true;
                    break;
                case RedirectType.Append:
                    stdoutFile = new FileStream(target, FileMode.Append, FileAccess.Write);
                    psi.RedirectStandardOutput = true;
                    redirectStdout = true;
                    break;
                case RedirectType.In:
                    stdinFile = new FileStream(target, FileMode.Open, FileAccess.Read);
                    psi.RedirectStandardInput = true;
                    redirectStdin = true;
                    break;
                case RedirectType.Err:
                    stderrFile = new FileStream(target, FileMode.Create, FileAccess.Write);
                    psi.RedirectStandardError = true;
                    redirectStderr = true;
                    break;
                case RedirectType.ErrAppend:
                    stderrFile = new FileStream(target, FileMode.Append, FileAccess.Write);
                    psi.RedirectStandardError = true;
                    redirectStderr = true;
                    break;
                case RedirectType.ErrToOut:
                    psi.RedirectStandardError = true;
                    errToOut = true;
                    break;
            }
        }

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine($"aursh: {cmd.Name}: command not found");
                return 127;
            }

            if (redirectStdin && stdinFile != null)
            {
                stdinFile.CopyTo(process.StandardInput.BaseStream);
                process.StandardInput.Close();
            }

            if (background)
            {
                Console.WriteLine($"[bg] {process.Id}");
                return 0;
            }

            if (redirectStdout && stdoutFile != null)
                process.StandardOutput.BaseStream.CopyTo(stdoutFile);

            if (errToOut)
                process.StandardError.BaseStream.CopyTo(Console.OpenStandardOutput());
            else if (redirectStderr && stderrFile != null)
                process.StandardError.BaseStream.CopyTo(stderrFile);

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: {cmd.Name}: {ex.Message}");
            return 127;
        }
        finally
        {
            stdoutFile?.Dispose();
            stderrFile?.Dispose();
            stdinFile?.Dispose();
        }
    }

    private static ProcessStartInfo CreateShellDelegatedStartInfo(
        CommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(cmd.Name);
        foreach (string arg in cmd.Args)
        {
            sb.Append(' ');
            if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\''))
            {
                sb.Append('"');
                sb.Append(arg.Replace("\"", "\\\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = Utils.Platform.DefaultShell,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        psi.ArgumentList.Add(Utils.Platform.ShellFlag);
        psi.ArgumentList.Add(sb.ToString());

        foreach (var kv in env.Variables)
            psi.Environment[kv.Key] = kv.Value;

        return psi;
    }
}
