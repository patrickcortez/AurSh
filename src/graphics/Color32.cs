namespace AurShell.Graphics;

public struct Color32
{
    public uint Value;

    public Color32(uint argb)
    {
        Value = argb;
    }

    public Color32(byte a, byte r, byte g, byte b)
    {
        Value = (uint)((a << 24) | (r << 16) | (g << 8) | b);
    }

    public Color32(byte r, byte g, byte b) : this(255, r, g, b) { }

    public byte A => (byte)((Value >> 24) & 0xFF);
    public byte R => (byte)((Value >> 16) & 0xFF);
    public byte G => (byte)((Value >> 8) & 0xFF);
    public byte B => (byte)(Value & 0xFF);

    public static readonly Color32 Black = new Color32(255, 0, 0, 0);
    public static readonly Color32 White = new Color32(255, 255, 255, 255);
    public static readonly Color32 Red = new Color32(255, 255, 0, 0);
    public static readonly Color32 Green = new Color32(255, 0, 255, 0);
    public static readonly Color32 Blue = new Color32(255, 0, 0, 255);
    public static readonly Color32 Transparent = new Color32(0, 0, 0, 0);
}
