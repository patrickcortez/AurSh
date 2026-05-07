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

        if (_plugins.Any(p => p.Manifest.Name == manifest.Name))
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

        _plugins.Add(plugin);

        foreach (var kv in plugin.RegisteredCommands)
            _commandMap[kv.Key] = plugin;

        return true;
    }

    public bool UnloadPlugin(string name)
    {
        var plugin = _plugins.Find(p => string.Equals(p.Manifest.Name, name, StringComparison.OrdinalIgnoreCase));
        if (plugin == null) return false;

        foreach (var cmd in plugin.RegisteredCommands.Keys)
            _commandMap.Remove(cmd);

        _plugins.Remove(plugin);
        return true;
    }

    public bool IsPluginCommand(string name) => _commandMap.ContainsKey(name);

    public int ExecutePluginCommand(string name, List<string> args)
    {
        if (!_commandMap.TryGetValue(name, out var plugin)) return 127;
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

    public int InitPlugin(string name)
    {
        EnsurePluginsDir();
        string pluginDir = Path.Combine(_pluginsDir, name);

        if (Directory.Exists(pluginDir))
        {
            Console.Error.WriteLine($"aursh: plugin '{name}' already exists");
            return 1;
        }

        try
        {
            Directory.CreateDirectory(pluginDir);

            var manifest = new PluginManifest
            {
                Name = name,
                Version = "1.0.0",
                Author = Utils.Platform.UserName,
                Description = $"A custom AurShell plugin",
                Entry = "init.lua",
                Commands = new List<string> { name }
            };

            string json = PluginManifest.Serialize(manifest);
            File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), json);

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

            Console.WriteLine($"Created plugin '{name}' at {pluginDir}");
            Console.WriteLine($"  plugin.json  - manifest");
            Console.WriteLine($"  init.lua     - entry point");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: plugin init failed: {ex.Message}");
            return 1;
        }
    }

    private void RegisterAurshApi(LoadedPlugin plugin)
    {
        var aursh = new LuaTable();
        var interp = plugin.Interpreter;

        aursh.SetField("register", LuaValue.FromFunc(new LuaCSharpFunc(args =>
        {
            if (args.Length < 2 || args[0].Type != LuaType.String || args[1].Type != LuaType.Function)
                throw new LuaError("aursh.register(name, func) requires a string and function");
            plugin.RegisteredCommands[args[0].StrVal!] = args[1].FuncVal!;
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
        string binaryDir = AppContext.BaseDirectory;
        return Path.Combine(binaryDir, "plugins");
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
    public LuaInterpreter Interpreter { get; }
    public Dictionary<string, LuaCallable> RegisteredCommands { get; } = new(StringComparer.OrdinalIgnoreCase);

    public LoadedPlugin(PluginManifest manifest, LuaInterpreter interpreter)
    {
        Manifest = manifest;
        Interpreter = interpreter;
    }
}
