using System.Text;
using System.Text.Json;
using AurShell.Core;
using AurShell.Lua;

namespace AurShell.Plugins;

public class PluginManager
{
    private readonly ShellEnvironment _env;
    private readonly Executor _executor;
    private readonly List<LoadedPlugin> _plugins = new();
    private readonly Dictionary<string, LoadedPlugin> _commandMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _pluginsDir;

    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    public PluginManager(ShellEnvironment env, Executor executor)
    {
        _env = env;
        _executor = executor;
        _pluginsDir = GetPluginsDirectory();
    }

    public string PluginsDirectory => _pluginsDir;

    public void EnsurePluginsDir()
    {
        try
        {
            if (!Directory.Exists(_pluginsDir))
                Directory.CreateDirectory(_pluginsDir);
        }
        catch { }
    }

    public void LoadAll()
    {
        EnsurePluginsDir();
        if (!Directory.Exists(_pluginsDir)) return;

        foreach (string dir in Directory.GetDirectories(_pluginsDir))
        {
            string manifestPath = Path.Combine(dir, "plugin.json");
            if (File.Exists(manifestPath))
                LoadPlugin(manifestPath);
        }
    }

    public bool LoadPlugin(string manifestPath)
    {
        var manifest = PluginManifest.LoadFrom(manifestPath);
        if (manifest == null) return false;

        if (_plugins.Any(p => string.Equals(p.Manifest.Name, manifest.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"aursh: plugin '{manifest.Name}' is already loaded");
            return false;
        }

        string entryPath = Path.Combine(manifest.PluginDir, manifest.Entry);
        if (!File.Exists(entryPath))
        {
            Console.Error.WriteLine($"aursh: plugin '{manifest.Name}': entry file '{manifest.Entry}' not found");
            return false;
        }

        var interpreter = new LuaInterpreter();
        var plugin = new LoadedPlugin(manifest, interpreter);

        if (manifest.Type.ToLowerInvariant() == "lua")
        {
            RegisterAurshApi(plugin);
            RegisterRequire(plugin);

            try
            {
                string source = File.ReadAllText(entryPath);
                interpreter.Execute(source, manifest.Entry);
            }
            catch (LuaError ex)
            {
                Console.Error.WriteLine($"aursh: plugin '{manifest.Name}': {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"aursh: plugin '{manifest.Name}': unexpected error: {ex.Message}");
                return false;
            }
        }
        else if (manifest.Type.ToLowerInvariant() == "fsharp")
        {
            plugin = new LoadedPlugin(manifest, null);
        }

        _plugins.Add(plugin);

        if (manifest.Invokable)
        {
            if (manifest.Type.ToLowerInvariant() == "lua")
            {
                foreach (var kv in plugin.RegisteredCommands)
                    _commandMap[kv.Key] = plugin;
            }
            else if (manifest.Type.ToLowerInvariant() == "binary" || manifest.Type.ToLowerInvariant() == "fsharp")
            {
                foreach (var cmd in manifest.Commands)
                    _commandMap[cmd] = plugin;
            }
        }

        return true;
    }

    public bool UnloadPlugin(string name)
    {
        var plugin = _plugins.Find(p => string.Equals(p.Manifest.Name, name, StringComparison.OrdinalIgnoreCase));
        if (plugin == null) return false;

        foreach (var cmd in plugin.RegisteredCommands.Keys)
            _commandMap.Remove(cmd);

        foreach (var cmd in plugin.Manifest.Commands)
            _commandMap.Remove(cmd);

        _plugins.Remove(plugin);
        return true;
    }

    public void UnloadAll()
    {
        var names = _plugins.Select(p => p.Manifest.Name).ToList();
        foreach (var name in names)
        {
            UnloadPlugin(name);
        }
    }

    public bool IsPluginCommand(string name) => _commandMap.ContainsKey(name);

    public int ExecutePluginCommand(string name, List<string> args)
    {
        if (!_commandMap.TryGetValue(name, out var plugin)) return 127;

        if (plugin.Manifest.Type.ToLowerInvariant() == "fsharp")
            return ExecuteFSharpPlugin(plugin, args);

        if (!plugin.RegisteredCommands.TryGetValue(name, out var callback)) return 127;

        var luaArgs = new LuaTable();
        for (int i = 0; i < args.Count; i++)
            luaArgs.Set(LuaValue.FromNumber(i + 1), LuaValue.FromString(args[i]));

        try
        {
            var result = callback.Call(new[] { LuaValue.FromTable(luaArgs) });
            if (result.Length > 0 && result[0].Type == LuaType.Number)
                return (int)result[0].NumVal;
            return 0;
        }
        catch (LuaError ex)
        {
            Console.Error.WriteLine($"aursh: plugin '{plugin.Manifest.Name}': {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: plugin '{plugin.Manifest.Name}': {ex.Message}");
            return 1;
        }
    }

    public string? EvaluatePromptSegment(string token)
    {
        foreach (var plugin in _plugins)
        {
            if (plugin.RegisteredPromptSegments.TryGetValue(token, out var callback))
            {
                try
                {
                    var result = callback.Call(Array.Empty<LuaValue>());
                    if (result.Length > 0 && result[0].Type == LuaType.String)
                        return result[0].StrVal;
                    if (result.Length > 0 && result[0].Type == LuaType.Number)
                        return result[0].NumVal.ToString();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"aursh: plugin '{plugin.Manifest.Name}' prompt error: {ex.Message}");
                }
            }
        }
        return null;
    }

    /// <summary>
    /// If <paramref name="name"/> is an F# plugin, returns a list of
    /// arguments for <c>dotnet fsi "entry.fsx" -- "arg1" "arg2"</c>.
    /// Pipeline uses this to build a CommandNode that goes through
    /// <see cref="Pipeline.ExecuteExternal"/> with full BlackBox pipe
    /// capture, instead of the unmanaged child process path in
    /// <see cref="ExecuteFSharpPlugin"/>.
    /// Returns <c>null</c> if the command is not an F# plugin.
    /// </summary>
    public List<string>? BuildFSharpArgs(string name, List<string> args)
    {
        if (!_commandMap.TryGetValue(name, out var plugin)) return null;
        if (plugin.Manifest.Type.ToLowerInvariant() != "fsharp") return null;

        string entryPath = Path.Combine(plugin.Manifest.PluginDir, plugin.Manifest.Entry);
        if (!File.Exists(entryPath))
        {
            Console.Error.WriteLine($"aursh: plugin '{plugin.Manifest.Name}': entry file '{plugin.Manifest.Entry}' not found");
            return null;
        }

        var result = new List<string>();
        result.Add("fsi");
        result.Add(entryPath);
        if (args.Count > 0)
        {
            result.Add("--");
            foreach (string a in args)
                result.Add(a);
        }
        return result;
    }

    private int ExecuteFSharpPlugin(LoadedPlugin plugin, List<string> args)
    {
        string entryPath = Path.Combine(plugin.Manifest.PluginDir, plugin.Manifest.Entry);
        if (!File.Exists(entryPath))
        {
            Console.Error.WriteLine($"aursh: plugin '{plugin.Manifest.Name}': entry file '{plugin.Manifest.Entry}' not found");
            return 127;
        }

        var argBuilder = new StringBuilder();
        argBuilder.Append($"fsi \"{entryPath}\"");
        if (args.Count > 0)
        {
            argBuilder.Append(" -- ");
            foreach (string a in args)
            {
                argBuilder.Append('"');
                argBuilder.Append(a.Replace("\"", "\\\""));
                argBuilder.Append("\" ");
            }
        }

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", argBuilder.ToString().TrimEnd())
        {
            WorkingDirectory = _executor.WorkingDirectory,
            UseShellExecute = false
        };

        foreach (var kv in _env.Variables)
            psi.Environment[kv.Key] = kv.Value;

        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return 127;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: plugin '{plugin.Manifest.Name}': {ex.Message}");
            return 1;
        }
    }

