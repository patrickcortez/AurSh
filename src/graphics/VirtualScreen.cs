using System;

namespace AurShell.Graphics;

public class VirtualScreen
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    private uint[] _pixels;

    public VirtualScreen(int width, int height)
    {
        Width = width;
        Height = height;
        _pixels = new uint[width * height];
    }

    public Span<uint> GetPixels() => _pixels.AsSpan();
    public uint[] Pixels => _pixels;

    public void SetPixel(int x, int y, Color32 color)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            if (color.A == 255)
            {
                _pixels[y * Width + x] = color.Value;
            }
            else if (color.A > 0)
            {
                // Simple alpha blending
                Color32 dest = new Color32(_pixels[y * Width + x]);
                float alpha = color.A / 255f;
                float invAlpha = 1f - alpha;
                
                byte r = (byte)(color.R * alpha + dest.R * invAlpha);
                byte g = (byte)(color.G * alpha + dest.G * invAlpha);
                byte b = (byte)(color.B * alpha + dest.B * invAlpha);
                byte a = (byte)Math.Max(dest.A, color.A);

                _pixels[y * Width + x] = new Color32(a, r, g, b).Value;
            }
        }
    }

    public Color32 GetPixel(int x, int y)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            return new Color32(_pixels[y * Width + x]);
        }
        return Color32.Transparent;
    }

    public void Clear(Color32 color)
    {
        Array.Fill(_pixels, color.Value);
    }

    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth == Width && newHeight == Height)
            return;

        uint[] newPixels = new uint[newWidth * newHeight];
        
        int minWidth = Math.Min(Width, newWidth);
        int minHeight = Math.Min(Height, newHeight);

        for (int y = 0; y < minHeight; y++)
        {
            Array.Copy(_pixels, y * Width, newPixels, y * newWidth, minWidth);
        }

        Width = newWidth;
        Height = newHeight;
        _pixels = newPixels;
    }
}
