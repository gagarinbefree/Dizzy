namespace Dizzy.Core;

[Flags]
public enum Controls { None = 0, Left = 1, Right = 2, Jump = 4, Action = 8, Pause = 16, Quit = 32 }

public sealed class PlayerState
{
    public Dictionary<string, int> Values { get; } = new();
    public int this[string name] { get => Values[name]; set => Values[name] = value; }
    public PlayerState Clone()
    {
        var p = new PlayerState();
        foreach (var entry in Values) p[entry.Key] = entry.Value;
        return p;
    }
}

/// <summary>E8F3–EADC, preserving byte arithmetic and room-change order.</summary>
public static class PlayerPhysics
{
    public static void Step(PlayerState p, Controls keys, int[] frames, IWorldDisplay display)
    {
        p["delay"] = (p["delay"] - 1) & 255;
        if (p["delay"] != 0) return;
        p["delay"] = 4;
        if (!(p["animation"] >= 3 && p["animation_step"] != 0) && p["grounded"] != 0)
        {
            p["left"] = keys.HasFlag(Controls.Left) ? 1 : 0;
            p["right"] = keys.HasFlag(Controls.Right) ? 1 : 0;
            int direction = p["left"] != 0 ? 1 : p["right"] != 0 ? 2 : 0;
            if (keys.HasFlag(Controls.Jump))
            {
                p["animation_step"] = 0;
                p["velocity"] = 8;
                direction += 3;
            }
            p["animation"] = direction;
        }
        p["frame"] = frames[8 * p["animation"] + p["animation_step"]];
        p["animation_step"] = (p["animation_step"] + 1) & 7;
        int oldRoom = p["room"], oldX = p["x"], oldY = p["y"];
        int candidate = (oldX - p["left"] + p["right"]) & 255;
        void ChangeRoom(int room) { p["room"] = room & 255; display.ChangeRoom(p["room"]); }
        if (candidate == 1) { ChangeRoom(p["room"] - 1); candidate = 56; }
        else if (candidate == 57) { ChangeRoom(p["room"] + 1); candidate = 2; }
        if (candidate != oldX && SideBlocked(candidate, p["y"], oldX < candidate ? 1 : -1, display)) candidate = oldX;
        p["x"] = candidate;
        bool Blocked(int yy)
        {
            for (int px = candidate + 1; px < candidate + 5; px++)
            {
                var (pixels, attribute) = display.Sample(px & 255, yy);
                if (pixels != 0 && (attribute & 64) == 0) return true;
            }
            return false;
        }
        int velocity = (p["velocity"] - 1) & 255;
        int signed = velocity < 128 ? velocity : velocity - 256;
        int y = p["y"], grounded = 0;
        if (signed >= 1)
        {
            for (int step = 0; step < signed; step++)
            {
                if (Blocked((y + 20) & 255)) break;
                y = (y + 1) & 255;
                if (y >= 130) { ChangeRoom(p["room"] + 22); y = (y - 122) & 255; }
            }
        }
        else
        {
            int magnitude = -signed;
            if (magnitude >= 8) { magnitude = 7; velocity = 249; }
            y = (y + 4) & 255;
            for (int step = 0; step < magnitude + 4; step++)
            {
                if (Blocked((y - 1) & 255))
                {
                    if (y >= 130) { ChangeRoom(p["room"] + 22); y = (y - 122) & 255; }
                    velocity = 0; grounded = 1; break;
                }
                y = (y - 1) & 255;
                if (y == 8) { ChangeRoom(p["room"] - 22); y = (y + 122) & 255; }
            }
        }
        p["y"] = y; p["velocity"] = velocity; p["grounded"] = grounded;
        if (oldRoom != p["room"])
        {
            p["saved_x"] = p["x"]; p["saved_y"] = p["y"];
            p["saved_prev_x"] = oldX; p["saved_prev_y"] = oldY;
        }
    }

    public static bool SideBlocked(int x, int y, int direction, IWorldDisplay display)
    {
        int px = (x + (direction == 1 ? 5 : 0)) & 255;
        for (int offset = 4; offset < 18; offset++)
        {
            var (pixels, attribute) = display.Sample(px, (y + offset) & 255);
            if (pixels != 0 && (attribute & 64) == 0) return true;
        }
        return false;
    }
}
