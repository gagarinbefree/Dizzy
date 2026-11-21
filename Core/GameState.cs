using System.Text.Json;

namespace Dizzy.Core;

public sealed record SoundEvent(string Kind, int FootstepState = 1, int[]? RandomState = null);

/// <summary>Ordered game-loop state derived from the original Z80 handlers.</summary>
public sealed partial class GameState : IWorldDisplay
{
    public WorldData Data { get; }
    public SpectrumScreen Screen { get; }
    public PlayerState Player { get; } = new();
    public int[][] Records { get; private set; } = [];
    public int[] Random { get; private set; } = [];
    public int[] Visited { get; private set; } = [];
    public int[] Score { get; private set; } = [];
    public int[] ShownScore { get; private set; } = [];
    public int[] Frames { get; private set; } = [];
    public Dictionary<string, int> Puzzle { get; } = new();
    public byte[] Messages { get; } = new byte[65536];
    public int Lives { get; private set; }
    public int FootstepState { get; private set; } = 1;
    public List<SoundEvent> Sounds { get; } = new();
    private int Room => Player["room"];
    private string? pausePhase;

    public void NewGame()
    {
        Sounds.Clear(); pausePhase = null;
        InitializeWorld(Records); Puzzle["ingredients"] = 0;
        if (Player["held_sprite"] != 0)
        { ToggleItemName(Player["held_sprite"]); Player["held_sprite"] = 0; Player["action"] = 255; }
        Player["room"] = 74; Player["x"] = 40; Player["y"] = 24;
        Player["saved_x"] = 40; Player["saved_y"] = 24;
        Player["saved_prev_x"] = Player["draw_x"]; Player["saved_prev_y"] = Player["draw_y"];
        ResetAppearance();
        Array.Clear(Visited); Visited[0] = 3;
        Array.Clear(Score); Award(0);
        while (Lives != 255) { ToggleLife(Lives); Lives = (Lives - 1) & 255; }
        for (int i = 0; i < 3; i++) { Lives = i; ToggleLife(Lives); }
        ChangeRoom(74); ResetAppearance();
    }

    private void ResetAppearance()
    {
        Player["velocity"] = 0; Player["grounded"] = 1; Player["animation"] = 0;
        Player["animation_step"] = 0; Player["frame"] = 0; Player["state"] = 15;
        Screen.DrawLifecycle(Player["x"], Player["y"], 15);
    }

    public void ReturnToTitle()
    { pausePhase = null; Player["room"] = 0; Puzzle["victory_timer"] = 0; RedrawBackground(0); }

    public GameState(WorldData data) { Data = data; Screen = new(data); }

    public static GameState FromReference(WorldData data, JsonElement initial)
    {
        var g = new GameState(data);
        foreach (var p in initial.GetProperty("player").EnumerateObject()) g.Player[p.Name] = p.Value.GetInt32();
        Convert.FromHexString(initial.GetProperty("screen_hex").GetString()!).CopyTo(g.Screen.Memory, 0);
        byte[] objects = Convert.FromHexString(initial.GetProperty("objects_hex").GetString()!);
        g.Records = Enumerable.Range(0, 256).Select(i => objects.AsSpan(i * 20, 20).ToArray().Select(b => (int)b).ToArray()).ToArray();
        int[] ArrayOf(string key) => initial.GetProperty(key).EnumerateArray().Select(x => x.GetInt32()).ToArray();
        g.Random = ArrayOf("random_state"); g.Visited = ArrayOf("visited");
        g.Score = ArrayOf("score"); g.ShownScore = ArrayOf("shown_score");
        g.Frames = ArrayOf("player_frame_table"); g.Lives = initial.GetProperty("lives").GetInt32();
        foreach (var p in initial.GetProperty("puzzle").EnumerateObject()) g.Puzzle[p.Name] = p.Value.GetInt32();
        Convert.FromHexString(initial.GetProperty("text_table_hex").GetString()!).CopyTo(g.Messages,
            initial.GetProperty("text_table_address").GetInt32());
        return g;
    }

    public (int Pixels, int Attribute) Sample(int x, int y) => Screen.Sample(x, y);

    public void ChangeRoom(int room)
    {
        RedrawBackground(room);
        foreach (var r in Records)
        {
            InitializeRoomRecord(r, room);
            if (r[0] == (room | 128) && r[4] is not (0 or 3 or 5 or 6 or 7)) Screen.DrawObject(r);
        }
    }

