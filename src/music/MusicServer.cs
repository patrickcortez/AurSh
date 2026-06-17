using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Serialization;

namespace AurShell.Music;

public class StatusResponse
{
    public bool Configured { get; set; }
    public string Directory { get; set; } = "";
    public int TrackCount { get; set; }
}

public class PlaylistUpdateRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CoverArt { get; set; }
    public string? BannerArt { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(System.Collections.Generic.List<TrackInfo>))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(UserData))]
[JsonSerializable(typeof(Playlist))]
[JsonSerializable(typeof(PlaylistUpdateRequest))]
internal partial class MusicJsonContext : JsonSerializerContext { }

public static class MusicServer
{
    public static void Start(string[] args)
    {
        Console.WriteLine("[Music] Initializing AurSh-Music Daemon...");
        var scanner = new MusicScanner();
        scanner.LoadConfig();
        scanner.Scan();

        var userDataManager = new UserDataManager();

        ExtractFrontendResources();

        var builder = WebApplication.CreateSlimBuilder(args);
        
        // Listen on 7007
        builder.WebHost.UseUrls("http://127.0.0.1:7007");

        // Add AOT JSON serialization context
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, MusicJsonContext.Default);
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        // Add CORS
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            });
        });

        var app = builder.Build();

        app.UseCors();

        app.MapGet("/api/tracks", () =>
        {
            return Results.Json(scanner.Tracks, MusicJsonContext.Default.ListTrackInfo);
        });

        app.MapGet("/api/status", () =>
        {
            var res = new StatusResponse
            {
                Configured = !string.IsNullOrEmpty(scanner.MusicDirectory) && Directory.Exists(scanner.MusicDirectory),
                Directory = scanner.MusicDirectory,
                TrackCount = scanner.Tracks.Count
            };
            return Results.Json(res, MusicJsonContext.Default.StatusResponse);
        });

        app.MapPost("/api/config", async (HttpContext context) =>
        {
            var reader = new StreamReader(context.Request.Body);
            var dir = await reader.ReadToEndAsync();
            scanner.SetConfig(dir);
            return Results.Ok();
        });

        app.MapGet("/api/cover/{id}", (string id) =>
        {
            var coverData = scanner.GetCoverArt(id);
            if (coverData != null)
            {
                return Results.File(coverData, "image/jpeg");
            }
            return Results.NotFound();
        });

        app.MapGet("/api/stream/{id}", (string id) =>
        {
            var track = scanner.Tracks.Find(t => t.Id == id);
            if (track == null || !File.Exists(track.Path))
                return Results.NotFound();

            return Results.File(track.Path, "audio/mpeg", enableRangeProcessing: true);
        });

        app.MapGet("/api/userdata", () =>
        {
            return Results.Json(userDataManager.Data, MusicJsonContext.Default.UserData);
        });

        app.MapPost("/api/like/{id}", (string id) =>
        {
            userDataManager.ToggleLike(id);
            return Results.Json(userDataManager.Data, MusicJsonContext.Default.UserData);
        });

        app.MapPost("/api/playlist", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var name = await reader.ReadToEndAsync();
            userDataManager.CreatePlaylist(name);
            return Results.Json(userDataManager.Data, MusicJsonContext.Default.UserData);
        });

        app.MapPost("/api/playlist/{playlistId}/add/{trackId}", (string playlistId, string trackId) =>
        {
            if (userDataManager.AddToPlaylist(playlistId, trackId))
            {
                return Results.Json(userDataManager.Data, MusicJsonContext.Default.UserData);
            }
            return Results.NotFound();
        });

        app.MapDelete("/api/playlist/{playlistId}/remove/{trackId}", (string playlistId, string trackId) =>
        {
            if (userDataManager.RemoveFromPlaylist(playlistId, trackId))
            {
                return Results.Json(userDataManager.Data, MusicJsonContext.Default.UserData);
            }
            return Results.NotFound();
        });

        app.MapDelete("/api/playlist/{playlistId}", (string playlistId) =>
        {
            if (userDataManager.DeletePlaylist(playlistId))
            {
                return Results.Json(userDataManager.Data, MusicJsonContext.Default.UserData);
            }
            return Results.NotFound();
        });

        app.MapPut("/api/playlist/{playlistId}", async (string playlistId, HttpContext context) =>
        {
            var req = await System.Text.Json.JsonSerializer.DeserializeAsync(context.Request.Body, MusicJsonContext.Default.PlaylistUpdateRequest);
            if (req != null)
            {
                if (userDataManager.UpdatePlaylist(playlistId, req.Name, req.Description, req.CoverArt, req.BannerArt))
                {
                    return Results.Json(userDataManager.Data, MusicJsonContext.Default.UserData);
                }
            }
            return Results.BadRequest();
        });

        app.MapDelete("/api/tracks/{id}", (string id) =>
        {
            if (scanner.DeleteTrack(id))
            {
                userDataManager.PurgeTrack(id);
                return Results.Ok();
            }
            return Results.NotFound();
        });

        app.MapPost("/api/upload", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest();
            var form = await request.ReadFormAsync();
            foreach (var file in form.Files)
            {
                if (file.Length > 0 && !string.IsNullOrEmpty(scanner.MusicDirectory))
                {
                    var filePath = Path.Combine(scanner.MusicDirectory, file.FileName);
                    using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);
                }
            }
            scanner.Scan();
            return Results.Json(scanner.Tracks, MusicJsonContext.Default.ListTrackInfo);
        });

        string uiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aursh", "music-ui");
        
        if (Directory.Exists(uiPath))
        {
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(uiPath)
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(uiPath),
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                    ctx.Context.Response.Headers.Append("Expires", "-1");
                }
            });
        }

        Console.WriteLine("[Music] Server is starting on http://127.0.0.1:7007");
        Console.WriteLine("[Music] Press Ctrl+C to stop.");

        app.Run();
    }

    private static void ExtractFrontendResources()
    {
        string uiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aursh", "music-ui");
        if (Directory.Exists(uiPath))
        {
            try { Directory.Delete(uiPath, true); } catch { }
        }
        Directory.CreateDirectory(uiPath);

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("AurShell.Resources.music_ui.zip");
        if (stream != null)
        {
            using var archive = new System.IO.Compression.ZipArchive(stream);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string destPath = Path.Combine(uiPath, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }
    }
}
