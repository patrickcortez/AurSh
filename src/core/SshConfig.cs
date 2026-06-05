using System.Text.Json;
using System.Text.Json.Serialization;

namespace AurShell.Core;

// AOT-compatible JSON context for SSH host serialization
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<SshHost>))]
internal partial class SshJsonContext : JsonSerializerContext { }

// Represents a saved remote SSH host
public class SshHost
{
    public string Name { get; set; } = "";
    public string User { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string IdentityFile { get; set; } = "";
}

// Represents a discovered SSH key pair from ~/.ssh/
public class SshKeyEntry
{
    public string Name { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string PublicKeyPath { get; set; } = "";
    public string Type { get; set; } = "unknown";
    public string Fingerprint { get; set; } = "";
}

// Persists SSH host entries to ~/.aursh/ssh/hosts.json
public static class SshConfigStore
{

    public static List<SshHost> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new List<SshHost>();
            }

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<SshHost>();
            }

            return JsonSerializer.Deserialize(json, SshJsonContext.Default.ListSshHost) ?? new List<SshHost>();
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("aursh: aursh-ssh: hosts.json is corrupted, starting with empty list");
            return new List<SshHost>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-ssh: failed to load hosts: {ex.Message}");
            return new List<SshHost>();
        }
    }

    public static bool Save(string path, List<SshHost> hosts)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(hosts, SshJsonContext.Default.ListSshHost);
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-ssh: failed to save hosts: {ex.Message}");
            return false;
        }
    }

    public static string DefaultHostsPath =>
        Path.Combine(Utils.Platform.SshConfigDirectory, "hosts.json");
}

// Discovers SSH key pairs from the filesystem
public static class SshKeyDiscovery
{
    // Known private key filename patterns (no extension)
    private static readonly HashSet<string> KnownPublicExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pub"
    };

    public static List<SshKeyEntry> DiscoverKeys(string sshDir)
    {
        var keys = new List<SshKeyEntry>();

        try
        {
            if (!Directory.Exists(sshDir))
            {
                return keys;
            }

            // Find all .pub files, then match to private keys
            string[] pubFiles;
            try
            {
                pubFiles = Directory.GetFiles(sshDir, "*.pub");
            }
            catch (UnauthorizedAccessException)
            {
                return keys;
            }

            foreach (string pubFile in pubFiles)
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(pubFile);
                    string privateKeyPath = Path.Combine(sshDir, fileName);

                    // Skip if no matching private key
                    if (!File.Exists(privateKeyPath))
                    {
                        continue;
                    }

                    // Skip known_hosts, authorized_keys, config, etc.
                    if (IsNonKeyFile(fileName))
                    {
                        continue;
                    }

                    string keyType = ParseKeyType(pubFile);
                    string fingerprint = GetFingerprint(pubFile);

                    keys.Add(new SshKeyEntry
                    {
                        Name = fileName,
                        PrivateKeyPath = privateKeyPath,
                        PublicKeyPath = pubFile,
                        Type = keyType,
                        Fingerprint = fingerprint
                    });
                }
                catch (Exception)
                {
                    // Skip keys we can't read
                }
            }
        }
        catch (Exception)
        {
            // Return whatever we have so far
        }

        return keys;
    }

    private static bool IsNonKeyFile(string name)
    {
        return name.Equals("known_hosts", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("authorized_keys", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("config", StringComparison.OrdinalIgnoreCase);
    }

    // Parses the key type from the first token of a .pub file
    private static string ParseKeyType(string pubFilePath)
    {
        try
        {
            using var reader = new StreamReader(pubFilePath);
            string? firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return "unknown";
            }

            // Format: ssh-ed25519 AAAA... comment
            int spaceIdx = firstLine.IndexOf(' ');
            if (spaceIdx > 0)
            {
                string raw = firstLine.Substring(0, spaceIdx);
                // Strip "ssh-" prefix for cleaner display
                if (raw.StartsWith("ssh-", StringComparison.OrdinalIgnoreCase))
                {
                    return raw.Substring(4);
                }
                return raw;
            }

            return "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    // Gets the key fingerprint via ssh-keygen -lf
    private static string GetFingerprint(string pubFilePath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ssh-keygen", $"-lf \"{pubFilePath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                return "";
            }

            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);

            if (string.IsNullOrEmpty(output))
            {
                return "";
            }

            // Output format: 256 SHA256:xxx... comment (ED25519)
            // Return the SHA256:... part
            string[] parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return parts[1];
            }

            return output;
        }
        catch
        {
            return "";
        }
    }
}
