using System;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshKillCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: kill: usage: kill %job_id or kill PID");
            return 1;
        }

        int result = 0;

        foreach (string arg in cmd.Args)
        {
            if (arg.StartsWith("%"))
            {
                string idStr = arg.Substring(1);
                if (int.TryParse(idStr, out int jobId))
                {
                    if (env.Jobs.Kill(jobId))
                    {
                        var job = env.Jobs.GetById(jobId);
                        if (job != null)
                            Console.WriteLine($"[{job.Id}]  Killed  {job.Command}");
                    }
                    else
                    {
                        Console.Error.WriteLine($"aursh: kill: %{jobId}: no such job");
                        result = 1;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"aursh: kill: {arg}: invalid job spec");
                    result = 1;
                }
            }
            else
            {
                if (int.TryParse(arg, out int pid))
                {
                    var jobByPid = env.Jobs.GetByPid(pid);
                    if (jobByPid != null)
                    {
                        env.Jobs.Kill(jobByPid.Id);
                        Console.WriteLine($"[{jobByPid.Id}]  Killed  {jobByPid.Command}");
                    }
                    else
                    {
                        try
                        {
                            var proc = System.Diagnostics.Process.GetProcessById(pid);
                            proc.Kill();
                            Console.WriteLine($"Killed process {pid}");
                        }
                        catch
                        {
                            Console.Error.WriteLine($"aursh: kill: ({pid}) - No such process");
                            result = 1;
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine($"aursh: kill: {arg}: invalid argument");
                    result = 1;
                }
            }
        }

        return result;
    }
}
