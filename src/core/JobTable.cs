using System.Diagnostics;

namespace AurShell.Core;

public enum JobState
{
    Running,
    Done,
    Killed
}

public class Job
{
    public int Id { get; }
    public int Pid { get; }
    public string Command { get; }
    public JobState State { get; set; }
    public int ExitCode { get; set; }
    public Process? Process { get; set; }
    public DateTime StartedAt { get; }

    public Job(int id, int pid, string command, Process process)
    {
        Id = id;
        Pid = pid;
        Command = command;
        State = JobState.Running;
        ExitCode = -1;
        Process = process;
        StartedAt = DateTime.Now;
    }

    public bool IsAlive()
    {
        if (Process == null)
            return false;

        try
        {
            return !Process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public void Refresh()
    {
        if (State != JobState.Running)
            return;

        if (!IsAlive())
        {
            State = JobState.Done;
            try
            {
                ExitCode = Process?.ExitCode ?? -1;
            }
            catch
            {
                ExitCode = -1;
            }
        }
    }

    public string FormatStatus()
    {
        string stateStr = State switch
        {
            JobState.Running => "Running",
            JobState.Done => $"Done ({ExitCode})",
            JobState.Killed => "Killed",
            _ => "Unknown"
        };

        return $"[{Id}]  {Pid,-8} {stateStr,-16} {Command}";
    }
}

public class JobTable
{
    private readonly List<Job> _jobs = new();
    private int _nextId = 1;
    private readonly object _lock = new();

    public int Add(Process process, string command)
    {
        lock (_lock)
        {
            int id = _nextId++;
            int pid;
            try
            {
                pid = process.Id;
            }
            catch
            {
                pid = -1;
            }

            var job = new Job(id, pid, command, process);
            _jobs.Add(job);
            return id;
        }
    }

    public List<Job> GetAll()
    {
        lock (_lock)
        {
            RefreshAll();
            return new List<Job>(_jobs);
        }
    }

    public Job? GetById(int jobId)
    {
        lock (_lock)
        {
            RefreshAll();
            return _jobs.Find(j => j.Id == jobId);
        }
    }

    public Job? GetByPid(int pid)
    {
        lock (_lock)
        {
            RefreshAll();
            return _jobs.Find(j => j.Pid == pid);
        }
    }

    public Job? GetMostRecent()
    {
        lock (_lock)
        {
            RefreshAll();
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                if (_jobs[i].State == JobState.Running)
                    return _jobs[i];
            }
            return _jobs.Count > 0 ? _jobs[_jobs.Count - 1] : null;
        }
    }

    public List<Job> CollectFinished()
    {
        lock (_lock)
        {
            RefreshAll();
            var finished = new List<Job>();

            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                if (_jobs[i].State == JobState.Done || _jobs[i].State == JobState.Killed)
                {
                    finished.Add(_jobs[i]);
                    _jobs.RemoveAt(i);
                }
            }

            finished.Reverse();
            return finished;
        }
    }

    public bool Remove(int jobId)
    {
        lock (_lock)
        {
            int idx = _jobs.FindIndex(j => j.Id == jobId);
            if (idx >= 0)
            {
                _jobs.RemoveAt(idx);
                return true;
            }
            return false;
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _jobs.Count;
            }
        }
    }

    public int RunningCount
    {
        get
        {
            lock (_lock)
            {
                RefreshAll();
                int count = 0;
                foreach (var job in _jobs)
                {
                    if (job.State == JobState.Running)
                        count++;
                }
                return count;
            }
        }
    }

    public int ForegroundWait(int jobId)
    {
        Job? job;
        lock (_lock)
        {
            job = _jobs.Find(j => j.Id == jobId);
        }

        if (job == null)
            return -1;

        if (job.Process == null || job.State != JobState.Running)
        {
            lock (_lock)
            {
                _jobs.Remove(job);
            }
            return job.ExitCode;
        }

        try
        {
            job.Process.WaitForExit();
            job.ExitCode = job.Process.ExitCode;
            job.State = JobState.Done;
        }
        catch
        {
            job.State = JobState.Killed;
            job.ExitCode = -1;
        }

        lock (_lock)
        {
            _jobs.Remove(job);
        }

        return job.ExitCode;
    }

    public bool Kill(int jobId)
    {
        lock (_lock)
        {
            var job = _jobs.Find(j => j.Id == jobId);
            if (job == null)
                return false;

            if (job.Process != null && job.State == JobState.Running)
            {
                try
                {
                    job.Process.Kill(entireProcessTree: true);
                }
                catch
                {
                    try
                    {
                        job.Process.Kill();
                    }
                    catch { }
                }
                job.State = JobState.Killed;
                job.ExitCode = 137;
            }

            return true;
        }
    }

    private void RefreshAll()
    {
        foreach (var job in _jobs)
            job.Refresh();
    }
}
