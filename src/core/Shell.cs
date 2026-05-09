using AurShell.Plugins;


namespace AurShell.Core;

public class Shell
{
    private readonly ShellEnvironment _env;
    private readonly Executor _executor;
    private readonly History _history;
    private readonly Prompt _prompt;
    private readonly InputHandler _inputHandler;
    private readonly PluginManager _pluginManager;
    private bool _running;
    private bool _interrupted;

    public ShellEnvironment Environment => _env;
    public Executor Executor => _executor;
    public History History => _history;
    public PluginManager PluginManager => _pluginManager;
    public string WorkingDirectory => _executor.WorkingDirectory;

    public Shell()
    {
        _env = new ShellEnvironment();
        _env.ImportFromSystem();

        string startDir;
        try
        {
            startDir = Directory.GetCurrentDirectory();
        }
        catch
        {
            startDir = Utils.Platform.HomeDirectory;
        }

        _executor = new Executor(_env, startDir);
        _history = new History(Utils.Platform.HistoryFilePath);
        _prompt = new Prompt(_env);
        _inputHandler = new InputHandler(_history, _env);
        _pluginManager = new PluginManager(_env, _executor);
        _env.PluginManager = _pluginManager;
        _running = true;


        InitializeDefaultVariables();
        RcLoader.CreateDefaultRc(Utils.Platform.RcFilePath);
        LoadRc();
        LoadPlugins();
    }

    public Shell(ShellEnvironment env, string workingDirectory)
    {
        _env = env;
        _executor = new Executor(_env, workingDirectory);
        _history = new History(Utils.Platform.HistoryFilePath);
        _prompt = new Prompt(_env);
        _inputHandler = new InputHandler(_history, _env);
        _pluginManager = new PluginManager(_env, _executor);
        _env.PluginManager = _pluginManager;
        _running = true;

        InitializeDefaultVariables();
        RcLoader.CreateDefaultRc(Utils.Platform.RcFilePath);
        LoadRc();
        LoadPlugins();
    }


    public void Run()
    {
        _running = true;
        _interrupted = false;

        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            Console.Write(Utils.Ansi.Reset);
            Banner.Print(_env);


            while (_running)
            {
                _interrupted = false;
                _env.Set("PWD", _executor.WorkingDirectory);

                NotifyFinishedJobs();

                string promptText = _prompt.Render(_executor.WorkingDirectory, _env.LastExitCode);
                Console.Write(Utils.Ansi.SetTitle($"Aursh: {Utils.Platform.ShortenPath(_executor.WorkingDirectory)}"));

                string? line = _inputHandler.ReadLine(promptText);

                if (_interrupted)
                {
                    _env.LastExitCode = 130;
                    continue;
                }

                if (line == null)
                {
                    Console.WriteLine("exit");
                    break;
                }

                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                _history.Add(trimmed);

                try
                {
                    int exitCode = _executor.Execute(trimmed);
                    _env.LastExitCode = exitCode;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"aursh: error: {ex.Message}");
                    _env.LastExitCode = 1;
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            Console.Write(Utils.Ansi.Reset);
            Console.Write(Utils.Ansi.CursorShow);
        }
    }

    public int ExecuteCommand(string input)
    {
        int result = _executor.Execute(input);
        _env.LastExitCode = result;
        return result;
    }

    public void Stop()
    {
        _running = false;
    }

    public int RunScript(string filePath, string[] args)
    {
        var runner = new ScriptRunner(_env, _executor);
        return runner.RunFile(filePath, args);
    }

    private void LoadRc()
    {
        var rcLoader = new RcLoader(_env, _executor);
        rcLoader.Load();
        rcLoader.LoadConfigDir();
    }

    private void LoadPlugins()
    {
        try { _pluginManager.LoadAll(); }
        catch (Exception ex) { Console.Error.WriteLine($"aursh: plugin load error: {ex.Message}"); }
    }

    private void InitializeDefaultVariables()
    {
        _env.Set("SHELL", "aursh");
        _env.Set("AURSH_VERSION", "1.2.0");
        _env.Set("PWD", _executor.WorkingDirectory);
        _env.Set("HOME", Utils.Platform.HomeDirectory);
        _env.Set("USER", Utils.Platform.UserName);
        _env.Set("HOSTNAME", Utils.Platform.HostName);
        _env.Set("OS", Utils.Platform.OsName);
        _env.Set("TERM", System.Environment.GetEnvironmentVariable("TERM") ?? "xterm-256color");

        string? path = System.Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
            _env.Set("PATH", path);
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _interrupted = true;
    }

    private void NotifyFinishedJobs()
    {
        var finished = _env.Jobs.CollectFinished();
        foreach (var job in finished)
        {
            string stateStr = job.State switch
            {
                JobState.Done => "Done",
                JobState.Killed => "Killed",
                _ => "Exited"
            };

            string dimFg = Utils.Ansi.FgRgb(100, 100, 130);
            string statusFg = job.ExitCode == 0
                ? Utils.Ansi.FgRgb(100, 230, 150)
                : Utils.Ansi.FgRgb(255, 130, 100);

            Console.WriteLine(
                $"{dimFg}[{job.Id}]{Utils.Ansi.Reset}  {statusFg}{stateStr}{Utils.Ansi.Reset}  {dimFg}{job.Command}{Utils.Ansi.Reset}"
            );
        }
    }
}