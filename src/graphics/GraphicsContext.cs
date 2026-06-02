using System;

namespace AurShell.Graphics;

public class GraphicsContext
{
    private VirtualScreen _screen;

    public GraphicsContext(VirtualScreen screen)
    {
        _screen = screen;
    }

    public void Clear(Color32 color)
    {
        _screen.Clear(color);
    }

    public void DrawRectangle(int x, int y, int width, int height, Color32 color)
    {
        DrawLine(x, y, x + width - 1, y, color);
        DrawLine(x, y, x, y + height - 1, color);
        DrawLine(x + width - 1, y, x + width - 1, y + height - 1, color);
        DrawLine(x, y + height - 1, x + width - 1, y + height - 1, color);
    }

    public void FillRectangle(int x, int y, int width, int height, Color32 color)
    {
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                _screen.SetPixel(x + i, y + j, color);
            }
        }
    }

    public void DrawLine(int x0, int y0, int x1, int y1, Color32 color)
    {
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            _screen.SetPixel(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    public void DrawText(string text, int x, int y, Color32 color)
    {
        int curX = x;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                y += 8;
                curX = x;
                continue;
            }

            byte[] glyph = BasicFont.GetGlyph(c);
            for (int r = 0; r < 8; r++)
            {
                for (int cIdx = 0; cIdx < 8; cIdx++)
                {
                    if ((glyph[r] & (1 << (7 - cIdx))) != 0)
                    {
                        _screen.SetPixel(curX + cIdx, y + r, color);
                    }
                }
            }
            curX += 8;
        }
    }

    public void Blit(VirtualScreen source, int destX, int destY)
    {
        for (int sy = 0; sy < source.Height; sy++)
        {
            for (int sx = 0; sx < source.Width; sx++)
            {
                Color32 color = source.GetPixel(sx, sy);
                if (color.A > 0)
                {
                    _screen.SetPixel(destX + sx, destY + sy, color);
                }
            }
        }
    }
}
