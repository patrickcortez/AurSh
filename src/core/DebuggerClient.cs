using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;

namespace AurShell.Core;

public class DebuggerClient : IDisposable
{
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _isStepping = false;
    private readonly int _port;
    private int[] _breakpoints = Array.Empty<int>();

    public DebuggerClient(int port)
    {
        _port = port;
    }

    public void Connect()
    {
        try
        {
            _client = new TcpClient("127.0.0.1", _port);
            var stream = _client.GetStream();
            _reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            _writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: failed to connect to debugger at port {_port}: {ex.Message}");
            Disconnect();
        }
    }

    public void Disconnect()
    {
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }
        
        _reader = null;
        _writer = null;
        _client = null;
    }

    public bool IsConnected => _client != null && _client.Connected;

    public void SetStepping(bool stepping)
    {
        _isStepping = stepping;
    }

    public bool ShouldPause(int line)
    {
        if (!IsConnected) return false;
        
        if (_isStepping) return true;
        
        if (Array.IndexOf(_breakpoints, line) >= 0) return true;

        return false;
    }

    public void PauseAndBlock(int line, ShellEnvironment env)
    {
        if (!IsConnected) return;

        try
        {
            // Send paused event
            var pauseMsg = JsonSerializer.Serialize(new { @event = "paused", line = line });
            _writer?.WriteLine(pauseMsg);

            // Wait for commands
            while (IsConnected)
            {
                var lineMsg = _reader?.ReadLine();
                if (lineMsg == null)
                {
                    // Disconnected
                    Disconnect();
                    break;
                }

                try
                {
                    var cmdNode = JsonSerializer.Deserialize<JsonElement>(lineMsg);
                    if (cmdNode.TryGetProperty("command", out var cmdProp))
                    {
                        string command = cmdProp.GetString() ?? "";
                        if (command == "continue")
                        {
                            _isStepping = false;
                            break;
                        }
                        else if (command == "step")
                        {
                            _isStepping = true;
                            break;
                        }
                        else if (command == "get_env")
                        {
                            // Serialize variables
                            var envState = new System.Collections.Generic.Dictionary<string, string>();
                            foreach (var kvp in env.Variables)
                            {
                                envState[kvp.Key] = env.Get(kvp.Key) ?? "";
                            }
                            var response = JsonSerializer.Serialize(new { @event = "env_state", variables = envState });
                            _writer?.WriteLine(response);
                        }
                        else if (command == "set_breakpoints")
                        {
                            if (cmdNode.TryGetProperty("lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
                            {
                                var bps = new System.Collections.Generic.List<int>();
                                foreach (var el in linesProp.EnumerateArray())
                                {
                                    if (el.TryGetInt32(out int lineVal)) bps.Add(lineVal);
                                }
                                _breakpoints = bps.ToArray();
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed JSON
                }
            }
        }
        catch (IOException)
        {
            Disconnect(); // Graceful degrade
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: debugger error: {ex.Message}");
            Disconnect();
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
