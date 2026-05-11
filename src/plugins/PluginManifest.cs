using System.Text.Json;
using System.Text.Json.Serialization;

namespace AurShell.Plugins;

[JsonSerializable(typeof(PluginManifest))]
internal partial class PluginJsonContext : JsonSerializerContext { }

public class PluginManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "init.lua";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "lua";

    [JsonPropertyName("invokable")]
    public bool Invokable { get; set; } = true;

    [JsonPropertyName("commands")]
    public List<string> Commands { get; set; } = new();

    [JsonIgnore]
    public string PluginDir { get; set; } = "";

    public static PluginManifest? LoadFrom(string pluginJsonPath)
    {
        try
        {
            string json = File.ReadAllText(pluginJsonPath);
            var manifest = JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginManifest);
            if (manifest != null)
                manifest.PluginDir = Path.GetDirectoryName(pluginJsonPath) ?? "";
            return manifest;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: plugin: failed to parse {pluginJsonPath}: {ex.Message}");
            return null;
        }
    }

    public static string Serialize(PluginManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, PluginJsonContext.Default.PluginManifest);
    }
}
