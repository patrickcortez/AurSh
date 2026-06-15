using System.Diagnostics;
using System.Text;

namespace AurShell.Utils;

public class GitInfo
{
    private string? _cachedBranch;
    private string? _cachedDir;
    private bool _cachedIsDirty;
    private int _cachedAhead;
    private int _cachedBehind;
    private int _cachedStaged;
    private int _cachedModified;
    private int _cachedUntracked;
    private int _cachedConflicted;
    private int _cachedDeleted;
    private int _cachedRenamed;
    private int _cachedStash;
    private string? _cachedActionState;
    private bool _cachedIsRepo;
    private string? _cachedRemoteUrl;
    private DateTime _cachedAt = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    public bool IsGitRepo { get; private set; }
    public string Branch { get; private set; } = "";
    public bool IsDirty { get; private set; }
    public int Ahead { get; private set; }
    public int Behind { get; private set; }
    public int StagedCount { get; private set; }
    public int ModifiedCount { get; private set; }
    public int UntrackedCount { get; private set; }
    public int ConflictedCount { get; private set; }
    public int DeletedCount { get; private set; }
    public int RenamedCount { get; private set; }
    public int StashCount { get; private set; }
    public string ActionState { get; private set; } = "";
    public bool IsDetached { get; private set; }
    public string? RemoteUrl { get; private set; }

    public void Refresh(string workingDirectory)
    {
        if (_cachedDir == workingDirectory && DateTime.UtcNow - _cachedAt < CacheDuration)
        {
            IsGitRepo = _cachedIsRepo;
            Branch = _cachedBranch ?? "";
            IsDirty = _cachedIsDirty;
            Ahead = _cachedAhead;
            Behind = _cachedBehind;
            StagedCount = _cachedStaged;
            ModifiedCount = _cachedModified;
            UntrackedCount = _cachedUntracked;
            ConflictedCount = _cachedConflicted;
            DeletedCount = _cachedDeleted;
            RenamedCount = _cachedRenamed;
            StashCount = _cachedStash;
            ActionState = _cachedActionState ?? "";
            RemoteUrl = _cachedRemoteUrl;
            return;
        }

        _cachedDir = workingDirectory;
        _cachedAt = DateTime.UtcNow;

        Reset();

        string? gitDir = FindGitDirectory(workingDirectory);
        if (gitDir == null)
        {
            CacheState();
            return;
        }

        IsGitRepo = true;

        ReadBranch(workingDirectory);
        ReadStatus(workingDirectory);
        ReadAheadBehind(workingDirectory);
        ReadRemoteUrl(workingDirectory);
        ReadActionState(gitDir);
        ReadStashCount(workingDirectory);

        CacheState();
    }

    public string FormatStatus()
    {
        if (!IsGitRepo)
            return "";

        var sb = new StringBuilder();
        sb.Append('\uE0A0');
        sb.Append(' ');
        sb.Append(Branch);

        if (!string.IsNullOrEmpty(ActionState))
        {
            sb.Append($" [{ActionState}]");
        }

        if (IsDirty || ConflictedCount > 0 || StashCount > 0 || DeletedCount > 0 || RenamedCount > 0)
        {
            if (ConflictedCount > 0)
                sb.Append($" !{ConflictedCount}");
            if (StagedCount > 0)
                sb.Append($" +{StagedCount}");
            if (ModifiedCount > 0)
                sb.Append($" ~{ModifiedCount}");
            if (DeletedCount > 0)
                sb.Append($" -{DeletedCount}");
            if (RenamedCount > 0)
                sb.Append($" \u00bb{RenamedCount}");
            if (UntrackedCount > 0)
                sb.Append($" ?{UntrackedCount}");
            if (StashCount > 0)
                sb.Append($" *{StashCount}");
        }
        else
        {
            sb.Append(" \u2713");
        }

        if (Ahead > 0)
            sb.Append($" \u2191{Ahead}");
        if (Behind > 0)
            sb.Append($" \u2193{Behind}");

        return sb.ToString();
    }

    private void Reset()
    {
        IsGitRepo = false;
        Branch = "";
        IsDirty = false;
        Ahead = 0;
        Behind = 0;
        StagedCount = 0;
        ModifiedCount = 0;
        UntrackedCount = 0;
        ConflictedCount = 0;
        DeletedCount = 0;
        RenamedCount = 0;
        StashCount = 0;
        ActionState = "";
        IsDetached = false;
        RemoteUrl = null;
    }