    public void RedrawBackground(int room)
    {
        if (room is < 0 or >= 128) throw new ArgumentOutOfRangeException(nameof(room));
        Screen.RenderBackground(room);
        int index = room & 15, bit = 1 << (room >> 4);
        if ((Visited[index] & bit) == 0) { Visited[index] |= bit; Award(0x21); }
    }

    public static void InitializeRoomRecord(int[] r, int room)
    {
        if (r[0] != (room | 128)) return;
        switch (r[4])
        {
            case 1: r[2] = r[7]; r[13] = 255; r[5] = (r[11] << 4) & 255; break;
            case 2:
                if ((r[17] & 128) == 0) { r[1] = r[7]; r[2] = r[8]; r[6] = 0; }
                r[5] &= 0xf0; r[13] = (r[13] | 128) & 0xf7; r[14] &= 0xf7;
                r[6] &= 127; r[16] &= 0xf8; break;
            case 4: r[6] = 0; r[11] = 0; break;
            case 5: r[0] &= 127; break;
            case 6: r[2] = r[10]; r[11] = 255; r[6] = 0; break;
            case 7: r[9] = 7; r[7] = 0xc9; r[8] = 0xef; break;
        }
    }

    public static void InitializeWorld(int[][] records)
    {
        foreach (var r in records.Take(240))
        {
            if (r[4] == 4) { r[0] = r[9]; r[1] = r[7]; r[2] = r[8]; }
            else if (r[4] == 2) { r[17] &= 127; r[1] = r[7]; r[2] = r[8]; r[6] = 0; }
            r[0] |= 128;
        }
        foreach (var r in records.Take(250))
            if (r[4] == 4 || r[4] == 2 && r[3] >= 250) records[r[18]][17] |= 128;
        records[102][0] &= 127; records[96][0] &= 127;
    }

    public int NextRandom() => AdvanceRandom(Random);

    // E734 with an optional externally scheduled ROM interrupt while IY=E759.
    // Interactive gameplay currently treats E734 atomically; it has no Z80 clock.
    public static int AdvanceRandom(int[] state, string? interruptStage = null)
    {
        int a = state[0], b = state[1], c = state[2];
        if (interruptStage == "before_mix") b |= 32;
        int mixed = ((a - 1) & 255) ^ b;
        int nextA = (mixed << 1) & 255, nextC = (c >> 1) | (mixed & 128);
        int complemented = (~nextA) & 255;
        int rotated = ((complemented << 1) | (c & 1)) & 255;
        if (interruptStage == "before_second_xor") b |= 32;
        int nextB = rotated ^ 0x30 ^ b;
        state[0] = nextA; state[1] = nextB; state[2] = nextC;
        if (interruptStage == "after_store") state[1] |= 32;
        return nextB ^ nextC;
    }

    public void Award(int field)
    {
        int column = field >> 4, amount = field & 15;
        if (column == 0) Score[0] += amount;
        else while (true)
        {
            int value = Score[column] + amount;
            if (value < 10) { Score[column] = value; break; }
            Score[column] = value - 10;
            if (--column == 0) break;
            amount = 1;
        }
        for (int i = 0; i < 6; i++) if (ShownScore[i] != Score[i])
        {
            foreach (int glyph in new[] { ShownScore[i], Score[i] })
                Screen.DrawObject([128, 12 + 2 * i, 177, glyph, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0, 0]);
            ShownScore[i] = Score[i];
        }
    }

    private static int EncodedGlyph(int ascii)
    {
        if (ascii == 32) return 0xfa;
        int result = (ascii + 0xd0) & 255;
        return result >= 10 ? (result + 0xf9) & 255 : result;
    }

    private void ToggleItemName(int sprite)
    {
        int pointer = (0xed86 + 20 * ((sprite + 128) & 255)) & 65535, x = 14;
        for (int count = 0; count < 256; count++, pointer = (pointer + 1) & 65535, x += 2)
        {
            int character = Messages[pointer];
            if (character == 255) return;
            Screen.DrawObject([128, x & 255, 160, EncodedGlyph(character), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0]);
        }
        throw new InvalidDataException("Unterminated item name");
    }

    private void ToggleLife(int lives)
    {
        if (lives < 6) Screen.DrawObject([128, 40 + 2 * lives, 177, 41, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0]);
    }

