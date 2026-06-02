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

        _renderer = _sdl.CreateRenderer(_window, -1, (uint)RendererFlags.Accelerated | (uint)RendererFlags.Presentvsync);
        if (_renderer == null)
        {
            // Fallback to software renderer for environments without GPU acceleration (e.g., WSL, CI)
            _renderer = _sdl.CreateRenderer(_window, -1, (uint)RendererFlags.Software);
            if (_renderer == null)
            {
                throw new Exception("Failed to create SDL Renderer (both Accelerated and Software failed): " + _sdl.GetErrorS());
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
