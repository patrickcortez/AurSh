using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using AurShell.Utils;

namespace AurShell.Music;

public class TrackInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public uint Year { get; set; }
    public string Genre { get; set; } = "";
    public uint TrackNumber { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public double Duration { get; set; }
    public string Path { get; set; } = "";
    public bool HasCover { get; set; }
}

public class MusicScanner
{
    public string MusicDirectory { get; private set; } = "";
    public List<TrackInfo> Tracks { get; private set; } = new List<TrackInfo>();
    private FileSystemWatcher? _watcher;
    private DateTime _lastScanTime = DateTime.MinValue;

    public void LoadConfig()
    {
        string homeDir = Platform.HomeDirectory;
        string configPath = Path.Combine(homeDir, ".aursh", "music.con");

        if (!File.Exists(configPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, "dir=\"\"");
            Console.WriteLine($"[Music] Created config at {configPath}. Please set your music directory.");
            return;
        }

        string[] lines = File.ReadAllLines(configPath);
        foreach (string line in lines)
        {
            if (line.StartsWith("dir="))
            {
                MusicDirectory = line.Substring(4).Trim('"', ' ', '\'');
            }
        }
        SetupWatcher();
    }

    public void SetConfig(string dir)
    {
        string homeDir = Platform.HomeDirectory;
        string configPath = Path.Combine(homeDir, ".aursh", "music.con");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, $"dir=\"{dir}\"");
        LoadConfig();
        Scan();
    }

    public void SetupWatcher()
    {
        if (_watcher != null)
        {
            _watcher.Dispose();
            _watcher = null;
        }

        if (string.IsNullOrEmpty(MusicDirectory) || !Directory.Exists(MusicDirectory)) return;

        _watcher = new FileSystemWatcher(MusicDirectory);
        _watcher.IncludeSubdirectories = true;
        _watcher.Filter = "*.*";
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if ((DateTime.Now - _lastScanTime).TotalMilliseconds < 1000) return;
        _lastScanTime = DateTime.Now;

        if (IsMusicFile(e.FullPath))
        {
            Console.WriteLine($"[Music] Detected file change. Rescanning...");
            Scan();
        }
    }

    private bool IsMusicFile(string path)
    {
        return path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    public void Scan()
    {
        Tracks.Clear();
        if (string.IsNullOrEmpty(MusicDirectory) || !Directory.Exists(MusicDirectory))
        {
            Console.WriteLine("[Music] Invalid or missing directory in ~/.aursh/music.con");
            return;
        }

        Console.WriteLine($"[Music] Scanning directory: {MusicDirectory} ...");

        var files = Directory.EnumerateFiles(MusicDirectory, "*.*", SearchOption.AllDirectories)
            .Where(s => s.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                        s.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                        s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                        s.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                        s.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            try
            {
                using var tagFile = TagLib.File.Create(file);
                var tag = tagFile.Tag;

                string id = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file))
                    .Replace("+", "-").Replace("/", "_").TrimEnd('=');

                var track = new TrackInfo
                {
                    Id = id,
                    Title = string.IsNullOrEmpty(tag.Title) ? Path.GetFileNameWithoutExtension(file) : tag.Title,
                    Artist = tag.FirstPerformer ?? tag.FirstAlbumArtist ?? "Unknown Artist",
                    Album = tag.Album ?? "Unknown Album",
                    Year = tag.Year,
                    Genre = tag.FirstGenre ?? "Unknown Genre",
                    TrackNumber = tag.Track,
                    Bitrate = tagFile.Properties.AudioBitrate,
                    SampleRate = tagFile.Properties.AudioSampleRate,
                    Channels = tagFile.Properties.AudioChannels,
                    Duration = tagFile.Properties.Duration.TotalSeconds,
                    Path = file,
                    HasCover = tag.Pictures.Length > 0
                };

                Tracks.Add(track);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Music] Error reading {file}: {ex.Message}");
            }
        }

        Console.WriteLine($"[Music] Found {Tracks.Count} tracks.");
    }

    public byte[]? GetCoverArt(string id)
    {
        var track = Tracks.FirstOrDefault(t => t.Id == id);
        if (track == null || !track.HasCover) return null;

        try
        {
            using var tagFile = TagLib.File.Create(track.Path);
            var tag = tagFile.Tag;
            if (tag.Pictures.Length > 0)
            {
                return tag.Pictures[0].Data.Data;
            }
        }
        catch { }
        return null;
    }

    public bool DeleteTrack(string id)
    {
        var track = Tracks.FirstOrDefault(t => t.Id == id);
        if (track != null)
        {
            try
            {
                if (File.Exists(track.Path))
                {
                    File.Delete(track.Path);
                }
                Tracks.Remove(track);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Music] Error deleting file {track.Path}: {ex.Message}");
            }
        }
        return false;
    }
}