    private void CacheState()
    {
        _cachedIsRepo = IsGitRepo;
        _cachedBranch = Branch;
        _cachedIsDirty = IsDirty;
        _cachedAhead = Ahead;
        _cachedBehind = Behind;
        _cachedStaged = StagedCount;
        _cachedModified = ModifiedCount;
        _cachedUntracked = UntrackedCount;
        _cachedConflicted = ConflictedCount;
        _cachedDeleted = DeletedCount;
        _cachedRenamed = RenamedCount;
        _cachedStash = StashCount;
        _cachedActionState = ActionState;
        _cachedRemoteUrl = RemoteUrl;
    }

    private void ReadBranch(string workingDirectory)
    {
        string? headContent = RunGit("rev-parse --abbrev-ref HEAD", workingDirectory);
        if (headContent == null)
        {
            Branch = "unknown";
            return;
        }

        headContent = headContent.Trim();

        if (headContent == "HEAD")
        {
            IsDetached = true;
            string? tag = RunGit("describe --tags --exact-match", workingDirectory);
            if (tag != null && tag.Trim().Length > 0)
            {
                Branch = tag.Trim();
            }
            else
            {
                string? shortHash = RunGit("rev-parse --short HEAD", workingDirectory);
                Branch = shortHash?.Trim() ?? "detached";
            }
        }
        else
        {
            Branch = headContent;
        }
    }

    private void ReadStatus(string workingDirectory)
    {
        string? statusOutput = RunGit("status --porcelain -u", workingDirectory);
        if (statusOutput == null)
            return;

        string[] lines = statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        int staged = 0;
        int modified = 0;
        int untracked = 0;
        int conflicted = 0;
        int deleted = 0;
        int renamed = 0;

        foreach (string line in lines)
        {
            if (line.Length < 2)
                continue;

            char x = line[0];
            char y = line[1];

            if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D'))
            {
                conflicted++;
                continue;
            }

            if (x == '?' && y == '?')
            {
                untracked++;
                continue;
            }

            if (x == 'R' || y == 'R')
                renamed++;
            else if (x == 'D' || y == 'D')
                deleted++;

            if (x != ' ' && x != '?' && x != 'U' && x != 'R' && x != 'D')
                staged++;

            if (y != ' ' && y != '?' && y != 'U' && y != 'R' && y != 'D')
                modified++;
        }

        StagedCount = staged;
        ModifiedCount = modified;
        UntrackedCount = untracked;
        ConflictedCount = conflicted;
        DeletedCount = deleted;
        RenamedCount = renamed;
        IsDirty = staged > 0 || modified > 0 || untracked > 0 || conflicted > 0 || deleted > 0 || renamed > 0;
    }

    private void ReadStashCount(string workingDirectory)
    {
        string? output = RunGit("stash list", workingDirectory);
        if (string.IsNullOrEmpty(output))
        {
            StashCount = 0;
            return;
        }

        StashCount = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private void ReadActionState(string gitDir)
    {
        ActionState = "";
        try
        {
            if (File.Exists(Path.Combine(gitDir, "rebase-merge", "interactive"))) ActionState = "REBASE-i";
            else if (Directory.Exists(Path.Combine(gitDir, "rebase-merge"))) ActionState = "REBASE-m";
            else if (Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
            {
                if (File.Exists(Path.Combine(gitDir, "rebase-apply", "rebasing"))) ActionState = "REBASE";
                else if (File.Exists(Path.Combine(gitDir, "rebase-apply", "applying"))) ActionState = "AM";
                else ActionState = "AM/REBASE";
            }
            else if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))) ActionState = "MERGING";
            else if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))) ActionState = "CHERRY-PICKING";
            else if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))) ActionState = "REVERTING";
            else if (File.Exists(Path.Combine(gitDir, "BISECT_LOG"))) ActionState = "BISECTING";
        }
        catch { }
    }

    private void ReadAheadBehind(string workingDirectory)
    {
        string? result = RunGit("rev-list --left-right --count HEAD...@{upstream}", workingDirectory);
        if (result == null)
            return;

        string[] parts = result.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            int.TryParse(parts[0], out int ahead);
            int.TryParse(parts[1], out int behind);
            Ahead = ahead;
            Behind = behind;
        }
    }

    private void ReadRemoteUrl(string workingDirectory)
    {
        string? url = RunGit("config --get remote.origin.url", workingDirectory);
        if (url != null && url.Trim().Length > 0)
        {
            RemoteUrl = url.Trim();
        }
        else
        {
            RemoteUrl = null;
        }
    }

    private static string? FindGitDirectory(string startDir)
    {
        string current = startDir;

        while (!string.IsNullOrEmpty(current))
        {
            string gitDir = Path.Combine(current, ".git");

            if (Directory.Exists(gitDir) || File.Exists(gitDir))
                return gitDir;

            string? parent = Path.GetDirectoryName(current);
            if (parent == current || parent == null)
                break;
            current = parent;
        }

        return null;
    }

    private static string? RunGit(string arguments, string workingDirectory)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            if (!process.HasExited)
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
