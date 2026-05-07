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

        string defaultContent = GenerateDefaultRc(Utils.Platform.CurrentOS);
        Utils.FileSystem.WriteAllTextSafe(rcPath, defaultContent);
    }

    private static string GenerateDefaultRc(Utils.OperatingSystemType os)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# ──────────────────────────────────────────────────────────");
        sb.AppendLine("# AurShell RC Configuration");
        sb.AppendLine("# This file is sourced on every interactive shell startup.");
        sb.AppendLine("# Edit it to customize your shell environment.");
        sb.AppendLine("# ──────────────────────────────────────────────────────────");
        sb.AppendLine();

        sb.AppendLine("# ── Welcome Banner ──");
        sb.AppendLine("# Set to 'off' to disable the startup ASCII art banner:");
        sb.AppendLine("# export AURSH_BANNER=off");
        sb.AppendLine();

        sb.AppendLine("# ── Prompt Customization ──");
        sb.AppendLine("# The prompt is built from two lines using token placeholders.");
        sb.AppendLine("# Set AURSH_PROMPT for line 1 and AURSH_PROMPT2 for line 2.");
        sb.AppendLine("#");
        sb.AppendLine("# Available tokens:");
        sb.AppendLine("#   {box_top}       Box-drawing top-left corner");
        sb.AppendLine("#   {box_bottom}    Box-drawing bottom-left corner");
        sb.AppendLine("#   {os_badge}      OS icon in a colored badge");
        sb.AppendLine("#   {powerline}     Powerline transition arrow");
        sb.AppendLine("#   {user_host}     user@hostname segment");
        sb.AppendLine("#   {dir_badge}     Directory with powerline styling");
        sb.AppendLine("#   {git}           Git branch and status segment");
        sb.AppendLine("#   {status}        Non-zero exit code indicator");
        sb.AppendLine("#   {chevron}       Colored > prompt character");
        sb.AppendLine("#   {time}          Current time HH:MM:SS");
        sb.AppendLine("#   {user}          Username (plain text)");
        sb.AppendLine("#   {host}          Hostname (plain text)");
        sb.AppendLine("#   {cwd}           Shortened current directory");
        sb.AppendLine("#   {cwd_full}      Full current directory path");
        sb.AppendLine("#   {os_icon}       OS icon character");
        sb.AppendLine("#   {os_name}       OS name string");
        sb.AppendLine("#   {shell}         Shell name (aursh)");
        sb.AppendLine("#   {version}       Shell version");
        sb.AppendLine("#   {exit_code}     Last exit code number");
        sb.AppendLine("#   {dollar}        $ or # (root)");
        sb.AppendLine("#   {arrow}         Unicode arrow character");
        sb.AppendLine("#   {lambda}        Lambda character");
        sb.AppendLine("#   {branch}        Git branch name (plain text)");
        sb.AppendLine("#   {newline}       Newline character");
        sb.AppendLine("#   {reset}         Reset all colors/styles");
        sb.AppendLine("#   {bold}          Bold text");
        sb.AppendLine("#   {dim}           Dim text");
        sb.AppendLine("#   {fg:red}        Named foreground color");
        sb.AppendLine("#   {bg:blue}       Named background color");
        sb.AppendLine("#   {fg:255,100,0}  RGB foreground color");
        sb.AppendLine("#   {bg:30,30,50}   RGB background color");
        sb.AppendLine("#");
        sb.AppendLine("# Default prompt (uncomment to customize):");
        sb.AppendLine("# export AURSH_PROMPT='{box_top} {os_badge}{powerline}{user_host}{powerline}{dir_badge}{git}{status}'");
        sb.AppendLine("# export AURSH_PROMPT2='{box_bottom} {chevron} '");
        sb.AppendLine("#");
        sb.AppendLine("# Minimal prompt example:");
        sb.AppendLine("# export AURSH_PROMPT='{fg:cyan}{cwd}{reset} {branch}'");
        sb.AppendLine("# export AURSH_PROMPT2='{chevron} '");
        sb.AppendLine();

        sb.AppendLine("# ── Aliases ──");

        switch (os)
        {
            case Utils.OperatingSystemType.Windows:
                sb.AppendLine("alias ls='dir'");
                sb.AppendLine("alias ll='dir'");
                sb.AppendLine("alias cls='clear'");
                sb.AppendLine("alias ..='cd ..'");
                sb.AppendLine("alias ...='cd ../..'");
                sb.AppendLine("alias home='cd ~'");
                sb.AppendLine("alias open='explorer'");
                break;

            case Utils.OperatingSystemType.MacOS:
                sb.AppendLine("alias ll='ls -la'");
                sb.AppendLine("alias la='ls -a'");
                sb.AppendLine("alias cls='clear'");
                sb.AppendLine("alias ..='cd ..'");
                sb.AppendLine("alias ...='cd ../..'");
                sb.AppendLine("alias home='cd ~'");
                sb.AppendLine("alias open='open'");
                sb.AppendLine("alias grep='grep --color=auto'");
                break;

            case Utils.OperatingSystemType.Termux:
                sb.AppendLine("alias ll='ls -la --color=auto'");
                sb.AppendLine("alias la='ls -a --color=auto'");
                sb.AppendLine("alias cls='clear'");
                sb.AppendLine("alias ..='cd ..'");
                sb.AppendLine("alias ...='cd ../..'");
                sb.AppendLine("alias home='cd ~'");
                sb.AppendLine("alias grep='grep --color=auto'");
                sb.AppendLine("alias pkg-update='pkg update && pkg upgrade'");
                break;

            default:
                sb.AppendLine("alias ll='ls -la --color=auto'");
                sb.AppendLine("alias la='ls -a --color=auto'");
                sb.AppendLine("alias cls='clear'");
                sb.AppendLine("alias ..='cd ..'");
                sb.AppendLine("alias ...='cd ../..'");
                sb.AppendLine("alias home='cd ~'");
                sb.AppendLine("alias grep='grep --color=auto'");
                sb.AppendLine("alias open='xdg-open'");
                break;
        }

        sb.AppendLine();

        sb.AppendLine("# ── Environment ──");
        switch (os)
        {
            case Utils.OperatingSystemType.Windows:
                sb.AppendLine("# export EDITOR=notepad");
                break;
            case Utils.OperatingSystemType.Termux:
                sb.AppendLine("# export EDITOR=nano");
                sb.AppendLine("# export PAGER=less");
                break;
            default:
                sb.AppendLine("# export EDITOR=vim");
                sb.AppendLine("# export PAGER=less");
                break;
        }

        sb.AppendLine();
        sb.AppendLine("# ── Custom PATH additions ──");

        switch (os)
        {
            case Utils.OperatingSystemType.Windows:
                sb.AppendLine("# export PATH=$PATH;C:\\Users\\you\\bin");
                break;
            case Utils.OperatingSystemType.Termux:
                sb.AppendLine("# export PATH=$PATH:~/.local/bin");
                break;
            default:
                sb.AppendLine("# export PATH=$PATH:~/bin:~/.local/bin");
                break;
        }

        return sb.ToString();
    }
}
