using System;
using System.Collections.Generic;
using System.IO;

namespace AurShell.Grm;

public class GrmConfigManager
{
    private readonly string _configDirectory;
    private readonly string _configFile;
    private readonly string _trustedFile;

    public GrmConfigManager()
    {
        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _configDirectory = Path.Combine(homeDir, ".grm");
        _configFile = Path.Combine(_configDirectory, "remotes.con");
        _trustedFile = Path.Combine(_configDirectory, "trusted.con");

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
            if (!File.Exists(_trustedFile))
            {
                File.WriteAllText(_trustedFile, "");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"grm: Failed to initialize configuration directory: {ex.Message}");
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

    public bool IsRepoTrusted(string repoIdentifier)
    {
        try
        {
            if (!File.Exists(_trustedFile)) return false;
            string[] lines = File.ReadAllLines(_trustedFile);
            foreach (var line in lines)
            {
                if (line.Trim().Equals(repoIdentifier, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    public void TrustRepo(string repoIdentifier)
    {
        if (IsRepoTrusted(repoIdentifier)) return;
        try
        {
            File.AppendAllLines(_trustedFile, new[] { repoIdentifier });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"grm: Failed to save trusted config: {ex.Message}");
        }
    }
}
