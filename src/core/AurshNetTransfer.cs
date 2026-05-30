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
    private static UdpClient? _udpListener;
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

            // UDP Listener for Discovery
            _udpListener = new UdpClient(15334);
            _udpListener.EnableBroadcast = true;

            _isRunning = true;
            _cts = new CancellationTokenSource();

            Task.Run(() => AcceptClientsAsync(_cts.Token), _cts.Token);
            Task.Run(() => ListenForDiscoveryAsync(_cts.Token), _cts.Token);
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
        _isRunning = false;
        _cts?.Cancel();
        _listener?.Stop();
        _udpListener?.Close();
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

    private static async Task ListenForDiscoveryAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udpListener != null)
        {
            try
            {
                UdpReceiveResult result = await _udpListener.ReceiveAsync(token);
                string message = System.Text.Encoding.UTF8.GetString(result.Buffer);

                if (message == "AURSH_DISCOVER")
                {
                    string response = $"AURSH_PEER|{Environment.MachineName}";
                    byte[] responseBytes = System.Text.Encoding.UTF8.GetBytes(response);
                    await _udpListener.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                }
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

    public static string EnsureAllowedIpFileExists()
    {
        string dir = Path.Combine(AurShell.Utils.Platform.HomeDirectory, ".aursh");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string file = Path.Combine(dir, "AllowedIP.con");
        if (!File.Exists(file))
        {
            File.WriteAllLines(file, new[] { "[Allowed]", "127.0.0.1" });
        }

        return file;
    }

    private static bool IsIpAllowed(string ip)
    {
        try
        {
            string file = EnsureAllowedIpFileExists();

            string[] lines = File.ReadAllLines(file);
            foreach (string line in lines)
            {
                string cleanLine = line.Trim();
                if (string.IsNullOrEmpty(cleanLine) || cleanLine.StartsWith("["))
                {
                    continue;
                }

                if (cleanLine == ip)
                {
                    return true;
                }
            }

            // Not found in the list
            return false;
        }
        catch
        {
            // If we can't read the config for some reason, securely deny access
            return false;
        }
    }

    private static void HandleClient(TcpClient client, CancellationToken token)
    {
        try
        {
            string remoteIp = "Unknown";
            if (client.Client.RemoteEndPoint is IPEndPoint endPoint)
            {
                remoteIp = endPoint.Address.ToString();
            }

            // Check against the whitelist first!
            if (!IsIpAllowed(remoteIp))
            {
                DebugLog($"Connection dropped: IP {remoteIp} is not in the allowed list (AllowedIP.con).");
                client.Close();
                return;
            }

            using (client)
            using (NetworkStream stream = client.GetStream())
            using (BinaryReader reader = new BinaryReader(stream))
            using (BinaryWriter writer = new BinaryWriter(stream)) // We need a writer to send the offset back
            {
                string magic = reader.ReadString();
                if (magic != "AURSH_NET_V2")
                {
                    DebugLog($"Transfer rejected: Unknown or outdated protocol '{magic}'. Expected 'AURSH_NET_V2'.");
                    return; 
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

                    // Handshake: Check if we already have some of this file
                    long existingSize = 0;
                    if (File.Exists(targetPath))
                    {
                        existingSize = new FileInfo(targetPath).Length;
                        if (existingSize > fileSize)
                        {
                            // If our file is somehow bigger, start over. Something is wrong.
                            existingSize = 0;
                        }
                    }

                    // Tell the sender where to start
                    writer.Write(existingSize);
                    writer.Flush();

                    if (existingSize == fileSize && fileSize > 0)
                    {
                        // We already have the whole file. Skip!
                        continue;
                    }

                    FileMode mode = existingSize > 0 ? FileMode.Append : FileMode.Create;

                    using (FileStream fs = new FileStream(targetPath, mode, FileAccess.Write))
                    {
                        // BIG 1MB buffer for maximum speed
                        byte[] buffer = new byte[1048576]; 
                        long totalRead = existingSize;

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
            using (BinaryReader reader = new BinaryReader(stream)) // Reader for the handshake
            {
                writer.Write("AURSH_NET_V2");
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
                    writer.Flush(); // Flush so the receiver gets the metadata immediately

                    // Wait for the receiver to tell us what they already have
                    long existingSize = reader.ReadInt64();

                    if (existingSize == fi.Length && fi.Length > 0)
                    {
                        // They already have everything. Fast forward progress.
                        progressCallback(relativePath, fi.Length, fi.Length);
                        continue;
                    }

                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        if (existingSize > 0)
                        {
                            fs.Seek(existingSize, SeekOrigin.Begin);
                        }

                        // BIG 1MB buffer for maximum speed
                        byte[] buffer = new byte[1048576];
                        long totalSent = existingSize;
                        int read;

                        // Report initial state if resuming
                        if (existingSize > 0)
                        {
                            progressCallback(relativePath, totalSent, fi.Length);
                        }

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

    public static List<(string Hostname, string IPAddress)> DiscoverPeers()
    {
        List<(string Hostname, string IPAddress)> peers = new List<(string Hostname, string IPAddress)>();
        
        try
        {
            using (UdpClient udpClient = new UdpClient())
            {
                udpClient.EnableBroadcast = true;
                udpClient.Client.ReceiveTimeout = 2000; // 2 seconds timeout

                IPEndPoint endpoint = new IPEndPoint(IPAddress.Broadcast, 15334);
                byte[] requestBytes = System.Text.Encoding.UTF8.GetBytes("AURSH_DISCOVER");
                
                // Blast the beacon
                udpClient.Send(requestBytes, requestBytes.Length, endpoint);

                // Start receiving responses
                DateTime startTime = DateTime.Now;
                while ((DateTime.Now - startTime).TotalSeconds < 2)
                {
                    try
                    {
                        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                        byte[] responseBytes = udpClient.Receive(ref remoteEP);
                        string response = System.Text.Encoding.UTF8.GetString(responseBytes);

                        if (response.StartsWith("AURSH_PEER|"))
                        {
                            string hostname = response.Substring(11);
                            string ip = remoteEP.Address.ToString();
                            
                            // Prevent duplicates
                            if (!peers.Exists(p => p.IPAddress == ip))
                            {
                                peers.Add((hostname, ip));
                            }
                        }
                    }
                    catch (SocketException)
                    {
                        // Receive timeout hit, which is normal. Just break out.
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog("Discovery failed: " + ex.Message);
        }

        return peers;
    }
}
