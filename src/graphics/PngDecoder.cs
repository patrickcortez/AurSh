using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;

namespace AurShell.Graphics;

public static class PngDecoder
{
    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static VirtualScreen Decode(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        byte[] sig = br.ReadBytes(8);
        for (int i = 0; i < 8; i++)
        {
            if (sig[i] != PngSignature[i])
                throw new Exception("Not a valid PNG file.");
        }

        int width = 0;
        int height = 0;
        byte bitDepth = 0;
        byte colorType = 0;

        List<byte> idatData = new List<byte>();

        while (fs.Position < fs.Length)
        {
            int length = ReadInt32BE(br);
            string type = new string(br.ReadChars(4));

            byte[] chunkData = br.ReadBytes(length);
            int crc = ReadInt32BE(br); // Skip CRC check for simplicity

            if (type == "IHDR")
            {
                width = ReadInt32BE(chunkData, 0);
                height = ReadInt32BE(chunkData, 4);
                bitDepth = chunkData[8];
                colorType = chunkData[9];

                if (bitDepth != 8 || (colorType != 2 && colorType != 6))
                {
                    throw new Exception("Only 8-bit truecolor (with or without alpha) PNGs are currently supported by this native decoder.");
                }
            }
            else if (type == "IDAT")
            {
                idatData.AddRange(chunkData);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        byte[] decompressed = DecompressIDAT(idatData.ToArray());
        return ParsePixelData(decompressed, width, height, colorType);
    }

    private static byte[] DecompressIDAT(byte[] idat)
    {
        // Zlib header is typically 2 bytes. Skip them to use pure DeflateStream
        using var ms = new MemoryStream(idat, 2, idat.Length - 2);
        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        deflate.CopyTo(outMs);
        return outMs.ToArray();
    }

    private static VirtualScreen ParsePixelData(byte[] data, int width, int height, byte colorType)
    {
        VirtualScreen screen = new VirtualScreen(width, height);
        int bytesPerPixel = colorType == 6 ? 4 : 3;
        int stride = width * bytesPerPixel + 1; // +1 for the filter type byte

        byte[] priorRow = new byte[width * bytesPerPixel];

        for (int y = 0; y < height; y++)
        {
            int offset = y * stride;
            if (offset >= data.Length) break;

            byte filterType = data[offset];
            byte[] currentRow = new byte[width * bytesPerPixel];

            for (int x = 0; x < width * bytesPerPixel; x++)
            {
                byte raw = data[offset + 1 + x];
                byte left = x >= bytesPerPixel ? currentRow[x - bytesPerPixel] : (byte)0;
                byte up = priorRow[x];
                byte upLeft = x >= bytesPerPixel ? priorRow[x - bytesPerPixel] : (byte)0;

                byte decoded = 0;
                switch (filterType)
                {
                    case 0: // None
                        decoded = raw;
                        break;
                    case 1: // Sub
                        decoded = (byte)(raw + left);
                        break;
                    case 2: // Up
                        decoded = (byte)(raw + up);
                        break;
                    case 3: // Average
                        decoded = (byte)(raw + (left + up) / 2);
                        break;
                    case 4: // Paeth
                        decoded = (byte)(raw + PaethPredictor(left, up, upLeft));
                        break;
                }
                currentRow[x] = decoded;
            }

            for (int px = 0; px < width; px++)
            {
                int pOffset = px * bytesPerPixel;
                byte r = currentRow[pOffset];
                byte g = currentRow[pOffset + 1];
                byte b = currentRow[pOffset + 2];
                byte a = bytesPerPixel == 4 ? currentRow[pOffset + 3] : (byte)255;
                screen.SetPixel(px, y, new Color32(a, r, g, b));
            }

            Array.Copy(currentRow, priorRow, currentRow.Length);
        }
        return screen;
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static int ReadInt32BE(BinaryReader br)
    {
        byte[] b = br.ReadBytes(4);
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    private static int ReadInt32BE(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
