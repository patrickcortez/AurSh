using System;
using System.Threading;
using System.Threading.Tasks;

namespace AurShell.UpdateTool;

/// <summary>
/// A simple, cross-platform console spinner utility that cycles through frames
/// to indicate a long-running process without flooding the console output.
/// </summary>
public sealed class ConsoleSpinner : IDisposable
{
    private const int Delay = 80;
    // Braille dots spinner commonly used in CLI tools like apt, snap, etc.
    private static readonly string[] Sequence = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    private int _counter;
    private readonly string _message;
    private readonly CancellationTokenSource _cts;
    private Task? _spinnerTask;
    private bool _started;

    public ConsoleSpinner(string message)
    {
        _message = message;
        _cts = new CancellationTokenSource();
    }

    public void Start()
    {
        if (_started) return;

        _started = true;

        if (Console.IsOutputRedirected)
        {
            // If output is redirected (e.g., CI environment), just print once.
            Console.WriteLine($"... {_message}");
            return;
        }

        _spinnerTask = Task.Run(async () =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    Turn();
                    await Task.Delay(Delay, _cts.Token);
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when stopping
            }
        });
    }

    private void Turn()
    {
        _counter++;
        string frame = Sequence[_counter % Sequence.Length];

        try
        {
            // Use \r to return to the beginning of the line
            // \x1b[36m makes the spinner cyan
            Console.Write($"\r\x1b[36m{frame}\x1b[0m {_message}");
        }
        catch
        {
            // If writing fails (e.g., cursor positioning issues), fail silently
        }
    }

    public void Stop(bool success)
    {
        if (_spinnerTask != null)
        {
            _cts.Cancel();
            try { _spinnerTask.Wait(); } catch { /* ignore */ }
        }

        string symbol = success ? "\x1b[32m✔\x1b[0m" : "\x1b[31m✖\x1b[0m";
        string endMessage = success ? "Done" : "Failed";

        if (!Console.IsOutputRedirected)
        {
            int paddingLength = 0;
            try
            {
                paddingLength = Console.WindowWidth > 0 ? Console.WindowWidth - 1 : 50;
            }
            catch
            {
                paddingLength = 50;
            }

            // Write final state, padding with spaces to clear any leftover characters
            string finalLine = $"\r{symbol} {_message} {endMessage}";
            if (finalLine.Length < paddingLength)
            {
                finalLine = finalLine.PadRight(paddingLength);
            }

            Console.WriteLine(finalLine);
        }
        else
        {
            // If redirected, just append a clean completion message
            Console.WriteLine($"{symbol} {_message} {endMessage}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
