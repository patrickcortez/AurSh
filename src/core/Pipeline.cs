using System.Diagnostics;
using AurShell.BlackBoxView;

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

    private static bool ShouldRouteToBlackBox(CommandNode cmd, out BlackBox box, out BlackBoxSession session)
    {
        var current = BlackBox.Current;
        if (current == null || current.ActiveSession == null || !current.Config.Enabled)
        {
            box = null!;
            session = null!;
            return false;
        }
        box = current;
        session = current.ActiveSession;
        if (BypassList.IsBypassed(cmd.Name, box.Config))
        {
            session.MarkTtyBypassed();
            return false;
        }
        // Passthrough sessions: the box framing is rendered by Shell.Run, but
        // the child must inherit the real terminal stdio so interactive REPLs
        // (python, ssh, mysql, …) work on platforms without a PTY. Do NOT
        // redirect through BlackBoxIo for these.
        if (session.Passthrough)
            return false;
        return true;
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
                    string target = ResolveRedirectionTarget(redir.Target, workingDirectory);
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
                        case RedirectType.HereString:
                            stdinStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(target + "\n"));
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

                BlackBoxWriter? boxOut = null;
                BlackBoxWriter? boxErr = null;
                TextWriter? boxOutOrigOut = null;
                TextWriter? boxOutOrigErr = null;
                if (ShouldRouteToBlackBox(cmd, out var box, out var sess))
                {
                    if (originalOut == null)
                    {
                        boxOutOrigOut = Console.Out;
                        boxOut = new BlackBoxWriter(sess, LineKind.Stdout,
                            () => box.LiveRenderer.Update(sess, sess.TerminalOut));
                        Console.SetOut(boxOut);
                    }
                    if (originalErr == null)
                    {
                        boxOutOrigErr = Console.Error;
                        boxErr = new BlackBoxWriter(sess, LineKind.Stderr,
                            () => box.LiveRenderer.Update(sess, sess.TerminalOut));
                        Console.SetError(boxErr);
                    }
                }

                try
                {
                    return BuiltinCommands.Execute(cmd, env, ref workingDirectory);
                }
                finally
                {
                    boxOut?.Flush();
                    boxErr?.Flush();
                    if (boxOutOrigOut != null) Console.SetOut(boxOutOrigOut);
                    if (boxOutOrigErr != null) Console.SetError(boxOutOrigErr);
                }
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

            // F# plugins: build a CommandNode so the process goes through
            // ExecuteExternal with full BlackBox pipe capture, instead of
            // the plugin manager's unmanaged child process path.
            var fsharpArgs = env.PluginManager.BuildFSharpArgs(cmd.Name, cmd.Args.ToList());
            if (fsharpArgs != null)
            {
                var fsharpNode = new CommandNode { Name = "dotnet" };
                foreach (string arg in fsharpArgs)
                    fsharpNode.Args.Add(arg);
                foreach (var r in cmd.Redirections) fsharpNode.Redirections.Add(r);
                return ExecuteExternal(fsharpNode, env, workingDirectory, background);
            }

            return env.PluginManager.ExecutePluginCommand(cmd.Name, cmd.Args.ToList());
        }

        return ExecuteExternal(cmd, env, workingDirectory, background);
    }

    private static int ExecuteExternal(CommandNode cmd, ShellEnvironment env, string workingDirectory, bool background)
    {
        if (TryGetAssociationShellCommand(cmd, env, workingDirectory, out string assocCmd))
        {
            var parts = assocCmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                string? resolvedAssocExe = ResolveCommand(parts[0], workingDirectory);
                if (resolvedAssocExe == null)
                {
                    Console.Error.WriteLine($"aursh: {parts[0]}: command not found");
                    return 127;
                }
                var tempCmd = new CommandNode { Name = resolvedAssocExe };
                for (int i = 1; i < parts.Length; i++) tempCmd.Args.Add(parts[i]);
                foreach (var r in cmd.Redirections) tempCmd.Redirections.Add(r);

                var assocPsi = CreateProcessStartInfo(resolvedAssocExe, tempCmd, env, workingDirectory);
                var process = Process.Start(assocPsi);
                if (process != null)
                {
                    if (!background) process.WaitForExit();
                    return background ? 0 : process.ExitCode;
                }
            }
            return 126;
        }

        // Resolve alias (e.g. BusyBox) before PATH lookup
        ResolveAlias(cmd, env);

        string? executable = ResolveCommand(cmd.Name, workingDirectory);
        if (executable == null)
        {
            Console.Error.WriteLine($"aursh: {cmd.Name}: command not found");
            return 127;
        }

        string actualExecutable = executable;
        IList<string> actualArgs = cmd.Args;

        bool useBoxPty = BlackBox.Current?.ActiveSession != null
                      && !BypassList.IsBypassed(cmd.Name, BlackBox.Current.Config)
                      && PtyHost.IsAvailable()
                      && PtyHost.NeedsPty(cmd.Name);
        if (useBoxPty)
        {
            var wrapped = PtyHost.WrapForPty(executable, cmd.Args);
            actualExecutable = wrapped.executable;
            actualArgs = wrapped.args;
        }

        var psi = new ProcessStartInfo
        {
            FileName = actualExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        foreach (string arg in actualArgs)
            psi.ArgumentList.Add(arg);

        foreach (var kv in env.Variables)
            psi.Environment[kv.Key] = kv.Value;

        if (useBoxPty)
        {
            // `script -c <cmd>` execs via $SHELL; aursh sets SHELL=aursh so override
            // it to a real POSIX shell for the wrapped child only.
            string realShell = System.IO.File.Exists("/bin/bash") ? "/bin/bash"
                             : System.IO.File.Exists("/bin/sh") ? "/bin/sh"
                             : "/bin/sh";
            psi.Environment["SHELL"] = realShell;
        }

        FileStream? stdoutFile = null;
        FileStream? stderrFile = null;
        Stream? stdinFile = null;
        bool redirectStdout = false;
        bool redirectStderr = false;
        bool redirectStdin = false;
        bool errToOut = false;

        foreach (var redir in cmd.Redirections)
        {
            string target = ResolveRedirectionTarget(redir.Target, workingDirectory);
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
                case RedirectType.HereString:
                    stdinFile = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(target + "\n"));
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

        bool routeToBox = ShouldRouteToBlackBox(cmd, out var boxOwner, out var boxSession);
        var boxFlags = new BoxRedirectFlags
        {
            StdoutRedirected = redirectStdout,
            StderrRedirected = redirectStderr || errToOut,
            StdinRedirected = redirectStdin
        };
        if (routeToBox)
        {
            BlackBoxIo.PrepareForBox(psi, boxFlags);
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
                env.BackgroundPid = process.Id;
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

            System.Threading.CancellationTokenSource? boxPumpCancel = null;
            System.Threading.Tasks.Task? boxPumpTask = null;
            if (routeToBox)
            {
                boxPumpCancel = new System.Threading.CancellationTokenSource();
                boxPumpTask = BlackBoxIo.PumpAsync(
                    process,
                    boxSession,
                    boxFlags,
                    boxOwner,
                    stageIndex: null,
                    stdoutForward: null,
                    cancellation: boxPumpCancel.Token);
            }

            System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
            process.WaitForExit();

            if (boxPumpTask != null)
            {
                // The pump must fully drain the child's stdout/stderr pipes
                // before we return. When the process exits, the OS pipe
                // buffer may still hold unread data — the pump's ReadAsync
                // will see it on the next iteration and then return 0 (EOF)
                // naturally. Give it a generous window; 5 seconds is well
                // beyond any realistic pipe-drain time. Only cancel as a
                // safety fallback so the shell never hangs on a broken pipe.
                try { boxPumpTask.Wait(System.TimeSpan.FromSeconds(5)); } catch { }
                if (!boxPumpTask.IsCompleted)
                {
                    boxPumpCancel?.Cancel();
                    try { boxPumpTask.Wait(System.TimeSpan.FromMilliseconds(500)); } catch { }
                }
                boxPumpCancel?.Dispose();
            }

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

        BlackBox? pipelineBox = null;
        BlackBoxSession? pipelineSession = null;
        if (BlackBox.Current?.ActiveSession is BlackBoxSession activePipeSession && !pipeline.Background)
        {
            pipelineBox = BlackBox.Current;
            pipelineSession = activePipeSession;
            bool anyBypassed = false;
            for (int idx = 0; idx < count; idx++)
            {
                if (BypassList.IsBypassed(commands[idx].Name, pipelineBox.Config))
                {
                    anyBypassed = true;
                    break;
                }
            }
            if (anyBypassed)
            {
                pipelineSession.MarkTtyBypassed();
                pipelineBox = null;
                pipelineSession = null;
            }
        }
        bool routePipelineToBox = pipelineBox != null && pipelineSession != null;

        // Coalesced pipeline delegation to OS shell removed to enforce independent system shell behavior

        var processes = new Process?[count];
        var fileStreams = new List<Stream>();
        var redirInfos = new (FileStream? stdout, FileStream? stderr, Stream? stdin, bool errToOut)[count];

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
                                    string target = ResolveRedirectionTarget(r.Target, workingDirectory);
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
                                        string target = ResolveRedirectionTarget(r.Target, workingDirectory);
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
                            tempPipe.Close();
                        }
                    });

                    if (i + 1 < count)
                    {
                        var nextCmd = commands[i + 1];
                        ResolveAlias(nextCmd, env);
                        bool isNextBuiltin = BuiltinCommands.IsBuiltin(nextCmd.Name);
                        string? nextExe = ResolveCommand(nextCmd.Name, workingDirectory);

                        if (nextExe == null && env.PluginManager != null && env.PluginManager.IsPluginCommand(nextCmd.Name))
                        {
                            nextExe = env.PluginManager.GetPluginBinaryPath(nextCmd.Name);
                        }

                        if (!isNextBuiltin && nextExe == null && !TryGetAssociationShellCommand(nextCmd, env, workingDirectory, out _))
                        {
                            // Let the next iteration handle the shell command parsing, we don't abort.
                        }
                        else if (nextExe == null && !isNextBuiltin)
                        {
                            Console.Error.WriteLine($"aursh: {nextCmd.Name}: command not found");
                            clientStream?.Dispose();
                            builtinTask.Wait();
                            return 127;
                        }

                        var nextPsi = CreateProcessStartInfo(nextExe!, nextCmd, env, workingDirectory);
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
                    var parts = assocCmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        string? resolvedAssocExe = ResolveCommand(parts[0], workingDirectory);
                        if (resolvedAssocExe == null)
                        {
                            Console.Error.WriteLine($"aursh: {parts[0]}: command not found");
                            return 127;
                        }
                        var tempCmd = new CommandNode { Name = resolvedAssocExe };
                        for (int argIdx = 1; argIdx < parts.Length; argIdx++) tempCmd.Args.Add(parts[argIdx]);
                        foreach (var r in cmd.Redirections) tempCmd.Redirections.Add(r);

                        var assocPsi = CreateProcessStartInfo(resolvedAssocExe, tempCmd, env, workingDirectory);

                        if (!isFirst) assocPsi.RedirectStandardInput = true;
                        if (!isLast || routePipelineToBox) assocPsi.RedirectStandardOutput = true;
                        if (routePipelineToBox) assocPsi.RedirectStandardError = true;

                        redirInfos[i] = ApplyRedirections(cmd, assocPsi, workingDirectory, fileStreams);

                        processes[i] = Process.Start(assocPsi);
                        if (processes[i] == null)
                        {
                            Console.Error.WriteLine($"aursh: {parts[0]}: failed to start associated process");
                            return 126;
                        }
                        continue;
                    }
                }

                // Resolve alias (e.g. BusyBox) before PATH lookup
                ResolveAlias(cmd, env);

                string? executable = ResolveCommand(cmd.Name, workingDirectory);
                if (executable == null && env.PluginManager != null && env.PluginManager.IsPluginCommand(cmd.Name))
                {
                    executable = env.PluginManager.GetPluginBinaryPath(cmd.Name);
                }

                if (executable == null)
                {
                    Console.Error.WriteLine($"aursh: {cmd.Name}: command not found");
                    return 127;
                }

                var psi = CreateProcessStartInfo(executable, cmd, env, workingDirectory);

                if (!isFirst) psi.RedirectStandardInput = true;
                if (!isLast || routePipelineToBox) psi.RedirectStandardOutput = true;
                if (routePipelineToBox) psi.RedirectStandardError = true;

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

            System.Threading.CancellationTokenSource? boxCancel = null;
            if (routePipelineToBox && pipelineBox != null && pipelineSession != null)
            {
                boxCancel = new System.Threading.CancellationTokenSource();
                for (int i = 0; i < count; i++)
                {
                    if (processes[i] == null) continue;
                    var (stdoutFile, stderrFile, _, errToOut) = redirInfos[i];
                    bool isLast = i == count - 1;
                    var proc = processes[i]!;
                    int stageIndex = i;

                    if (isLast && stdoutFile == null && proc.StartInfo.RedirectStandardOutput)
                    {
                        drainTasks.Add(BlackBoxIo.PumpToBufferAsync(
                            proc.StandardOutput.BaseStream,
                            pipelineSession,
                            LineKind.Stdout,
                            count > 1 ? (int?)stageIndex : null,
                            null,
                            pipelineBox,
                            boxCancel.Token));
                    }

                    if (stderrFile == null && !errToOut && proc.StartInfo.RedirectStandardError)
                    {
                        drainTasks.Add(BlackBoxIo.PumpToBufferAsync(
                            proc.StandardError.BaseStream,
                            pipelineSession,
                            LineKind.Stderr,
                            count > 1 ? (int?)stageIndex : null,
                            null,
                            pipelineBox,
                            boxCancel.Token));
                    }
                }
            }

            try
            {
                System.Threading.Tasks.Task.WaitAll(drainTasks.ToArray());
            }
            catch (AggregateException) { }
            finally
            {
                boxCancel?.Dispose();
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

    private static (FileStream? stdout, FileStream? stderr, Stream? stdin, bool errToOut) ApplyRedirections(
        CommandNode cmd, ProcessStartInfo psi, string workingDirectory, List<Stream> streams)
    {
        FileStream? stdoutFile = null;
        FileStream? stderrFile = null;
        Stream? stdinFile = null;
        bool errToOut = false;

        foreach (var redir in cmd.Redirections)
        {
            string target = ResolveRedirectionTarget(redir.Target, workingDirectory);
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
                case RedirectType.HereString:
                    stdinFile = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(target + "\n"));
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

    /// <summary>
    /// Resolves alias for a CommandNode before external execution.
    /// If the command name has an alias, the alias is expanded into
    /// the command name and any alias args are prepended to the command args.
    /// </summary>
    private static void ResolveAlias(CommandNode cmd, ShellEnvironment env)
    {
        string? alias = env.GetAlias(cmd.Name);
        if (alias == null)
        {
            return;
        }

        string[] parts = alias.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        // Save original args before modifying
        var originalArgs = new List<string>(cmd.Args);

        // Strip surrounding quotes from executable path (BusyBox aliases use them)
        cmd.Name = parts[0].Trim('"');
        cmd.Args.Clear();

        // Add alias args (e.g. for "busybox.exe head" -> alias arg is "head")
        for (int i = 1; i < parts.Length; i++)
        {
            cmd.Args.Add(parts[i]);
        }

        // Append original command args after alias args
        foreach (string arg in originalArgs)
        {
            cmd.Args.Add(arg);
        }
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

                        string newArgsStr = string.Join(" ", cmd.RawExpandedArgs);
                        shellCommand = template.Replace("{0}", fullPath).Replace("{1}", newArgsStr).Trim();
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static string ResolveRedirectionTarget(string target, string workingDirectory)
    {
        if (target.Equals("nul", StringComparison.OrdinalIgnoreCase) || target.Equals("/dev/null", StringComparison.OrdinalIgnoreCase))
        {
            return Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows ? "nul" : "/dev/null";
        }
        return Utils.FileSystem.ResolvePath(target, workingDirectory);
    }
}
