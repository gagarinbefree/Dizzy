namespace Dizzy.Core;

public sealed partial class GameState
{
    private void TickType3(int[] r)
    {
        if (!TickGate(r)) return;
        var item = Records[r[18]];
        var before = (int[])item.Clone();
        int left = r[7], right = r[8], y0 = r[9], y1 = r[10];
        if ((item[0] & 127) == Room && Overlap(left * 2 & 255, y0, (right - left + 1) * 2 & 255, y1 - y0 & 255,
                item[1] - 4 & 255, item[2] - 4 & 255, 18, 16))
        { item[0] &= 127; r[0] &= 127; }
        if ((before[0] & 128) != 0 && (item[0] & 128) == 0) Screen.DrawObject(before);
        if (Player["state"] == 0 && Overlap(Player["x"], Player["y"], 6, 28,
                (left - 1) * 2 & 255, y0 + 1 & 255, (right + 2 - left) * 2 & 255, y1 + 4 - y0 & 255))
            Player["state"] = 255;
        int mode = r[11];
        if (mode is not (0 or 2 or 3)) return;
        int count = y1 - y0 + (mode == 0 ? 1 : 0), width = right - left;
        var rows = Enumerable.Range(0, count).Select(i => Screen.Memory.AsSpan(SpectrumScreen.Line(191 - y0 - i) | left, width).ToArray()).ToArray();
        bool wrap = (r[12] & 1) == 0;
        for (int i = 0; i < count; i++)
        {
            byte[] row = rows[i];
            if (mode == 0) row = rows[i + 1 < count ? i + 1 : wrap ? 0 : i];
            else
            {
                row = (byte[])row.Clone();
                if (mode == 2)
                {
                    int carry = wrap ? rows[i][^1] & 1 : 0;
                    for (int j = 0; j < width; j++) { int value = row[j]; row[j] = (byte)((value >> 1) | (carry << 7)); carry = value & 1; }
                }
                else
                {
                    int carry = wrap ? rows[i][0] >> 7 : 0;
                    for (int j = width - 1; j >= 0; j--) { int value = row[j]; row[j] = (byte)((value << 1) | carry); carry = value >> 7; }
                }
            }
            row.CopyTo(Screen.Memory, SpectrumScreen.Line(191 - y0 - i) | left);
        }
    }

    private bool ItemInteraction(int[] r)
    {
        if (!Linked(r, Records[r[17]])) return false;
        int sprite = r[3];
        if (sprite is not (0x94 or 0x93)) Sounds.Add(new("interaction"));
        if (sprite == 0x94)
        {
            Records[239][0] = 0xec; Records[9][0] = 0xec; Records[9][9] = 7;
            Records[9][7] = 0xc9; Records[9][8] = 0xef; Puzzle["victory_timer"] = 205;
        }
        else if (sprite == 0x93)
        {
            if ((Records[96][0] & 128) == 0 || Puzzle["ingredients"] != 4) { r[11] = 10; return false; }
            var full = Records[102]; full[0] |= 128; Screen.DrawObject(full); full[11] = 10;
        }
        else if (sprite == 0x8f) { Records[96][0] |= 128; Screen.DrawObject(Records[96]); }
        else if (sprite == 0x8b || sprite is >= 0x90 and < 0x93) Puzzle["ingredients"] = (Puzzle["ingredients"] + 1) & 255;
        else { Records[r[18]][17] &= 127; Award(r[19]); }
        r[0] &= 127; return true;
    }

    private void TickType4(int[] r, int index)
    {
        if (!TickGate(r)) return;
        Screen.DrawObject(r);
        if (ItemInteraction(r)) return;
        int color = r[15] & 7;
        if ((r[12] & 128) != 0) { color = (color + 1) & 255; if (color == 7) r[12] &= 127; }
        else { color = (color - 1) & 255; if (color == 0) r[12] |= 128; }
        r[15] = color;
        int velocity = (r[11] - 1) & 255;
        if (velocity == 248) velocity = 249;
        r[11] = velocity;
        int oldY = r[2], signed = velocity < 128 ? velocity : velocity - 256;
        bool landed = false;
        if (!(velocity < 128 && velocity >= 4))
        {
            int count = (-signed + 4) & 255;
            for (int distance = 0; distance < (count == 0 ? 256 : count); distance++)
            {
                int probe = (oldY + 3 - distance) & 255;
                bool blocked = false;
                for (int i = 0; i < 3; i++)
                {
                    int x = (r[1] + i) & 255;
                    // EB44 does not clip item X to the visible 64 nibbles.
                    // X=64 reads byte column 32, spilling into the next row.
                    int sy = 191 - probe;
                    int pixels = Screen.Memory[SpectrumScreen.Line(sy) + x / 2] & ((x & 1) != 0 ? 15 : 240);
                    int attribute = Screen.Memory[6144 + (sy & 248) * 4 + x / 2];
                    if (pixels != 0 && (attribute & 64) == 0) blocked = true;
                }
                if (!blocked) continue;
                r[2] = (oldY + ((count - distance) & 255) + velocity) & 255; r[11] = 0; landed = true; break;
            }
        }
        if (!landed) r[2] = (oldY + velocity) & 255;
        if (r[2] < 9) { r[0] = ((Room - 22) & 255) | 128; r[2] = 128; }
        if (Player["action"] == 1 && Player["held_sprite"] == 0 && Touching(r))
        {
            Player["action"] = 255; Player["held_index"] = index; Player["held_pointer"] = 0x95dd + 20 * index;
            Player["held_sprite"] = r[3]; ToggleItemName(r[3]); r[0] &= 127; Sounds.Add(new("pickup")); return;
        }
        if (r[0] == (Room | 128)) Screen.DrawObject(r);
    }

