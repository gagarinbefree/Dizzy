namespace Dizzy.Core;

public sealed partial class GameState
{
    private static bool Overlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) =>
        bx - ax < aw && ax - bx < bw && by - ay < ah && ay - by < bh;

    private bool Linked(int[] r, int[] target)
    {
        if (target[0] != (Room | 128)) return false;
        int sprite = r[3], x = r[1], y = r[2];
        if (sprite is >= 0x80 and < 0xa0) { sprite = 0x3d; x = (x - 4) & 255; y = (y - 10) & 255; }
        var a = Data.Sprites[sprite]; var b = Data.Sprites[target[3]];
        return Overlap(x, y, a.Width, a.Height, target[1], target[2], b.Width, b.Height);
    }

    private bool Touching(int[] r)
    {
        if (!Data.Sprites.TryGetValue((r[3] + r[6]) & 255, out var s)) return false;
        return Overlap(Player["x"], Player["y"], 6, 18, r[1], r[2], s.Width, s.Height);
    }

    private static bool TickGate(int[] r)
    {
        int high = r[5] & 0xf0, low = r[5] & 15;
        if (low == 0) { r[5] = high | (high >> 4); return true; }
        r[5] = high | (low - 1); return false;
    }

    private static bool MotionGate(int[] r)
    {
        int low = r[16] & 7;
        if (low != 0) { r[16] = (r[16] & 0xf8) | (low - 1); return false; }
        r[16] = (r[16] & 0xf8) | ((r[16] & 0x38) >> 3); return true;
    }

    private void DrawType2(int[] r)
    {
        var drawn = (int[])r.Clone();
        if ((r[16] & 128) != 0 && (r[13] & 8) != 0) drawn[6] = (drawn[6] + 2) & 255;
        if ((r[17] & 8) != 0 && (r[1] & 1) != 0) drawn[3] = (drawn[3] + 1) & 255;
        Screen.DrawObject(drawn);
    }

    private void Destroy(int[] r)
    {
        if (r[4] == 2) DrawType2(r);
        else if (r[4] == 6 && r[11] == 255) { }
        else if (r[4] == 6 && r[11] == 0) Screen.DrawObject(r, r[12]);
        else Screen.DrawObject(r);
        r[0] &= 127;
        foreach (var effect in Records.Skip(240).Take(15))
            if (effect[0] != (Room | 128))
            {
                effect[0] = Room | 128; effect[1] = r[1]; effect[2] = r[2];
                effect[3] = 0x4d; effect[4] = 5; effect[5] = 0x20; effect[6] = 0; effect[7] = 128; effect[15] = 7;
                Screen.DrawObject(effect);
                Sounds.Add(new("destroy", RandomState: (int[])Random.Clone()));
                for (int count = 0; count < 60; count++) NextRandom();
                break;
            }
        Award(r[19]);
    }

    private bool CollideLinked(int[] r)
    {
        if (r[4] == 2 && (r[17] & 16) != 0) return false;
        var target = Records[r[18]];
        if (!Linked(r, target)) return false;
        Screen.DrawObject(target); target[0] &= 127; Destroy(r); return true;
    }

    private void Contact(int[] r)
    {
        int sprite = Player["held_sprite"];
        if (sprite != 0 && Player["held_index"] == r[18])
        {
            bool consumed = r[18] is not (41 or 61 or 68 or 70 or 76 or 95);
            if (consumed) { ToggleItemName(sprite); Player["held_sprite"] = 0; }
            if (!(r[4] == 2 && (r[17] & 16) != 0 && consumed)) Destroy(r);
        }
        else if ((r[4] != 2 || (r[17] & 32) != 0) && Player["state"] == 0) Player["state"] = 255;
    }

    private void Trigger(int[] r)
    {
        r[0] &= 127; Records[r[18]][17] &= 127; Award(r[19]);
    }

    public void TickObjects()
    {
        for (int index = 0; index < 256; index++)
        {
            var r = Records[index];
            if (r[0] != (Room | 128)) continue;
            switch (r[4])
            {
                case 1: TickType1(r); break;
                case 2: TickType2(r); break;
                case 3: TickType3(r); break;
                case 4: TickType4(r, index); break;
                case 5: TickType5(r); break;
                case 6: TickType6(r); break;
                case 7: TickType7(r); break;
                case 8: TickType8(r); break;
            }
        }
    }

    private void DrawTrail(int[] r)
    {
        var trail = (int[])r.Clone(); trail[0] |= 128; trail[2] = (r[2] + 4) & 255;
        trail[3] = 0x49; trail[6] = 0; trail[15] = 7; Screen.DrawObject(trail);
    }

    private void TickType1(int[] r)
    {
        if (!TickGate(r)) return;
        if (!CollideLinked(r) && Touching(r)) Contact(r);
        Screen.DrawObject(r); DrawTrail(r);
        r[6] ^= 1; r[2] = (r[2] + r[13]) & 255;
        bool turned = false;
        if (r[2] == r[8]) { r[13] = 1; r[5] = (r[10] << 4) & 255; turned = true; }
        else if (r[2] == r[9] || r[13] < 128 && NextRandom() < r[12])
        { r[13] = 255; r[5] = (r[11] << 4) & 255; turned = true; }
        if (turned) DrawTrail(r);
        Screen.DrawObject(r);
    }

    private static void FlipX(int[] r) { r[13] ^= 8; if ((r[16] & 64) != 0) r[14] ^= 8; }
    private static void FlipY(int[] r) => r[13] ^= 128;

    private int AttemptMove(int[] r)
    {
        int changes = 0;
        void TurnX() { FlipX(r); changes = (changes + 1) & 255; }
        void TurnY() { FlipY(r); changes = (changes + 1) & 255; }
        int dx = r[13] & 7; if ((r[13] & 8) != 0) dx = -dx;
        int x = (r[1] + dx) & 255;
        if (x < r[9]) { if ((r[14] & 64) != 0) x = r[10]; else if ((r[13] & 8) != 0) TurnX(); }
        else if (x >= r[10]) { if ((r[14] & 64) != 0) x = r[9]; else if ((r[13] & 8) == 0) TurnX(); }
        else if ((r[17] & 1) != 0 && NextRandom() < 4) TurnX();
        int dy = (r[13] >> 4) & 7; if ((r[13] & 128) != 0) dy = -dy;
        int y = (r[2] + dy) & 255;
        if (y < r[11]) { if ((r[14] & 128) != 0) y = r[12]; else if ((r[13] & 128) != 0) TurnY(); }
        else if (y >= r[12]) { if ((r[14] & 128) != 0) y = r[11]; else if ((r[13] & 128) == 0) TurnY(); }
        else if ((r[17] & 2) != 0 && NextRandom() < 4) TurnY();
        r[1] = x; r[2] = y; return changes;
    }

    private bool BlockedObject(int[] r)
    {
        var s = Data.Sprites[(r[3] + (r[6] & 127)) & 255];
        int x = r[1] & 0xfe, y = r[2];
        for (int row = 0; row < (s.Height + (y & 7) + 7) / 8; row++)
            for (int col = 0; col < (s.Width + 1) / 2; col++)
                if ((Screen.Memory[6144 + ((0xbf - ((y + 8 * row) & 255)) & 0xf8) * 4 + ((x + 2 * col) & 255) / 2] & 64) == 0) return true;
        return false;
    }

    private int MoveType2(int[] r)
    {
        if (!MotionGate(r)) return 0;
        int x = r[1], y = r[2], changes = 0;
        while (true)
        {
            changes = (changes + AttemptMove(r)) & 255;
            if ((r[17] & 4) == 0 || !BlockedObject(r)) break;
            r[1] = x; r[2] = y;
            if ((NextRandom() & 8) != 0) FlipY(r); else FlipX(r);
            changes = (changes + 1) & 255;
            if (changes >= 100) break;
        }
        if ((r[14] & 32) != 0 && changes != 0) r[6] |= 128;
        else if ((r[17] & 64) != 0 && changes != 0) r[17] |= 128;
        return changes;
    }

    private static void AdvanceAnimation(int[] r)
    {
        int frame = r[6], flags = r[14], maximum = flags & 7;
        if ((frame & 128) == 0)
        {
            if ((flags & 8) == 0)
            {
                frame = (frame + 1) & 255;
                if (frame > maximum)
                {
                    if ((flags & 16) == 0) frame = 0;
                    else { frame = (frame - 1) & 255; flags |= 8; }
                }
            }
            else
            {
                frame = (frame - 1) & 255;
                if (frame == 255)
                { frame = 0; if ((r[16] & 64) == 0) flags &= 0xf7; else frame = maximum; }
            }
        }
        r[6] = frame & 127; r[14] = flags;
    }

    private void TickType2(int[] r)
    {
        if (!TickGate(r)) return;
        bool linkedHit = CollideLinked(r);
        if (!linkedHit && Touching(r))
        {
            if (r[3] == 0xfa) Trigger(r);
            if (r[3] == 0x53 && Player["velocity"] >= 128)
            { Player["velocity"] = (-Player["velocity"] * 2) & 255; Player["grounded"] = 1; Player["animation"] = 0; }
            else Contact(r);
        }
        if (r[3] == 0xd4 || (r[17] & 128) != 0 || r[13] == 0 && (r[14] & 7) == 0) return;
        if ((r[14] & 32) == 0) DrawType2(r);
        AdvanceAnimation(r);
        int changes;
        if (Linked(r, Records[r[18]])) { MotionGate(r); changes = 0; }
        else changes = MoveType2(r);
        if ((r[14] & 32) != 0 && changes != 0) return;
        DrawType2(r);
    }
}
