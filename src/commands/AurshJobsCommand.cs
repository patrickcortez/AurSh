using System;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshJobsCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        var allJobs = env.Jobs.GetAll();

        if (allJobs.Count == 0)
        {
            return 0;
        }

        bool showPids = cmd.Args.Contains("-l");

        foreach (var job in allJobs)
        {
            string stateStr = job.State switch
            {
                JobState.Running => "Running",
                JobState.Done => "Done",
                JobState.Killed => "Killed",
                _ => "Unknown"
            };

            if (showPids)
                Console.WriteLine($"[{job.Id}]  {job.Pid,-8} {stateStr,-12} {job.Command}");
            else
                Console.WriteLine($"[{job.Id}]  {stateStr,-12} {job.Command}");
        }

        return 0;
    }
}