    private void TickType5(int[] r)
    {
        if (!TickGate(r)) return;
        Screen.DrawObject(r);
        int frame;
        if ((r[7] & 128) != 0) { frame = (r[6] + 1) & 255; if (frame == 4) { r[7] &= 127; frame = 3; } }
        else { frame = (r[6] - 1) & 255; if (frame == 255) { r[0] &= 127; r[6] = 255; return; } }
        r[6] = frame;
        if ((r[0] & 128) != 0) Screen.DrawObject(r);
    }

    private void TickType6(int[] r)
    {
        if (!TickGate(r)) return;
        if (!CollideLinked(r) && Touching(r)) Contact(r);
        int phase = r[11];
        if (phase is 2 or 3) Screen.DrawObject(r);
        else if (phase == 0 && ((r[16] + 1) & 3) == 0) Screen.DrawObject(r, r[12]);
        switch (phase)
        {
            case 255: if (NextRandom() < r[14]) { r[11] = 0; r[12] = 1; r[2] = r[10]; } break;
            case 0:
                r[16] = (r[16] + 1) & 3;
                if (r[16] == 0) { r[2] = (r[2] - 1) & 255; r[12] = (r[12] + 1) & 255; if (r[12] == r[13]) r[11] = 1; }
                break;
            case 1: if (NextRandom() < r[14]) r[11] = 2; break;
            case 2: r[2] = (r[2] - 2) & 255; if (r[2] < r[9]) r[11] = 3; break;
            case 3: r[6] = (r[6] + 1) & 255; if (r[6] == 3) { r[11] = 255; r[6] = 0; r[2] = r[8]; } break;
        }
        if (phase == 255 && r[11] == 0 || phase == 0 && r[16] == 0) Screen.DrawObject(r, r[12]);
        else if (phase == 2 || phase == 3 && r[11] != 255) Screen.DrawObject(r);
    }

    private void TickType7(int[] r)
    {
        if (!TickGate(r)) return;
        int phase = (r[9] + 1) & 7; r[9] = phase;
        if (phase == 0)
        {
            int address = ((r[7] | r[8] << 8) + 1) & 65535;
            if (Messages[address] == 255)
            {
                address = 0xefca;
                for (int i = 0; i <= r[6]; i++) { while (Messages[address] != 255) address++; address++; }
            }
            r[7] = address & 255; r[8] = address >> 8; r[13] = EncodedGlyph(Messages[address]);
        }
        int column = 0;
        if (r[13] != 0xfa)
        {
            var bitmap = Data.Sprites[r[13]].Bitmap;
            for (int i = 0; i < 7; i++) if ((bitmap[i] & (128 >> phase)) != 0) column |= 1 << i;
        }
        for (int i = 0; i < 8; i++)
        {
            int address = SpectrumScreen.Line(191 - r[11] - i) | r[10], carry = (column >> (7 - i)) & 1;
            for (int j = r[12] - 1; j >= 0; j--) { int value = Screen.Memory[address + j]; Screen.Memory[address + j] = (byte)((value << 1) | carry); carry = value >> 7; }
        }
    }

    private void TickType8(int[] r)
    {
        if (!TickGate(r)) return;
        Screen.DrawObject(r);
        if (Touching(r)) { r[0] &= 127; Lives = (Lives + 1) & 255; ToggleLife(Lives); return; }
        int color = r[15] & 7;
        if ((r[16] & 1) != 0) { color = (color + 1) & 255; if (color == 7) r[16] &= 254; }
        else { color = (color - 1) & 255; if (color == 0) r[16] |= 1; }
        r[15] = color; Screen.DrawObject(r);
    }
}
