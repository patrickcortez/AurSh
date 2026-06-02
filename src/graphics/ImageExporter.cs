using System.IO;

namespace AurShell.Graphics;

public static class ImageExporter
{
    public static void SaveToBmp(VirtualScreen screen, string filePath)
    {
        int width = screen.Width;
        int height = screen.Height;

        using (FileStream fs = new FileStream(filePath, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(fs))
        {
            int rowSize = width * 4;
            int pixelArraySize = rowSize * height;
            int fileSize = 54 + pixelArraySize;

            // BMP Header
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(fileSize);
            writer.Write((short)0); // reserved
            writer.Write((short)0); // reserved
            writer.Write(54);       // pixel data offset

            // DIB Header (BITMAPINFOHEADER)
            writer.Write(40);       // DIB header size
            writer.Write(width);
            writer.Write(height);   // positive height means bottom-up DIB
            writer.Write((short)1); // color planes
            writer.Write((short)32);// bits per pixel
            writer.Write(0);        // compression (0 = uncompressed)
            writer.Write(pixelArraySize); // image size
            writer.Write(2835);     // horizontal resolution (72 DPI)
            writer.Write(2835);     // vertical resolution (72 DPI)
            writer.Write(0);        // colors in color table
            writer.Write(0);        // important color count

            // Pixel Data (Bottom-Up format)
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 color = screen.GetPixel(x, y);
                    // BMP expects BGRA order
                    writer.Write(color.B);
                    writer.Write(color.G);
                    writer.Write(color.R);
                    writer.Write(color.A);
                }
            }
        }
    }
}
