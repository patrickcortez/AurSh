using System;
using Silk.NET.SDL;
using AurShell.Graphics;

namespace AurShell.Graphics;

public unsafe class SdlWindowHost : IDisposable
{
    private Sdl _sdl;
    private Window* _window;
    private Renderer* _renderer;
    private Texture* _texture;

    public SdlWindowHost(int width, int height, string title)
    {
        _sdl = Sdl.GetApi();

        if (_sdl.Init(Sdl.InitVideo) < 0)
        {
            throw new Exception("Failed to initialize SDL: " + _sdl.GetErrorS());
        }

        _window = _sdl.CreateWindow(title, Sdl.WindowposUndefined, Sdl.WindowposUndefined, width, height, (uint)WindowFlags.Shown);
        if (_window == null)
        {
            throw new Exception("Failed to create SDL Window: " + _sdl.GetErrorS());
        }

        int numDrivers = _sdl.GetNumRenderDrivers();
        bool debugRenderer = Environment.GetEnvironmentVariable("AURSH_DEBUG_RENDERER") == "1";
        string? logPath = null;

        if (debugRenderer)
        {
            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string logDir = System.IO.Path.Combine(homeDir, ".aursh", "logs");
            System.IO.Directory.CreateDirectory(logDir);
            logPath = System.IO.Path.Combine(logDir, "aursh-view.log");
            System.IO.File.AppendAllText(logPath!, $"\n--- {DateTime.Now} ---\n");
            System.IO.File.AppendAllText(logPath!, $"Found {numDrivers} SDL Render Drivers.\n");
        }

        int bestIndex = -1;
        uint bestFlags = 0;
        int bestScore = -1;

        for (int i = 0; i < numDrivers; i++)
        {
            RendererInfo info = new RendererInfo();
            _sdl.GetRenderDriverInfo(i, ref info);
            string driverName = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)info.Name) ?? "unknown";

            if (debugRenderer)
            {
                System.IO.File.AppendAllText(logPath!, $"Driver {i}: '{driverName}', Flags: {info.Flags}\n");
            }

            bool isAccel = (info.Flags & (uint)RendererFlags.Accelerated) != 0;
            bool isSoftware = (info.Flags & (uint)RendererFlags.Software) != 0;
            bool isVsync = (info.Flags & (uint)RendererFlags.Presentvsync) != 0;

            int score = 0;
            if (isSoftware) score = 1;
            if (isAccel) score = 2;
            if (isAccel && isVsync) score = 3;

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;

                bestFlags = 0;
                if (isAccel) bestFlags |= (uint)RendererFlags.Accelerated;
                if (isSoftware) bestFlags |= (uint)RendererFlags.Software;
                if (isVsync) bestFlags |= (uint)RendererFlags.Presentvsync;
            }
        }

        if (bestIndex != -1)
        {
            if (debugRenderer)
            {
                System.IO.File.AppendAllText(logPath!, $"Selecting Driver Index: {bestIndex}, Requested Flags: {bestFlags}\n");
            }
            _renderer = _sdl.CreateRenderer(_window, bestIndex, bestFlags);
        }

        if (_renderer == null)
        {
            if (debugRenderer)
            {
                System.IO.File.AppendAllText(logPath!, $"Smart selection failed or no drivers found. Falling back to default index -1.\n");
            }

            _renderer = _sdl.CreateRenderer(_window, -1, (uint)RendererFlags.Accelerated | (uint)RendererFlags.Presentvsync);
            if (_renderer == null)
            {
                _renderer = _sdl.CreateRenderer(_window, -1, (uint)RendererFlags.Software);
                if (_renderer == null)
                {
                    throw new Exception("Failed to create SDL Renderer (both Accelerated and Software failed): " + _sdl.GetErrorS());
                }
            }
        }

        // Texture to upload our VirtualScreen ARGB data
        _texture = _sdl.CreateTexture(_renderer, (uint)PixelFormatEnum.Argb8888, (int)TextureAccess.Streaming, width, height);
        if (_texture == null)
        {
            throw new Exception("Failed to create SDL Texture: " + _sdl.GetErrorS());
        }
    }

    public void Show(Compositor compositor)
    {
        bool running = true;
        Event e = new Event();

        int mouseX = 0, mouseY = 0;
        while (running)
        {
            while (_sdl.PollEvent(ref e) != 0)
            {
                if (e.Type == (uint)EventType.Quit)
                {
                    running = false;
                }
                else if (e.Type == (uint)EventType.Keydown)
                {
                    // Exit on Escape key
                    if (e.Key.Keysym.Sym == (int)KeyCode.KEscape)
                    {
                        running = false;
                    }
                }
                else if (e.Type == (uint)EventType.Mousemotion)
                {
                    mouseX = e.Motion.X;
                    mouseY = e.Motion.Y;
                    compositor.HandleMouseEvent(new MouseEventArgs { X = mouseX, Y = mouseY }, "MouseMove");
                }
                else if (e.Type == (uint)EventType.Mousebuttondown)
                {
                    compositor.HandleMouseEvent(new MouseEventArgs { X = e.Button.X, Y = e.Button.Y, Button = e.Button.Button }, "MouseDown");
                }
                else if (e.Type == (uint)EventType.Mousebuttonup)
                {
                    compositor.HandleMouseEvent(new MouseEventArgs { X = e.Button.X, Y = e.Button.Y, Button = e.Button.Button }, "MouseUp");
                }
                else if (e.Type == (uint)EventType.Mousewheel)
                {
                    compositor.HandleMouseEvent(new MouseEventArgs { X = mouseX, Y = mouseY, DeltaY = e.Wheel.Y }, "Wheel");
                }
            }

            // Perform compositor rendering
            compositor.RenderPass();

            VirtualScreen screen = compositor.GetBuffer();

            // Upload the pixel buffer directly to the GPU texture
            fixed (uint* ptr = screen.Pixels)
            {
                _sdl.UpdateTexture(_texture, null, ptr, screen.Width * 4);
            }

            // Blit and swap buffers
            _sdl.RenderClear(_renderer);
            _sdl.RenderCopy(_renderer, _texture, null, null);
            _sdl.RenderPresent(_renderer);
        }
    }

    public void Dispose()
    {
        if (_texture != null)
        {
            _sdl.DestroyTexture(_texture);
            _texture = null;
        }
        if (_renderer != null)
        {
            _sdl.DestroyRenderer(_renderer);
            _renderer = null;
        }
        if (_window != null)
        {
            _sdl.DestroyWindow(_window);
            _window = null;
        }
        _sdl.Quit();
        _sdl.Dispose();
    }
}
