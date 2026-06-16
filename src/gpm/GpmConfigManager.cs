using System;
using System.Collections.Generic;
using System.IO;

namespace AurShell.Gpm;

public class GpmConfigManager
{
    private readonly string _configDirectory;
    private readonly string _configFile;

    public GpmConfigManager()
    {
        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _configDirectory = Path.Combine(homeDir, ".gpm");
        _configFile = Path.Combine(_configDirectory, "remotes.con");

        EnsureConfigExists();
    }

    private void EnsureConfigExists()
    {
        try
        {
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }
            if (!File.Exists(_configFile))
            {
                File.WriteAllText(_configFile, "");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"gpm: Failed to initialize configuration directory: {ex.Message}");
        }
    }

    public Dictionary<string, string> GetInstalledRepos()
    {
        var repos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            if (!File.Exists(_configFile)) return repos;

            string[] lines = File.ReadAllLines(_configFile);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    string name = trimmed.Substring(0, eqIdx).Trim('\"', ' ');
                    string path = trimmed.Substring(eqIdx + 1).Trim('\"', ' ');
                    repos[name] = path;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"gpm: Failed to read configuration: {ex.Message}");
        }

        return repos;
    }

    public void AddRepo(string name, string path)
    {
        try
        {
            var repos = GetInstalledRepos();
            repos[name] = path;
            SaveRepos(repos);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"gpm: Failed to save repository config: {ex.Message}");
        }
    }

    public void RemoveRepo(string name)
    {
        try
        {
            var repos = GetInstalledRepos();
            if (repos.Remove(name))
            {
                SaveRepos(repos);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"gpm: Failed to remove repository config: {ex.Message}");
        }
    }

    private void SaveRepos(Dictionary<string, string> repos)
    {
        var lines = new List<string>();
        foreach (var kvp in repos)
        {
            lines.Add($"\"{kvp.Key}\"=\"{kvp.Value}\"");
        }
        File.WriteAllLines(_configFile, lines);
    }
}
