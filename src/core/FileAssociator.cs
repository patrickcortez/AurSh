using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AurShell.Core;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class FileAssociatorJsonContext : JsonSerializerContext
{
}

public class FileAssociator
{
    private readonly string _configPath;
    private Dictionary<string, string> _associations;

    public FileAssociator()
    {
        string profileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string aurshDir = Path.Combine(profileDir, ".aursh");
        _configPath = Path.Combine(aurshDir, "associations.json");
        _associations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(aurshDir))
        {
            Directory.CreateDirectory(aurshDir);
        }

        Load();
    }

    public void Load()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                string json = File.ReadAllText(_configPath);
                var loaded = JsonSerializer.Deserialize(json, FileAssociatorJsonContext.Default.DictionaryStringString);
                if (loaded != null)
                {
                    _associations = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"aursh: failed to load associations: {ex.Message}");
            }
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_associations, FileAssociatorJsonContext.Default.DictionaryStringString);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: failed to save associations: {ex.Message}");
        }
    }

    public string? GetAssociation(string extension)
    {
        if (!extension.StartsWith("."))
        {
            extension = "." + extension;
        }

        if (_associations.TryGetValue(extension, out string? template))
        {
            return template;
        }

        return null;
    }

    public void SetAssociation(string extension, string template)
    {
        if (!extension.StartsWith("."))
        {
            extension = "." + extension;
        }

        _associations[extension] = template;
        Save();
    }

    public bool RemoveAssociation(string extension)
    {
        if (!extension.StartsWith("."))
        {
            extension = "." + extension;
        }

        if (_associations.Remove(extension))
        {
            Save();
            return true;
        }
        return false;
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        return _associations;
    }
}