    private void UpdatePlayer(Controls keys)
    {
        bool update = Player["delay"] == 1;
        if (update)
        {
            int anim = Player["animation"], phase = Player["animation_step"];
            if (Player["grounded"] != 0 && anim != 0 && (anim < 3 || phase == 0) && (phase & 1) == 0)
            {
                Sounds.Add(new("footstep", FootstepState)); FootstepState ^= 2;
            }
            if (keys.HasFlag(Controls.Action) && Player["action"] == 0) Player["action"] = 1;
            else if (!keys.HasFlag(Controls.Action)) Player["action"] = 0;
            Screen.DrawCharacter(Player["draw_x"], Player["draw_y"], Player["draw_frame"]);
        }
        PlayerPhysics.Step(Player, keys, Frames, this);
        if (update)
        {
            Player["draw_x"] = Player["x"]; Player["draw_y"] = Player["y"]; Player["draw_frame"] = Player["frame"];
            Screen.DrawCharacter(Player["x"], Player["y"], Player["frame"]);
        }
    }

    public void StepBeforeObjects(Controls keys)
    {
        int state = Player["state"];
        if (state == 0) UpdatePlayer(keys);
        else if (state is >= 1 and <= 15)
        {
            Screen.DrawLifecycle(Player["x"], Player["y"], state);
            Player["state"] = --state;
            if (state == 0)
            {
                Player["draw_x"] = Player["x"]; Player["draw_y"] = Player["y"]; Player["draw_frame"] = Player["frame"];
                Screen.DrawCharacter(Player["x"], Player["y"], Player["frame"]);
                UpdatePlayer(keys);
            }
            else Screen.DrawLifecycle(Player["x"], Player["y"], state);
        }
        else if (state is >= 241 and <= 255)
        {
            int phase = (-state) & 255;
            if (phase == 1)
            {
                Player["draw_x"] = Player["x"]; Player["draw_y"] = Player["y"]; Player["draw_frame"] = Player["frame"];
                Screen.DrawCharacter(Player["x"], Player["y"], Player["frame"]);
                Screen.DrawLifecycle(Player["x"], Player["y"], phase);
            }
            Screen.DrawLifecycle(Player["x"], Player["y"], phase);
            if (++phase == 16) Player["state"] = 16;
            else { Screen.DrawLifecycle(Player["x"], Player["y"], phase); Player["state"] = (-phase) & 255; }
        }
        else throw new InvalidDataException("Unsupported lifecycle state");
        if (Player["action"] == 1 && Player["held_sprite"] != 0)
        {
            int index = (Player["held_pointer"] - 0x95dd) / 20;
            ToggleItemName(Player["held_sprite"]); Player["action"] = 255;
            var r = Records[index]; r[0] = Room | 128; r[1] = (Player["x"] + 2) & 255; r[2] = Player["y"];
            Screen.DrawObject(r); Player["held_sprite"] = 0; Sounds.Add(new("drop"));
        }
    }

    public string Step(Controls keys)
    {
        Sounds.Clear();
        if (pausePhase == null)
        {
            StepBeforeObjects(keys);
            if (keys.HasFlag(Controls.Pause)) { pausePhase = "release_initial"; return "paused"; }
        }
        else
        {
            bool pressed = keys.HasFlag(Controls.Pause);
            if (pausePhase == "release_initial" && !pressed) pausePhase = "sequence";
            if (pausePhase == "sequence" && pressed) pausePhase = "release_resume";
            if (pausePhase != "release_resume" || pressed) return "paused";
            pausePhase = null;
        }
        TickObjects();
        return keys.HasFlag(Controls.Quit) ? "quit" : FinishLoop();
    }

    public string FinishLoop()
    {
        if (Player["state"] == 16)
        {
            Player["x"] = Player["saved_x"]; Player["y"] = Player["saved_y"];
            Player["draw_x"] = Player["saved_prev_x"]; Player["draw_y"] = Player["saved_prev_y"];
            ToggleLife(Lives); Lives = (Lives - 1) & 255;
            if (Lives == 255) return "game_over";
            ChangeRoom(Room);
            Player["velocity"] = 0; Player["grounded"] = 1; Player["animation"] = 0;
            Player["animation_step"] = 0; Player["frame"] = 0; Player["state"] = 15;
            Screen.DrawLifecycle(Player["x"], Player["y"], 15);
            return "respawn";
        }
        Puzzle["loop_counter"] = (Puzzle["loop_counter"] + 1) & 7;
        if (Puzzle["loop_counter"] == 0 && Puzzle["victory_timer"] != 0 && --Puzzle["victory_timer"] == 0)
        {
            Records[239][0] = 0xd8; Records[9][0] = 0xd8; return "victory_finished";
        }
        return "continue";
    }
}
