using System;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshFgCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        int jobId;

        if (cmd.Args.Count == 0)
        {
            var recent = env.Jobs.GetMostRecent();
            if (recent == null)
            {
                Console.Error.WriteLine("aursh: fg: no current job");
                return 1;
            }
            jobId = recent.Id;
        }
        else
        {
            string arg = cmd.Args[0];
            if (arg.StartsWith("%"))
                arg = arg.Substring(1);

            if (!int.TryParse(arg, out jobId))
            {
                Console.Error.WriteLine($"aursh: fg: {cmd.Args[0]}: no such job");
                return 1;
            }
        }

        var job = env.Jobs.GetById(jobId);
        if (job == null)
        {
            Console.Error.WriteLine($"aursh: fg: %{jobId}: no such job");
            return 1;
        }

        if (job.State != JobState.Running)
        {
            Console.Error.WriteLine($"aursh: fg: %{jobId}: job has already completed");
            env.Jobs.Remove(jobId);
            return job.ExitCode;
        }

        Console.WriteLine(job.Command);
        int exitCode = env.Jobs.ForegroundWait(jobId);
        return exitCode;
    }
}
