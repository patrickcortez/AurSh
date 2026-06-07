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



    public void DrawEllipse(int xc, int yc, int rx, int ry, Color32 color)
    {
        float dx, dy, d1, d2, x, y;
        x = 0;
        y = ry;
        d1 = (ry * ry) - (rx * rx * ry) + (0.25f * rx * rx);
        dx = 2 * ry * ry * x;
        dy = 2 * rx * rx * y;

        while (dx < dy)
        {
            _screen.SetPixel((int)(x + xc), (int)(y + yc), color);
            _screen.SetPixel((int)(-x + xc), (int)(y + yc), color);
            _screen.SetPixel((int)(x + xc), (int)(-y + yc), color);
            _screen.SetPixel((int)(-x + xc), (int)(-y + yc), color);

            if (d1 < 0)
            {
                x++;
                dx = dx + (2 * ry * ry);
                d1 = d1 + dx + (ry * ry);
            }
            else
            {
                x++;
                y--;
                dx = dx + (2 * ry * ry);
                dy = dy - (2 * rx * rx);
                d1 = d1 + dx - dy + (ry * ry);
            }
        }

        d2 = ((ry * ry) * ((x + 0.5f) * (x + 0.5f))) + ((rx * rx) * ((y - 1) * (y - 1))) - (rx * rx * ry * ry);
        while (y >= 0)
        {
            _screen.SetPixel((int)(x + xc), (int)(y + yc), color);
            _screen.SetPixel((int)(-x + xc), (int)(y + yc), color);
            _screen.SetPixel((int)(x + xc), (int)(-y + yc), color);
            _screen.SetPixel((int)(-x + xc), (int)(-y + yc), color);

            if (d2 > 0)
            {
                y--;
                dy = dy - (2 * rx * rx);
                d2 = d2 + (rx * rx) - dy;
            }
            else
            {
                y--;
                x++;
                dx = dx + (2 * ry * ry);
                dy = dy - (2 * rx * rx);
                d2 = d2 + dx - dy + (rx * rx);
            }
        }
    }

    public void DrawBezierCurve(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, Color32 color, int segments = 50)
    {
        float prevX = x0;
        float prevY = y0;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            float pX = uuu * x0; //first term
            pX += 3 * uu * t * x1; //second term
            pX += 3 * u * tt * x2; //third term
            pX += ttt * x3; //fourth term

            float pY = uuu * y0;
            pY += 3 * uu * t * y1;
            pY += 3 * u * tt * y2;
            pY += ttt * y3;

            DrawLine((int)prevX, (int)prevY, (int)pX, (int)pY, color);
            prevX = pX;
            prevY = pY;
        }
    }

    public void FillPolygon(int[] xs, int[] ys, Color32 color)
    {
        if (xs.Length != ys.Length || xs.Length < 3) return;
        int minY = ys[0], maxY = ys[0];
        for (int i = 1; i < ys.Length; i++)
        {
            if (ys[i] < minY) minY = ys[i];
            if (ys[i] > maxY) maxY = ys[i];
        }

        for (int y = minY; y <= maxY; y++)
        {
            var nodes = new System.Collections.Generic.List<int>();
            int j = xs.Length - 1;
            for (int i = 0; i < xs.Length; i++)
            {
                if (ys[i] < y && ys[j] >= y || ys[j] < y && ys[i] >= y)
                {
                    nodes.Add((int)(xs[i] + (y - ys[i]) / (float)(ys[j] - ys[i]) * (xs[j] - xs[i])));
                }
                j = i;
            }
            nodes.Sort();
            for (int i = 0; i < nodes.Count - 1; i += 2)
            {
                DrawLine(nodes[i], y, nodes[i + 1], y, color);
            }
        }
    }

    public void DrawText(string text, int x, int y, Color32 color, int scale = 1, bool bold = false, bool italic = false, bool strikethrough = false, bool underline = false)
    {
        if (scale < 1) scale = 1;
        int curX = x;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                y += 8 * scale;
                curX = x;
                continue;
            }

            int charStartX = curX;
            byte[] glyph = BasicFont.GetGlyph(c);
            for (int r = 0; r < 8; r++)
            {
                for (int cIdx = 0; cIdx < 8; cIdx++)
                {
                    if ((glyph[r] & (1 << (7 - cIdx))) != 0)
                    {
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int italicOffset = italic ? ((7 - r) * scale) / 3 : 0;
                                _screen.SetPixel(curX + cIdx * scale + sx + italicOffset, y + r * scale + sy, color);
                                if (bold)
                                {
                                    _screen.SetPixel(curX + cIdx * scale + sx + italicOffset + 1, y + r * scale + sy, color);
                                }
                            }
                        }
                    }
                }
            }
            curX += 8 * scale;
            if (bold) curX += 1;
            if (italic) curX += scale / 2; // Slight extra spacing for italic so it doesn't overlap next char too much

            if (strikethrough)
            {
                int sY = y + 4 * scale;
                for (int i = charStartX; i < curX; i++)
                {
                    for (int s = 0; s < scale; s++)
                        _screen.SetPixel(i, sY + s, color);
                }
            }
            if (underline)
            {
                int uY = y + 7 * scale;
                for (int i = charStartX; i < curX; i++)
                {
                    for (int s = 0; s < scale; s++)
                        _screen.SetPixel(i, uY + s, color);
                }
            }
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

    public void BlitScaled(VirtualScreen source, int destX, int destY, int destWidth, int destHeight)
    {
        for (int y = 0; y < destHeight; y++)
        {
            for (int x = 0; x < destWidth; x++)
            {
                if (destX + x < 0 || destX + x >= _screen.Width || destY + y < 0 || destY + y >= _screen.Height) continue;

                int srcX = (x * source.Width) / destWidth;
                int srcY = (y * source.Height) / destHeight;

                Color32 color = source.GetPixel(srcX, srcY);
                if (color.A > 0)
                {
                    _screen.SetPixel(destX + x, destY + y, color);
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
