using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AurShell.Music;

public class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverArt { get; set; } = "";
    public List<string> TrackIds { get; set; } = new List<string>();
}

public class UserData
{
    public List<string> LikedTracks { get; set; } = new List<string>();
    public List<Playlist> Playlists { get; set; } = new List<Playlist>();
}

public class UserDataManager
{
    private string DataPath { get; }
    public UserData Data { get; private set; }

    public UserDataManager()
    {
        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DataPath = Path.Combine(homeDir, ".aursh", "music_data.json");
        Data = new UserData();
        LoadData();
    }

    public void LoadData()
    {
        try
        {
            if (File.Exists(DataPath))
            {
                string json = File.ReadAllText(DataPath);
                var loaded = JsonSerializer.Deserialize(json, MusicJsonContext.Default.UserData);
                if (loaded != null)
                {
                    Data = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] Error loading user data: {ex.Message}");
        }
    }

    public void SaveData()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
            string json = JsonSerializer.Serialize(Data, MusicJsonContext.Default.UserData);
            File.WriteAllText(DataPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] Error saving user data: {ex.Message}");
        }
    }

    public void ToggleLike(string trackId)
    {
        if (Data.LikedTracks.Contains(trackId))
        {
            Data.LikedTracks.Remove(trackId);
        }
        else
        {
            Data.LikedTracks.Add(trackId);
        }
        SaveData();
    }

    public Playlist CreatePlaylist(string name)
    {
        var playlist = new Playlist { Name = name };
        Data.Playlists.Add(playlist);
        SaveData();
        return playlist;
    }

    public bool AddToPlaylist(string playlistId, string trackId)
    {
        var playlist = Data.Playlists.Find(p => p.Id == playlistId);
        if (playlist != null)
        {
            if (!playlist.TrackIds.Contains(trackId))
            {
                playlist.TrackIds.Add(trackId);
                SaveData();
            }
            return true;
        }
        return false;
    }

    public bool UpdatePlaylist(string playlistId, string name, string description, string coverArt)
    {
        var playlist = Data.Playlists.Find(p => p.Id == playlistId);
        if (playlist != null)
        {
            playlist.Name = name ?? playlist.Name;
            playlist.Description = description ?? playlist.Description;
            playlist.CoverArt = coverArt ?? playlist.CoverArt;
            SaveData();
            return true;
        }
        return false;
    }

    public bool RemoveFromPlaylist(string playlistId, string trackId)
    {
        var playlist = Data.Playlists.Find(p => p.Id == playlistId);
        if (playlist != null)
        {
            if (playlist.TrackIds.Remove(trackId))
            {
                SaveData();
            }
            return true;
        }
        return false;
    }

    public void PurgeTrack(string trackId)
    {
        bool changed = false;
        if (Data.LikedTracks.Remove(trackId)) changed = true;
        
        foreach (var playlist in Data.Playlists)
        {
            if (playlist.TrackIds.Remove(trackId)) changed = true;
        }

        if (changed) SaveData();
    }
}
