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
                            stdoutStream = SafeOpenFileStream(target, FileMode.Create, FileAccess.Write);
                            if (stdoutStream == null) return 1;
                            originalOut = Console.Out;
                            Console.SetOut(new StreamWriter(stdoutStream) { AutoFlush = true });
                            break;
                        case RedirectType.Append:
                            stdoutStream = SafeOpenFileStream(target, FileMode.Append, FileAccess.Write);
                            if (stdoutStream == null) return 1;
                            originalOut = Console.Out;
                            Console.SetOut(new StreamWriter(stdoutStream) { AutoFlush = true });
                            break;
                        case RedirectType.In:
                            stdinStream = SafeOpenFileStream(target, FileMode.Open, FileAccess.Read);
                            if (stdinStream == null) return 1;
                            originalIn = Console.In;
                            Console.SetIn(new StreamReader(stdinStream));
                            break;
                        case RedirectType.Err:
                            stderrStream = SafeOpenFileStream(target, FileMode.Create, FileAccess.Write);
                            if (stderrStream == null) return 1;
                            originalErr = Console.Error;
                            Console.SetError(new StreamWriter(stderrStream) { AutoFlush = true });
                            break;
                        case RedirectType.ErrAppend:
                            stderrStream = SafeOpenFileStream(target, FileMode.Append, FileAccess.Write);
                            if (stderrStream == null) return 1;
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

        if (env.PluginManager != null && env.PluginManager.IsPluginCommand(cmd.Name))
        {
            string? binPath = env.PluginManager.GetPluginBinaryPath(cmd.Name);
            if (binPath != null)
            {
                var tempCmd = new CommandNode { Name = binPath };
                foreach (var arg in cmd.Args) tempCmd.Args.Add(arg);
                foreach (var r in cmd.Redirections) tempCmd.Redirections.Add(r);
                return ExecuteExternal(tempCmd, env, workingDirectory, background);
            }
            return env.PluginManager.ExecutePluginCommand(cmd.Name, cmd.Args.ToList());
        }

        return ExecuteExternal(cmd, env, workingDirectory, background);
    }

    private static int ExecuteExternal(CommandNode cmd, ShellEnvironment env, string workingDirectory, bool background)
    {
        if (TryGetAssociationShellCommand(cmd, env, workingDirectory, out string assocCmd))
        {
            var tempCmd = new CommandNode { Name = assocCmd };
            foreach (var r in cmd.Redirections) tempCmd.Redirections.Add(r);
            return ExecuteViaShell(tempCmd, env, workingDirectory, background);
        }

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
                    stdoutFile = SafeOpenFileStream(target, FileMode.Create, FileAccess.Write);
                    if (stdoutFile == null) return 1;
                    psi.RedirectStandardOutput = true;
                    redirectStdout = true;
                    break;
                case RedirectType.Append:
                    stdoutFile = SafeOpenFileStream(target, FileMode.Append, FileAccess.Write);
                    if (stdoutFile == null) return 1;
                    psi.RedirectStandardOutput = true;
                    redirectStdout = true;
                    break;
                case RedirectType.In:
                    stdinFile = SafeOpenFileStream(target, FileMode.Open, FileAccess.Read);
                    if (stdinFile == null) return 1;
                    psi.RedirectStandardInput = true;
                    redirectStdin = true;
                    break;
                case RedirectType.Err:
                    stderrFile = SafeOpenFileStream(target, FileMode.Create, FileAccess.Write);
                    if (stderrFile == null) return 1;
                    psi.RedirectStandardError = true;
                    redirectStderr = true;
                    break;
                case RedirectType.ErrAppend:
                    stderrFile = SafeOpenFileStream(target, FileMode.Append, FileAccess.Write);
                    if (stderrFile == null) return 1;
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
                int jobId = env.Jobs.Add(process, cmd.Name + (cmd.Args.Count > 0 ? " " + string.Join(" ", cmd.Args) : ""));
                Console.WriteLine($"[{jobId}] {process.Id}");
                return 0;
            }

            var tasks = new List<System.Threading.Tasks.Task>();

            if (errToOut && stdoutFile != null)
            {
                var fileLock = new object();
                tasks.Add(PumpStreamAsync(process.StandardOutput.BaseStream, stdoutFile, fileLock));
                tasks.Add(PumpStreamAsync(process.StandardError.BaseStream, stdoutFile, fileLock));
            }
            else
            {
                if (redirectStdout && stdoutFile != null)
                {
                    tasks.Add(PumpStreamAsync(process.StandardOutput.BaseStream, stdoutFile));
                }

                if (errToOut && stdoutFile == null)
                {
                    tasks.Add(PumpStreamAsync(process.StandardError.BaseStream, Console.OpenStandardOutput()));
                }
                else if (redirectStderr && stderrFile != null)
                {
                    tasks.Add(PumpStreamAsync(process.StandardError.BaseStream, stderrFile));
                }
            }

            System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
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

        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows && count > 1)
        {
            bool allShellDelegated = true;
            for (int i = 0; i < count; i++)
            {
                var cmd = commands[i];
                if (BuiltinCommands.IsBuiltin(cmd.Name))
                {
                    allShellDelegated = false;
                    break;
                }
                if (env.PluginManager != null && env.PluginManager.IsPluginCommand(cmd.Name))
                {
                    allShellDelegated = false;
                    break;
                }
                if (TryGetAssociationShellCommand(cmd, env, workingDirectory, out _))
                {
                    allShellDelegated = false;
                    break;
                }
                string? exe = ResolveCommand(cmd.Name, workingDirectory);
                if (exe != null)
                {
                    string exeLower = exe.ToLowerInvariant();
                    bool isPowerShellExe = exeLower.EndsWith("powershell.exe") || exeLower.EndsWith("pwsh.exe");
                    if (!isPowerShellExe)
                    {
                        allShellDelegated = false;
                        break;
                    }
                }
            }

            if (allShellDelegated)
            {
                return ExecuteCoalescedPipeline(pipeline, env, workingDirectory);
            }
        }

        var processes = new Process?[count];
        var fileStreams = new List<FileStream>();
        var redirInfos = new (FileStream? stdout, FileStream? stderr, FileStream? stdin, bool errToOut)[count];

        async System.Threading.Tasks.Task PumpStreamResilientAsync(Stream source, Stream? destination, object? writeLock = null)
        {
            byte[] buffer = new byte[81920];
            int read;
            bool destAlive = destination != null;

            try
            {
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (destAlive)
                    {
                        try
                        {
                            if (writeLock != null)
                            {
                                lock (writeLock)
                                {
                                    destination!.Write(buffer, 0, read);
                                }
                                await destination!.FlushAsync();
                            }
                            else
                            {
                                await destination!.WriteAsync(buffer, 0, read);
                                await destination!.FlushAsync();
                            }
                        }
                        catch (Exception)
                        {
                            destAlive = false;
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        try
        {
            for (int i = 0; i < count; i++)
            {
                var cmd = commands[i];
                bool isFirst = i == 0;
                bool isLast = i == count - 1;

                if (isFirst && BuiltinCommands.IsBuiltin(cmd.Name))
                {
                    var tempPipe = new System.IO.Pipes.AnonymousPipeServerStream(
                        System.IO.Pipes.PipeDirection.Out);

                    System.IO.Pipes.AnonymousPipeClientStream? clientStream = null;
                    if (i + 1 < count)
                    {
                        clientStream = new System.IO.Pipes.AnonymousPipeClientStream(
                            System.IO.Pipes.PipeDirection.In,
                            tempPipe.GetClientHandleAsString());
                    }

                    var builtinTask = System.Threading.Tasks.Task.Run(() =>
                    {
                        StreamWriter? writer = null;
                        TextWriter? origOut = null;
                        TextWriter? origErr = null;
                        Stream? stdoutFileStream = null;
                        Stream? stderrFileStream = null;

                        try
                        {
                            foreach (var r in cmd.Redirections)
                            {
                                if (r.Type == RedirectType.Out || r.Type == RedirectType.Append)
                                {
                                    string target = Utils.FileSystem.ResolvePath(r.Target, workingDirectory);
                                    stdoutFileStream = SafeOpenFileStream(target,
                                        r.Type == RedirectType.Out ? FileMode.Create : FileMode.Append,
                                        FileAccess.Write);
                                    if (stdoutFileStream != null)
                                    {
                                        origOut = Console.Out;
                                        writer = new StreamWriter(stdoutFileStream) { AutoFlush = true };
                                        Console.SetOut(writer);
                                    }
                                    break;
                                }
                            }

                            foreach (var r in cmd.Redirections)
                            {
                                if (r.Type == RedirectType.Err || r.Type == RedirectType.ErrAppend || r.Type == RedirectType.ErrToOut)
                                {
                                    if (r.Type == RedirectType.ErrToOut)
                                    {
                                        origErr = Console.Error;
                                        Console.SetError(Console.Out);
                                    }
                                    else
                                    {
                                        string target = Utils.FileSystem.ResolvePath(r.Target, workingDirectory);
                                        stderrFileStream = SafeOpenFileStream(target,
                                            r.Type == RedirectType.Err ? FileMode.Create : FileMode.Append,
                                            FileAccess.Write);
                                        if (stderrFileStream != null)
                                        {
                                            origErr = Console.Error;
                                            Console.SetError(new StreamWriter(stderrFileStream) { AutoFlush = true });
                                        }
                                    }
                                    break;
                                }
                            }

                            if (origOut == null)
                            {
                                writer = new StreamWriter(tempPipe) { AutoFlush = true };
                                origOut = Console.Out;
                                Console.SetOut(writer);
                            }

                            BuiltinCommands.Execute(cmd, env, ref workingDirectory);
                        }
                        finally
                        {
                            if (origOut != null) Console.SetOut(origOut);
                            if (origErr != null) Console.SetError(origErr);
                            writer?.Flush();
                            stdoutFileStream?.Dispose();
                            stderrFileStream?.Dispose();
                            try { tempPipe.DisposeLocalCopyOfClientHandle(); } catch { }
                            tempPipe.Close();
                        }
                    });

                    if (i + 1 < count)
                    {
                        var nextCmd = commands[i + 1];
                        string? nextExe = ResolveCommand(nextCmd.Name, workingDirectory);
                        if (nextExe == null)
                        {
                            Console.Error.WriteLine($"aursh: {nextCmd.Name}: command not found");
                            clientStream?.Dispose();
                            builtinTask.Wait();
                            return 127;
                        }

                        var nextPsi = CreateProcessStartInfo(nextExe, nextCmd, env, workingDirectory);
                        nextPsi.RedirectStandardInput = true;
                        
                        if (i + 1 != count - 1)
                            nextPsi.RedirectStandardOutput = true;

                        redirInfos[i + 1] = ApplyRedirections(nextCmd, nextPsi, workingDirectory, fileStreams);

                        var nextProc = Process.Start(nextPsi);
                        if (nextProc != null)
                        {
                            processes[i + 1] = nextProc;
                            
                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                await PumpStreamResilientAsync(clientStream!, nextProc.StandardInput.BaseStream);
                                try { nextProc.StandardInput.Close(); } catch { }
                                clientStream!.Dispose();
                            });
                        }
                        else
                        {
                            clientStream?.Dispose();
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

                if (TryGetAssociationShellCommand(cmd, env, workingDirectory, out string assocCmd))
                {
                    var tempCmd = new CommandNode { Name = assocCmd };
                    foreach (var r in cmd.Redirections) tempCmd.Redirections.Add(r);
                    var assocPsi = CreateShellDelegatedStartInfo(tempCmd, env, workingDirectory);

                    if (!isFirst) assocPsi.RedirectStandardInput = true;
                    if (!isLast) assocPsi.RedirectStandardOutput = true;

                    redirInfos[i] = ApplyRedirections(cmd, assocPsi, workingDirectory, fileStreams);

                    processes[i] = Process.Start(assocPsi);
                    if (processes[i] == null)
                    {
                        Console.Error.WriteLine($"aursh: {cmd.Name}: failed to start associated process");
                        return 126;
                    }
                    continue;
                }

                string? executable = ResolveCommand(cmd.Name, workingDirectory);
                if (executable == null && env.PluginManager != null && env.PluginManager.IsPluginCommand(cmd.Name))
                {
                    executable = env.PluginManager.GetPluginBinaryPath(cmd.Name);
                }

                if (executable == null)
                {
                    executable = Utils.Platform.DefaultShell;
                    var shellPsi = CreateShellDelegatedStartInfo(cmd, env, workingDirectory);

                    if (!isFirst) shellPsi.RedirectStandardInput = true;
                    if (!isLast) shellPsi.RedirectStandardOutput = true;

                    redirInfos[i] = ApplyRedirections(cmd, shellPsi, workingDirectory, fileStreams);

                    processes[i] = Process.Start(shellPsi);
                    if (processes[i] == null)
                    {
                        Console.Error.WriteLine($"aursh: {cmd.Name}: command not found");
                        return 127;
                    }
                    continue;
                }

                var psi = CreateProcessStartInfo(executable, cmd, env, workingDirectory);

                if (!isFirst) psi.RedirectStandardInput = true;
                if (!isLast) psi.RedirectStandardOutput = true;

                redirInfos[i] = ApplyRedirections(cmd, psi, workingDirectory, fileStreams);

                processes[i] = Process.Start(psi);
                if (processes[i] == null)
                {
                    Console.Error.WriteLine($"aursh: {cmd.Name}: failed to start process");
                    return 126;
                }
            }

            var drainTasks = new List<System.Threading.Tasks.Task>();

            for (int i = 0; i < count; i++)
            {
                if (processes[i] == null) continue;
                var (stdoutFile, stderrFile, stdinFile, errToOut) = redirInfos[i];

                if (stdinFile != null)
                {
                    var proc = processes[i]!;
                    drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                    {
                        await PumpStreamResilientAsync(stdinFile, proc.StandardInput.BaseStream);
                        try { proc.StandardInput.Close(); } catch { }
                    }));
                }

                if (i < count - 1 && processes[i + 1] != null)
                {
                    var proc = processes[i]!;
                    var nextProc = processes[i + 1]!;

                    if (stdoutFile != null)
                    {
                        drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                        {
                            await PumpStreamResilientAsync(proc.StandardOutput.BaseStream, stdoutFile);
                            try { nextProc.StandardInput.Close(); } catch { }
                        }));
                    }
                    else
                    {
                        drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                        {
                            await PumpStreamResilientAsync(proc.StandardOutput.BaseStream, nextProc.StandardInput.BaseStream);
                            try { nextProc.StandardInput.Close(); } catch { }
                        }));
                    }

                    if (errToOut && stdoutFile == null)
                    {
                        drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                        {
                            await PumpStreamResilientAsync(proc.StandardError.BaseStream, nextProc.StandardInput.BaseStream);
                        }));
                    }
                    else if (stderrFile != null)
                    {
                        drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                        {
                            await PumpStreamResilientAsync(proc.StandardError.BaseStream, stderrFile);
                        }));
                    }
                }
                else
                {
                    if (errToOut && stdoutFile != null)
                    {
                        var proc = processes[i]!;
                        var fileLock = new object();
                        drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                        {
                            await PumpStreamResilientAsync(proc.StandardOutput.BaseStream, stdoutFile, fileLock);
                        }));
                        drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                        {
                            await PumpStreamResilientAsync(proc.StandardError.BaseStream, stdoutFile, fileLock);
                        }));
                    }
                    else
                    {
                        if (stdoutFile != null)
                        {
                            var proc = processes[i]!;
                            drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                            {
                                await PumpStreamResilientAsync(proc.StandardOutput.BaseStream, stdoutFile);
                            }));
                        }

                        if (errToOut && stdoutFile == null)
                        {
                            var proc = processes[i]!;
                            drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                            {
                                await PumpStreamResilientAsync(proc.StandardError.BaseStream, Console.OpenStandardOutput());
                            }));
                        }
                        else if (stderrFile != null)
                        {
                            var proc = processes[i]!;
                            drainTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                            {
                                await PumpStreamResilientAsync(proc.StandardError.BaseStream, stderrFile);
                            }));
                        }
                    }
                }
            }

            try
            {
                System.Threading.Tasks.Task.WaitAll(drainTasks.ToArray());
            }
            catch (AggregateException) { }

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
                {
                    string fullCmd = string.Join(" | ", commands.Select(c => c.Name));
                    int jobId = env.Jobs.Add(lastProc, fullCmd);
                    Console.WriteLine($"[{jobId}] {lastProc.Id}");
                }
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

    private static int ExecuteCoalescedPipeline(PipelineNode pipeline, ShellEnvironment env, string workingDirectory)
    {
        var fullSb = new System.Text.StringBuilder();

        for (int i = 0; i < pipeline.Commands.Count; i++)
        {
            var cmd = pipeline.Commands[i];
            if (i > 0)
                fullSb.Append(" | ");

            fullSb.Append(cmd.Name);
            foreach (string arg in cmd.Args)
            {
                fullSb.Append(' ');
                if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\''))
                {
                    fullSb.Append('"');
                    fullSb.Append(arg.Replace("\"", "\\\""));
                    fullSb.Append('"');
                }
                else
                {
                    fullSb.Append(arg);
                }
            }

            foreach (var redir in cmd.Redirections)
            {
                switch (redir.Type)
                {
                    case RedirectType.Out:
                        fullSb.Append(" > ");
                        fullSb.Append(QuoteForShell(Utils.FileSystem.ResolvePath(redir.Target, workingDirectory)));
                        break;
                    case RedirectType.Append:
                        fullSb.Append(" >> ");
                        fullSb.Append(QuoteForShell(Utils.FileSystem.ResolvePath(redir.Target, workingDirectory)));
                        break;
                    case RedirectType.In:
                        fullSb.Append(" < ");
                        fullSb.Append(QuoteForShell(Utils.FileSystem.ResolvePath(redir.Target, workingDirectory)));
                        break;
                    case RedirectType.Err:
                        fullSb.Append(" 2> ");
                        fullSb.Append(QuoteForShell(Utils.FileSystem.ResolvePath(redir.Target, workingDirectory)));
                        break;
                    case RedirectType.ErrAppend:
                        fullSb.Append(" 2>> ");
                        fullSb.Append(QuoteForShell(Utils.FileSystem.ResolvePath(redir.Target, workingDirectory)));
                        break;
                    case RedirectType.ErrToOut:
                        fullSb.Append(" 2>&1");
                        break;
                }
            }
        }

        string fullCommand = fullSb.ToString();

        var psi = new ProcessStartInfo
        {
            FileName = Utils.Platform.DefaultShell,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        Utils.Platform.AddShellCommandArguments(psi, fullCommand);

        foreach (var kv in env.Variables)
            psi.Environment[kv.Key] = kv.Value;

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine($"aursh: failed to start shell for pipeline");
                return 126;
            }

            if (pipeline.Background)
            {
                string cmdDesc = string.Join(" | ", pipeline.Commands.Select(c => c.Name));
                int jobId = env.Jobs.Add(process, cmdDesc);
                Console.WriteLine($"[{jobId}] {process.Id}");
                return 0;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: pipeline execution failed: {ex.Message}");
            return 126;
        }
    }

    private static string QuoteForShell(string value)
    {
        if (value.Contains(' ') || value.Contains('"') || value.Contains('\''))
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        return value;
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

    private static (FileStream? stdout, FileStream? stderr, FileStream? stdin, bool errToOut) ApplyRedirections(
        CommandNode cmd, ProcessStartInfo psi, string workingDirectory, List<FileStream> streams)
    {
        FileStream? stdoutFile = null;
        FileStream? stderrFile = null;
        FileStream? stdinFile = null;
        bool errToOut = false;

        foreach (var redir in cmd.Redirections)
        {
            string target = Utils.FileSystem.ResolvePath(redir.Target, workingDirectory);
            switch (redir.Type)
            {
                case RedirectType.Out:
                    stdoutFile = SafeOpenFileStream(target, FileMode.Create, FileAccess.Write);
                    if (stdoutFile == null) continue;
                    psi.RedirectStandardOutput = true;
                    streams.Add(stdoutFile);
                    break;
                case RedirectType.Append:
                    stdoutFile = SafeOpenFileStream(target, FileMode.Append, FileAccess.Write);
                    if (stdoutFile == null) continue;
                    psi.RedirectStandardOutput = true;
                    streams.Add(stdoutFile);
                    break;
                case RedirectType.In:
                    stdinFile = SafeOpenFileStream(target, FileMode.Open, FileAccess.Read);
                    if (stdinFile == null) continue;
                    psi.RedirectStandardInput = true;
                    streams.Add(stdinFile);
                    break;
                case RedirectType.Err:
                    stderrFile = SafeOpenFileStream(target, FileMode.Create, FileAccess.Write);
                    if (stderrFile == null) continue;
                    psi.RedirectStandardError = true;
                    streams.Add(stderrFile);
                    break;
                case RedirectType.ErrAppend:
                    stderrFile = SafeOpenFileStream(target, FileMode.Append, FileAccess.Write);
                    if (stderrFile == null) continue;
                    psi.RedirectStandardError = true;
                    streams.Add(stderrFile);
                    break;
                case RedirectType.ErrToOut:
                    psi.RedirectStandardError = true;
                    errToOut = true;
                    break;
            }
        }

        return (stdoutFile, stderrFile, stdinFile, errToOut);
    }

    private static FileStream? SafeOpenFileStream(string path, FileMode mode, FileAccess access)
    {
        try
        {
            return new FileStream(path, mode, access);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: cannot open {path}: {ex.Message}");
            return null;
        }
    }

    private static System.Threading.Tasks.Task PumpStreamAsync(Stream source, Stream destination, object? writeLock = null)
    {
        return System.Threading.Tasks.Task.Run(() =>
        {
            if (writeLock != null)
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    lock (writeLock)
                    {
                        destination.Write(buffer, 0, read);
                    }
                }
            }
            else
            {
                source.CopyTo(destination);
            }
        });
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

        Utils.Platform.AddShellCommandArguments(psi, fullCommand);

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
                    stdoutFile = SafeOpenFileStream(target, FileMode.Create, FileAccess.Write);
                    if (stdoutFile == null) return 1;
                    psi.RedirectStandardOutput = true;
                    redirectStdout = true;
                    break;
                case RedirectType.Append:
                    stdoutFile = SafeOpenFileStream(target, FileMode.Append, FileAccess.Write);
                    if (stdoutFile == null) return 1;
                    psi.RedirectStandardOutput = true;
                    redirectStdout = true;
                    break;
                case RedirectType.In:
                    stdinFile = SafeOpenFileStream(target, FileMode.Open, FileAccess.Read);
                    if (stdinFile == null) return 1;
                    psi.RedirectStandardInput = true;
                    redirectStdin = true;
                    break;
                case RedirectType.Err:
                    stderrFile = SafeOpenFileStream(target, FileMode.Create, FileAccess.Write);
                    if (stderrFile == null) return 1;
                    psi.RedirectStandardError = true;
                    redirectStderr = true;
                    break;
                case RedirectType.ErrAppend:
                    stderrFile = SafeOpenFileStream(target, FileMode.Append, FileAccess.Write);
                    if (stderrFile == null) return 1;
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
                int jobId = env.Jobs.Add(process, cmd.Name + (cmd.Args.Count > 0 ? " " + string.Join(" ", cmd.Args) : ""));
                Console.WriteLine($"[{jobId}] {process.Id}");
                return 0;
            }

            var tasks = new List<System.Threading.Tasks.Task>();

            if (errToOut && stdoutFile != null)
            {
                var fileLock = new object();
                tasks.Add(PumpStreamAsync(process.StandardOutput.BaseStream, stdoutFile, fileLock));
                tasks.Add(PumpStreamAsync(process.StandardError.BaseStream, stdoutFile, fileLock));
            }
            else
            {
                if (redirectStdout && stdoutFile != null)
                {
                    tasks.Add(PumpStreamAsync(process.StandardOutput.BaseStream, stdoutFile));
                }

                if (errToOut && stdoutFile == null)
                {
                    tasks.Add(PumpStreamAsync(process.StandardError.BaseStream, Console.OpenStandardOutput()));
                }
                else if (redirectStderr && stderrFile != null)
                {
                    tasks.Add(PumpStreamAsync(process.StandardError.BaseStream, stderrFile));
                }
            }

            System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
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

        Utils.Platform.AddShellCommandArguments(psi, sb.ToString());

        foreach (var kv in env.Variables)
            psi.Environment[kv.Key] = kv.Value;

        return psi;
    }

    private static bool TryGetAssociationShellCommand(CommandNode cmd, ShellEnvironment env, string workingDirectory, out string shellCommand)
    {
        shellCommand = "";
        
        if (string.IsNullOrEmpty(cmd.Name))
            return false;

        bool isFilePath = cmd.Name.Contains('/') || cmd.Name.Contains('\\') || cmd.Name.StartsWith(".");
        if (!isFilePath)
        {
            string fullTestPath = Utils.FileSystem.ResolvePath(cmd.Name, workingDirectory);
            if (File.Exists(fullTestPath))
                isFilePath = true;
        }

        if (isFilePath)
        {
            string fullPath = Utils.FileSystem.ResolvePath(cmd.Name, workingDirectory);
            if (File.Exists(fullPath))
            {
                string ext = Path.GetExtension(fullPath);
                if (!string.IsNullOrEmpty(ext))
                {
                    string? template = env.Associator.GetAssociation(ext);
                    if (template != null)
                    {
                        if (!template.Contains("{0}"))
                        {
                            template += " \"{0}\" {1}";
                        }
                        
                        string newArgsStr = string.Join(" ", cmd.Args);
                        shellCommand = template.Replace("{0}", fullPath).Replace("{1}", newArgsStr).Trim();
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
