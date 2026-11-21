using System.Text.Json;

namespace Dizzy.Core;

public sealed record Sprite(int Width, int Height, int Offset, int Flags, byte[] Bitmap);
public sealed record BackgroundCommand(int Sprite, int X, int Y, int Attribute, bool Or);

public sealed class WorldData
{
    public Dictionary<int, Sprite> Sprites { get; } = new();
    public Dictionary<int, Sprite> Characters { get; } = new();
    public BackgroundCommand[][] Rooms { get; }
    public int[][] InitialObjects { get; }

    public WorldData(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var item in root.GetProperty("background_sprites").EnumerateArray())
            Sprites.Add(item.GetProperty("index").GetInt32(), ReadSprite(item));
        foreach (var item in root.GetProperty("character_frames").EnumerateArray())
            Characters.Add(item.GetProperty("index").GetInt32(), ReadSprite(item));
        Rooms = root.GetProperty("rooms").EnumerateArray().Select(room =>
            room.GetProperty("background").EnumerateArray().Select(c => new BackgroundCommand(
                c.GetProperty("sprite").GetInt32(), c.GetProperty("x_units").GetInt32(),
                c.GetProperty("y_units").GetInt32(), c.GetProperty("attribute").GetInt32(),
                c.GetProperty("blend").GetString() == "or")).ToArray()).ToArray();
        InitialObjects = root.GetProperty("objects").EnumerateArray().Select(item =>
            Convert.FromHexString(item.GetProperty("after_initialization_hex").GetString()!)
                .Select(b => (int)b).ToArray()).ToArray();
    }

    private static Sprite ReadSprite(JsonElement item)
    {
        var h = item.GetProperty("header").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        return new(h[0], h[1], h[2], h[3], Convert.FromHexString(item.GetProperty("bitmap_hex").GetString()!));
    }
}

public interface IWorldDisplay
{
    void ChangeRoom(int room);
    (int Pixels, int Attribute) Sample(int x, int y);
}

public sealed class SpectrumScreen(WorldData data) : IWorldDisplay
{
    public byte[] Memory { get; } = new byte[6912];
    public int Room { get; private set; }
    public List<(int From, int To)> Transitions { get; } = new();
    public static int Line(int sy) => ((sy & 0xc0) << 5) | ((sy & 7) << 8) | ((sy & 0x38) << 2);

    public void ChangeRoom(int room)
    {
        Transitions.Add((Room, room));
        RenderBackground(room);
    }

    public void RenderBackground(int room)
    {
        if (room is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(room));
        Room = room;
        for (int sy = 40; sy < 184; sy++) Array.Clear(Memory, Line(sy) + 1, 30);
        for (int row = 5; row < 23; row++)
            for (int col = 1; col < 31; col++) Memory[6144 + row * 32 + col] = 0x47;
        if (room >= 112 || room >= data.Rooms.Length) return;
        foreach (var c in data.Rooms[room])
        {
            var s = data.Sprites[c.Sprite];
            int x = ((c.X & 0xfe) + s.Offset) & 255, y = c.Y;
            if (y + s.Height >= 200) continue;
            for (int yy = 0; yy < (s.Height + (y & 7) + 7) / 8; yy++)
                for (int xx = 0; xx < (s.Width + (x & 1) + 1) / 2; xx++)
                {
                    int col = (x + 2 * xx) / 2;
                    if (col is <= 0 or >= 31) continue;
                    int row = (((0xbf - (y + 8 * yy)) & 255) & 0xf8) / 8;
                    int address = 6144 + 32 * row + col;
                    if (address is >= 6144 and < 6912) Memory[address] = (byte)c.Attribute;
                }
            for (int yy = 0; yy < s.Height; yy++)
            {
                int sy = 191 - (y + s.Height - 1 - yy);
                if (sy is < 0 or >= 192) continue;
                for (int xx = 0; xx < s.Width / 2; xx++)
                {
                    int col = x / 2 + xx;
                    if (col is <= 0 or >= 31) continue;
                    int pos = Line(sy) + col;
                    byte value = s.Bitmap[yy * (s.Width / 2) + xx];
                    Memory[pos] = c.Or ? (byte)(Memory[pos] | value) : value;
                }
            }
        }
    }

    public (int Pixels, int Attribute) Sample(int x, int y)
    {
        if (x is < 0 or >= 64 || y is < 0 or >= 192) throw new ArgumentOutOfRangeException();
        return (Memory[Line(191 - y) + x / 2] & ((x & 1) != 0 ? 15 : 240),
                Memory[6144 + ((0xbf - y) & 0xf8) * 4 + x / 2]);
    }

    public void DrawObject(int[] r, int? visibleHeight = null)
    {
        if ((r[0] & 128) == 0) return;
        var s = data.Sprites[(r[3] + (r[6] & 63)) & 255];
        int height = visibleHeight ?? s.Height;
        if (visibleHeight.HasValue && (height < 1 || height > s.Height)) throw new ArgumentOutOfRangeException();
        int x = ((r[1] & 0xfe) + s.Offset) & 255, y = r[2];
        if (y + height >= 200) return;
        int color = ((r[15] & 127) ^ 7) & 127;
        for (int yy = 0; yy < (height + (y & 7) + 7) / 8; yy++)
            for (int xx = 0; xx < (s.Width + (x & 1) + 1) / 2; xx++)
            {
                int col = (x + 2 * xx) / 2;
                if (col is <= 0 or >= 31) continue;
                int row = (((0xbf - (y + 8 * yy)) & 255) & 0xf8) / 8;
                int address = 6144 + row * 32 + col;
                if (address is >= 6144 and < 6912) Memory[address] ^= (byte)color;
            }
        int width = (s.Width + 1) / 2;
        int first = visibleHeight.HasValue ? ((s.Width * (s.Height - height)) & 255) / 2 : 0;
        for (int yy = 0; yy < height; yy++)
        {
            int sy = 191 - (y + height - 1 - yy);
            if (sy is < 0 or >= 192) continue;
            for (int xx = 0; xx < width; xx++)
            {
                int col = x / 2 + xx;
                if (col is > 0 and < 31) Memory[Line(sy) + col] ^= s.Bitmap[first + yy * width + xx];
            }
        }
    }

    public void DrawCharacter(int x, int y, int frame)
    {
        var s = data.Characters[(frame + ((x & 1) != 0 ? 39 : 0)) & 255];
        if (y + s.Height >= 200) return;
        int width = s.Width / 2, col = (x / 2 + s.Offset) & 255;
        for (int yy = 0; yy < s.Height; yy++)
        {
            int sy = 191 - (y + s.Height - 1 - yy);
            if (sy is < 0 or >= 192 || col + width > 32) throw new ArgumentOutOfRangeException();
            for (int xx = 0; xx < width; xx++) Memory[Line(sy) + col + xx] ^= s.Bitmap[yy * width + xx];
        }
    }

    public void DrawLifecycle(int x, int y, int phase)
    {
        int dx = (phase * 2) & 255, dy = (phase * 4) & 255;
        (int X, int Y)[] points = [((x - dx) & 255, (y + 10 + dy) & 255),
            ((x + dx + 4) & 255, (y + 10 + dy) & 255),
            ((x + dx + 4) & 255, (y - dy) & 255), ((x - dx) & 255, (y - dy) & 255)];
        foreach (var point in points)
            if (point.X is >= 2 and < 60 && point.Y is >= 8 and < 144)
                DrawObject([128, point.X, point.Y, 0xa0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0, 0]);
        DrawObject([128, x, (y + 5) & 255, 0xa1 + (phase >> 2), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0, 0]);
    }
}
