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
        BlitRegion(source, 0, 0, source.Width, source.Height, destX, destY);
    }

    public void BlitRegion(VirtualScreen source, int srcX, int srcY, int srcWidth, int srcHeight, int destX, int destY)
    {
        for (int sy = 0; sy < srcHeight; sy++)
        {
            for (int sx = 0; sx < srcWidth; sx++)
            {
                if (srcX + sx < 0 || srcX + sx >= source.Width || srcY + sy < 0 || srcY + sy >= source.Height) continue;
                
                Color32 color = source.GetPixel(srcX + sx, srcY + sy);
                if (color.A > 0)
                {
                    // Check bounds for dest
                    if (destX + sx >= 0 && destX + sx < _screen.Width && destY + sy >= 0 && destY + sy < _screen.Height)
                    {
                        _screen.SetPixel(destX + sx, destY + sy, color);
                    }
                }
            }
        }
    }

    public void DrawCircle(int xc, int yc, int r, Color32 color)
    {
        int x = 0, y = r;
        int d = 3 - 2 * r;
        DrawCirclePoints(xc, yc, x, y, color);
        while (y >= x)
        {
            x++;
            if (d > 0)
            {
                y--;
                d = d + 4 * (x - y) + 10;
            }
            else
            {
                d = d + 4 * x + 6;
            }
            DrawCirclePoints(xc, yc, x, y, color);
        }
    }

    private void DrawCirclePoints(int xc, int yc, int x, int y, Color32 color)
    {
        _screen.SetPixel(xc + x, yc + y, color);
        _screen.SetPixel(xc - x, yc + y, color);
        _screen.SetPixel(xc + x, yc - y, color);
        _screen.SetPixel(xc - x, yc - y, color);
        _screen.SetPixel(xc + y, yc + x, color);
        _screen.SetPixel(xc - y, yc + x, color);
        _screen.SetPixel(xc + y, yc - x, color);
        _screen.SetPixel(xc - y, yc - x, color);
    }

    public void FillCircle(int xc, int yc, int r, Color32 color)
    {
        for (int y = -r; y <= r; y++)
        {
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y <= r * r)
                {
                    _screen.SetPixel(xc + x, yc + y, color);
                }
            }
        }
    }

    public void DrawTriangle(int x1, int y1, int x2, int y2, int x3, int y3, Color32 color)
    {
        DrawLine(x1, y1, x2, y2, color);
        DrawLine(x2, y2, x3, y3, color);
        DrawLine(x3, y3, x1, y1, color);
    }

    public void FillTriangle(int x1, int y1, int x2, int y2, int x3, int y3, Color32 color)
    {
        int minX = Math.Min(x1, Math.Min(x2, x3));
        int maxX = Math.Max(x1, Math.Max(x2, x3));
        int minY = Math.Min(y1, Math.Min(y2, y3));
        int maxY = Math.Max(y1, Math.Max(y2, y3));

        float den = ((y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3));
        if (den == 0) return; // collinear

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                float w1 = ((y2 - y3) * (px - x3) + (x3 - x2) * (py - y3)) / den;
                float w2 = ((y3 - y1) * (px - x3) + (x1 - x3) * (py - y3)) / den;
                float w3 = 1.0f - w1 - w2;
                
                if (w1 >= 0 && w2 >= 0 && w3 >= 0)
                {
                    _screen.SetPixel(px, py, color);
                }
            }
        }
    }

    public void FillRoundedRectangle(int x, int y, int width, int height, int r, Color32 color)
    {
        // Clamp radius
        r = Math.Min(r, Math.Min(width / 2, height / 2));
        
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                int px = i;
                int py = j;
                
                if (px < r && py < r) // top-left
                {
                    if ((r - px - 1) * (r - px - 1) + (r - py - 1) * (r - py - 1) > r * r) continue;
                }
                else if (px >= width - r && py < r) // top-right
                {
                    if ((px - (width - r)) * (px - (width - r)) + (r - py - 1) * (r - py - 1) > r * r) continue;
                }
                else if (px < r && py >= height - r) // bottom-left
                {
                    if ((r - px - 1) * (r - px - 1) + (py - (height - r)) * (py - (height - r)) > r * r) continue;
                }
                else if (px >= width - r && py >= height - r) // bottom-right
                {
                    if ((px - (width - r)) * (px - (width - r)) + (py - (height - r)) * (py - (height - r)) > r * r) continue;
                }
                
                _screen.SetPixel(x + i, y + j, color);
            }
        }
    }
}