    public string? GetPluginBinaryPath(string name)
    {
        if (_commandMap.TryGetValue(name, out var plugin))
        {
            if (plugin.Manifest.Type.ToLowerInvariant() == "binary")
            {
                return Path.Combine(plugin.Manifest.PluginDir, plugin.Manifest.Entry);
            }
        }
        return null;
    }

    public int InstallPlugin(string sourcePath)
    {
        EnsurePluginsDir();

        if (!Directory.Exists(sourcePath))
        {
            Console.Error.WriteLine($"aursh: plugin: '{sourcePath}' is not a directory");
            return 1;
        }

        string manifestPath = Path.Combine(sourcePath, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"aursh: plugin: no plugin.json found in '{sourcePath}'");
            return 1;
        }

        var manifest = PluginManifest.LoadFrom(manifestPath);
        if (manifest == null) return 1;

        string destDir = Path.Combine(_pluginsDir, manifest.Name);
        if (Directory.Exists(destDir))
        {
            Console.Error.WriteLine($"aursh: plugin '{manifest.Name}' already installed. Remove it first.");
            return 1;
        }

        try
        {
            CopyDirectory(sourcePath, destDir);
            Console.WriteLine($"Installed plugin '{manifest.Name}' v{manifest.Version}");

            string newManifest = Path.Combine(destDir, "plugin.json");
            if (LoadPlugin(newManifest))
                Console.WriteLine($"Plugin '{manifest.Name}' loaded successfully");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: plugin install failed: {ex.Message}");
            return 1;
        }
    }

    public int RemovePlugin(string name)
    {
        string pluginDir = Path.Combine(_pluginsDir, name);
        if (!Directory.Exists(pluginDir))
        {
            Console.Error.WriteLine($"aursh: plugin '{name}' not found");
            return 1;
        }

        UnloadPlugin(name);

        try
        {
            Directory.Delete(pluginDir, true);
            Console.WriteLine($"Removed plugin '{name}'");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: failed to remove plugin '{name}': {ex.Message}");
            return 1;
        }
    }

    public int UpdatePlugin(string name)
    {
        string pluginDir = Path.Combine(_pluginsDir, name);
        if (!Directory.Exists(pluginDir))
        {
            // Try case-insensitive directory lookup
            string? matchedDir = null;
            try
            {
                foreach (string dir in Directory.GetDirectories(_pluginsDir))
                {
                    string dirName = Path.GetFileName(dir);
                    if (string.Equals(dirName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedDir = dir;
                        break;
                    }
                }
            }
            catch { }

            if (matchedDir == null)
            {
                Console.Error.WriteLine($"aursh: plugin '{name}' not found in {_pluginsDir}");
                return 1;
            }
            pluginDir = matchedDir;
        }

        string manifestPath = Path.Combine(pluginDir, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"aursh: plugin '{name}': no plugin.json found in '{pluginDir}'");
            return 1;
        }

        // Read the manifest to get the canonical name before unloading
        var manifest = PluginManifest.LoadFrom(manifestPath);
        if (manifest == null)
        {
            Console.Error.WriteLine($"aursh: plugin '{name}': failed to parse plugin.json");
            return 1;
        }

        string entryPath = Path.Combine(manifest.PluginDir, manifest.Entry);
        if (!File.Exists(entryPath))
        {
            Console.Error.WriteLine($"aursh: plugin '{manifest.Name}': entry file '{manifest.Entry}' not found");
            return 1;
        }

        // Unload using the canonical name from the manifest for reliable matching
        string canonicalName = manifest.Name;
        bool wasLoaded = UnloadPlugin(canonicalName);

        if (!wasLoaded)
        {
            // Also try the user-supplied name in case it differs from canonical
            wasLoaded = UnloadPlugin(name);
        }

        // Reload the plugin from disk (picks up any file changes)
        if (LoadPlugin(manifestPath))
        {
            Console.WriteLine($"Updated plugin '{canonicalName}' v{manifest.Version}");
            return 0;
        }

        Console.Error.WriteLine($"aursh: failed to reload plugin '{canonicalName}' — check the entry file for errors");
        return 1;
    }

    public int InitPlugin(string name, string workingDirectory, string type = "lua")
    {
        string pluginDir = Path.Combine(workingDirectory, name);

        if (Directory.Exists(pluginDir))
        {
            Console.Error.WriteLine($"aursh: plugin '{name}' already exists");
            return 1;
        }

        try
        {
            Directory.CreateDirectory(pluginDir);

            string entryFile = type.ToLowerInvariant() == "fsharp" ? "init.fsx" : "init.lua";
            var manifest = new PluginManifest
            {
                Name = name,
                Version = "1.0.0",
                Author = Utils.Platform.UserName,
                Description = $"A custom AurShell plugin",
                Entry = entryFile,
                Type = type.ToLowerInvariant() == "fsharp" ? "fsharp" : "lua",
                Invokable = true,
                Commands = new List<string> { name }
            };

            string json = PluginManifest.Serialize(manifest);
            File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), json);

            if (type.ToLowerInvariant() == "fsharp")
            {
                string fsxTemplate = $@"open System
open System.Diagnostics

module Aursh =
    let print (s: string) = Console.WriteLine(s)

    let exec (cmd: string) =
        let shell = if Environment.OSVersion.Platform = PlatformID.Win32NT then ""cmd"" else ""sh""
        let flag = if Environment.OSVersion.Platform = PlatformID.Win32NT then ""/c"" else ""-c""
        let psi = ProcessStartInfo(shell, sprintf ""%s \""%s\"""" flag cmd)
        psi.UseShellExecute <- false
        use p = Process.Start(psi)
        if p <> null then
            p.WaitForExit()
            p.ExitCode
        else 1

    let get_env (var: string) =
        Environment.GetEnvironmentVariable(var)

    let get_cwd () =
        Environment.CurrentDirectory

    let register (name: string) (handler: string list -> int) =
        ()

let args = Environment.GetCommandLineArgs()

let pluginArgs =
    match args |> Array.tryFindIndex (fun a -> a = ""--"") with
    | Some index -> 
        args |> Array.skip (index + 1) |> Array.toList
    | None -> 
        []

Aursh.print $""Hello from {name} plugin!""

if not (List.isEmpty pluginArgs) then
    Aursh.print (sprintf ""Argument: %s"" (List.head pluginArgs))

Environment.ExitCode <- 0
";
                File.WriteAllText(Path.Combine(pluginDir, "init.fsx"), fsxTemplate);
                Console.WriteLine($"Created F# plugin '{name}' at {pluginDir}");
                Console.WriteLine($"  plugin.json  - manifest");
                Console.WriteLine($"  init.fsx     - entry point");
            }
            else
            {
                string luaTemplate = $@"aursh.register(""{name}"", function(args)
    aursh.print(""Hello from {name} plugin!"")
    if args[1] then
        aursh.print(""Argument: "" .. args[1])
    end
    return 0
end)

aursh.print(""[plugin] {name} loaded"")
";
                File.WriteAllText(Path.Combine(pluginDir, "init.lua"), luaTemplate);
                Console.WriteLine($"Created Lua plugin '{name}' at {pluginDir}");
                Console.WriteLine($"  plugin.json  - manifest");
                Console.WriteLine($"  init.lua     - entry point");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: plugin init failed: {ex.Message}");
            return 1;
        }
    }

    public int DebugPlugin(string fileOrName, string workingDirectory)
    {
        string filePath = Utils.FileSystem.ResolvePath(fileOrName, workingDirectory);
        if (!File.Exists(filePath))
        {
            string pluginDir = Path.Combine(_pluginsDir, fileOrName);
            if (Directory.Exists(pluginDir))
            {
                // Try both .fsx and .lua entry files
                filePath = Path.Combine(pluginDir, "init.fsx");
                if (!File.Exists(filePath))
                {
                    filePath = Path.Combine(pluginDir, "init.lua");
                    if (!File.Exists(filePath))
                    {
                        Console.Error.WriteLine($"aursh: debug: no init.fsx or init.lua found for plugin '{fileOrName}'");
                        return 1;
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"aursh: debug: file or plugin '{fileOrName}' not found");
                return 1;
            }
        }

        bool isFSharp = filePath.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isFSharp)
            {
                var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"fsi \"{filePath}\" --quiet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return 1;

                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0 && string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"Syntax OK: {filePath}");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"Syntax Error in {filePath}:\n{error}");
                    return 1;
                }
            }
            else
            {
                string source = File.ReadAllText(filePath);
                var lexer = new LuaLexer(source);
                var tokens = lexer.Tokenize();
                var parser = new LuaParser(tokens);
                parser.ParseBlock();

                Console.WriteLine($"Syntax OK: {filePath}");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Syntax Error in {filePath}:\n{ex.Message}");
            return 1;
        }
    }

    private void RegisterAurshApi(LoadedPlugin plugin)
    {
        var aursh = new LuaTable();
        var interp = plugin.Interpreter!;

        aursh.SetField("register", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length < 2 || args[0].Type != LuaType.String || args[1].Type != LuaType.Function)
                throw new LuaError("aursh.register(name, func) requires a string and function");
            plugin.RegisteredCommands[args[0].StrVal!] = args[1].FuncVal!;
            return Array.Empty<LuaValue>();
        })));

        aursh.SetField("register_prompt", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length < 2 || args[0].Type != LuaType.String || args[1].Type != LuaType.Function)
                throw new LuaError("aursh.register_prompt(name, func) requires a string and function");
            plugin.RegisteredPromptSegments[args[0].StrVal!] = args[1].FuncVal!;
            return Array.Empty<LuaValue>();
        })));


        aursh.SetField("print", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            Console.WriteLine(string.Join("\t", args.Select(a => a.AsString())));
            return Array.Empty<LuaValue>();
        })));

        aursh.SetField("print_color", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length < 4) { Console.WriteLine(args.Length > 0 ? args[0].AsString() : ""); return Array.Empty<LuaValue>(); }
            string text = args[0].AsString();
            int r = (int)args[1].AsNumber(), g = (int)args[2].AsNumber(), b = (int)args[3].AsNumber();
            Console.Write(Utils.Ansi.FgRgb(r, g, b));
            Console.Write(text);
            Console.WriteLine(Utils.Ansi.Reset);
            return Array.Empty<LuaValue>();
        })));

        aursh.SetField("get_env", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length == 0) return new[] { LuaValue.Nil };
            string? val = _env.Get(args[0].AsString());
            return new[] { val != null ? LuaValue.FromString(val) : LuaValue.Nil };
        })));

        aursh.SetField("set_env", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length >= 2)
                _env.Set(args[0].AsString(), args[1].AsString());
            return Array.Empty<LuaValue>();
        })));

        aursh.SetField("exec", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length == 0) return new[] { LuaValue.FromNumber(0) };
            try
            {
                int exit = _executor.Execute(args[0].AsString());
                return new[] { LuaValue.FromNumber(exit) };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"aursh.exec: {ex.Message}");
                return new[] { LuaValue.FromNumber(1) };
            }
        })));

        aursh.SetField("get_cwd", LuaValue.FromFunc(new LuaCSharpFunc(_ =>
            new[] { LuaValue.FromString(_executor.WorkingDirectory) })));

        aursh.SetField("get_os", LuaValue.FromFunc(new LuaCSharpFunc(_ =>
            new[] { LuaValue.FromString(Utils.Platform.OsName) })));

        aursh.SetField("get_user", LuaValue.FromFunc(new LuaCSharpFunc(_ =>
            new[] { LuaValue.FromString(Utils.Platform.UserName) })));

        aursh.SetField("get_host", LuaValue.FromFunc(new LuaCSharpFunc(_ =>
            new[] { LuaValue.FromString(Utils.Platform.HostName) })));

        aursh.SetField("set_alias", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length >= 2) _env.SetAlias(args[0].AsString(), args[1].AsString());
            return Array.Empty<LuaValue>();
        })));

        aursh.SetField("get_alias", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length == 0) return new[] { LuaValue.Nil };
            string? alias = _env.GetAlias(args[0].AsString());
            return new[] { alias != null ? LuaValue.FromString(alias) : LuaValue.Nil };
        })));

        interp.SetGlobal("aursh", LuaValue.FromTable(aursh));
    }

    private void RegisterRequire(LoadedPlugin plugin)
    {
        var loaded = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        plugin.Interpreter.SetGlobalFunc("require", args =>
        {
            if (args.Length == 0) throw new LuaError("require expects a module name");
            string modName = args[0].AsString();

            if (loaded.TryGetValue(modName, out var cached))
                return new[] { cached };

            string filePath = Path.Combine(plugin.Manifest.PluginDir, modName.Replace('.', Path.DirectorySeparatorChar) + ".lua");
            if (!File.Exists(filePath))
                throw new LuaError($"module '{modName}' not found at {filePath}");

            string source = File.ReadAllText(filePath);
            var lexer = new LuaLexer(source);
            var tokens = lexer.Tokenize();
            var parser = new LuaParser(tokens);
            var block = parser.ParseBlock();

            LuaValue result = LuaValue.True;
            try { plugin.Interpreter.Execute(source, modName + ".lua"); }
            catch (LuaError) { throw; }

            loaded[modName] = result;
            return new[] { result };
        });
    }

    private static string GetPluginsDirectory()
    {
        return Path.Combine(Utils.Platform.HomeDirectory, ".aursh", "plugins");
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(source))
        {
            string destFile = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (string dir in Directory.GetDirectories(source))
        {
            string destDir = Path.Combine(dest, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }
}

public class LoadedPlugin
{
    public PluginManifest Manifest { get; }
    public LuaInterpreter? Interpreter { get; }
    public Dictionary<string, LuaCallable> RegisteredCommands { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LuaCallable> RegisteredPromptSegments { get; } = new(StringComparer.OrdinalIgnoreCase);

    public LoadedPlugin(PluginManifest manifest, LuaInterpreter? interpreter)
    {
        Manifest = manifest;
        Interpreter = interpreter;
    }
}
