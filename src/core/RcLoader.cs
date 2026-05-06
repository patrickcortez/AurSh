namespace AurShell.Core;

public class RcLoader
{
    private readonly ShellEnvironment _env;
    private readonly Executor _executor;

    public RcLoader(ShellEnvironment env, Executor executor)
    {
        _env = env;
        _executor = executor;
    }

    public int Load()
    {
        string rcPath = Utils.Platform.RcFilePath;

        if (!File.Exists(rcPath))
            return 0;

        return ExecuteRcFile(rcPath);
    }

    public int LoadFrom(string filePath)
    {
        if (!File.Exists(filePath))
            return 0;

        return ExecuteRcFile(filePath);
    }

    public int LoadConfigDir()
    {
        string configDir = Utils.Platform.ConfigDirectory;
        string configRc = Path.Combine(configDir, "aurshrc");

        int result = 0;

        if (File.Exists(configRc))
            result = ExecuteRcFile(configRc);

        string configInitDir = Path.Combine(configDir, "init.d");
        if (Directory.Exists(configInitDir))
        {
            string[] scripts = Directory.GetFiles(configInitDir, "*.aur");
            Array.Sort(scripts, StringComparer.OrdinalIgnoreCase);

            foreach (string script in scripts)
            {
                int scriptResult = ExecuteAurScript(script);
                if (scriptResult != 0)
                    result = scriptResult;
            }
        }

        return result;
    }

    private int ExecuteRcFile(string filePath)
    {
        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: error reading {filePath}: {ex.Message}");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(content))
            return 0;

        int lastResult = 0;
        string[] lines = content.Split('\n');
        var multiLineBuffer = new System.Text.StringBuilder();
        bool inMultiLine = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');

            if (inMultiLine)
            {
                if (line.TrimEnd().EndsWith('\\'))
                {
                    multiLineBuffer.Append(line.TrimEnd().TrimEnd('\\'));
                    multiLineBuffer.Append(' ');
                    continue;
                }

                multiLineBuffer.Append(line);
                string fullLine = multiLineBuffer.ToString().Trim();
                multiLineBuffer.Clear();
                inMultiLine = false;

                if (!string.IsNullOrEmpty(fullLine) && !fullLine.StartsWith('#'))
                    lastResult = _executor.Execute(fullLine);

                continue;
            }

            if (line.TrimEnd().EndsWith('\\'))
            {
                inMultiLine = true;
                multiLineBuffer.Clear();
                multiLineBuffer.Append(line.TrimEnd().TrimEnd('\\'));
                multiLineBuffer.Append(' ');
                continue;
            }

            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            lastResult = _executor.Execute(trimmed);
            _env.LastExitCode = lastResult;
        }

        if (inMultiLine && multiLineBuffer.Length > 0)
        {
            string remaining = multiLineBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(remaining) && !remaining.StartsWith('#'))
                lastResult = _executor.Execute(remaining);
        }

        return lastResult;
    }

    private int ExecuteAurScript(string filePath)
    {
        var runner = new ScriptRunner(_env, _executor);
        return runner.RunFile(filePath, Array.Empty<string>());
    }

    public static void CreateDefaultRc(string rcPath)
    {
        if (File.Exists(rcPath))
            return;

        string defaultContent = GenerateDefaultRc();
        Utils.FileSystem.WriteAllTextSafe(rcPath, defaultContent);
    }

    private static string GenerateDefaultRc()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# AurShell RC Configuration");
        sb.AppendLine("# This file is sourced on startup.");
        sb.AppendLine("# Edit it to customize your shell environment.");
        sb.AppendLine();
        sb.AppendLine("# Aliases");
        sb.AppendLine("alias ll='ls -la'");
        sb.AppendLine("alias la='ls -a'");
        sb.AppendLine("alias ..='cd ..'");
        sb.AppendLine("alias ...='cd ../..'");
        sb.AppendLine("alias cls='clear'");
        sb.AppendLine();
        sb.AppendLine("# Environment");
        sb.AppendLine("# export EDITOR=vim");
        sb.AppendLine("# export PAGER=less");
        sb.AppendLine();
        sb.AppendLine("# Custom PATH additions");
        sb.AppendLine("# export PATH=$PATH:~/bin");

        return sb.ToString();
    }
}
