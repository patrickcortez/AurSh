using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AurShell.Utils;

namespace AurShell.Core;

public static class AurshNetTransfer
{
    private const int DefaultPort = 15333;
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static bool _isRunning = false;

    public static string DownloadDirectory
    {
        get
        {
            string dir = Path.Combine(Platform.HomeDirectory, "Downloads", "AurshNet");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }
    }

    public static void StartReceiverDaemon()
    {
        if (_isRunning)
        {
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, DefaultPort);
            _listener.Start();
            _isRunning = true;
            _cts = new CancellationTokenSource();

            Task.Run(() => AcceptClientsAsync(_cts.Token), _cts.Token);
        }
        catch (SocketException)
        {
            // Port already in use. Likely another aursh instance is already running the daemon.
            // We just silently fail and let the first instance handle incoming transfers.
        }
        catch (Exception)
        {
            // Ignore other startup errors for the daemon
        }
    }

    public static void StopReceiverDaemon()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cts?.Cancel();
        _listener?.Stop();
    }

    private static async Task AcceptClientsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClient(client, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Continue accepting
            }
        }
    }

    private static void HandleClient(TcpClient client, CancellationToken token)
    {
        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            using (BinaryReader reader = new BinaryReader(stream))
            {
                string magic = reader.ReadString();
                if (magic != "AURSH_NET_V1")
                {
                    return; // Unknown protocol
                }

                long fileCount = reader.ReadInt64();
                
                for (long i = 0; i < fileCount; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    string relativePath = reader.ReadString();
                    long fileSize = reader.ReadInt64();

                    string targetPath = Path.Combine(DownloadDirectory, relativePath);
                    string? targetDir = Path.GetDirectoryName(targetPath);
                    
                    if (targetDir != null && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        while (totalRead < fileSize)
                        {
                            int toRead = (int)Math.Min(buffer.Length, fileSize - totalRead);
                            int read = stream.Read(buffer, 0, toRead);
                            if (read == 0)
                            {
                                throw new EndOfStreamException();
                            }
                            fs.Write(buffer, 0, read);
                            totalRead += read;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error silently if daemon
            DebugLog("Transfer error: " + ex.Message);
        }
    }

    private static void DebugLog(string msg)
    {
        // Internal debugging, could write to a log file if needed.
    }

    public static void Send(string path, string ipAddress, Action<string, long, long> progressCallback)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("The specified file or folder does not exist.");
        }

        List<string> filesToSend = new List<string>();
        string basePath = "";

        if (File.Exists(path))
        {
            filesToSend.Add(path);
            basePath = Path.GetDirectoryName(path) ?? "";
        }
        else if (Directory.Exists(path))
        {
            filesToSend.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
            basePath = Path.GetDirectoryName(path) ?? "";
        }

        using (TcpClient client = new TcpClient())
        {
            client.Connect(ipAddress, DefaultPort);

            using (NetworkStream stream = client.GetStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write("AURSH_NET_V1");
                writer.Write((long)filesToSend.Count);

                foreach (string file in filesToSend)
                {
                    string relativePath = file;
                    if (!string.IsNullOrEmpty(basePath) && file.StartsWith(basePath))
                    {
                        relativePath = file.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                    else
                    {
                        relativePath = Path.GetFileName(file);
                    }

                    FileInfo fi = new FileInfo(file);
                    writer.Write(relativePath);
                    writer.Write(fi.Length);

                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        byte[] buffer = new byte[8192];
                        long totalSent = 0;
                        int read;
                        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            stream.Write(buffer, 0, read);
                            totalSent += read;
                            progressCallback(relativePath, totalSent, fi.Length);
                        }
                    }
                }
            }
        }
    }
}
