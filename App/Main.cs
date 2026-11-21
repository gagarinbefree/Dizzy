using System.Text.Json;
using System.Security.Cryptography;
using Dizzy.Core;
using Godot;
using FileAccess = Godot.FileAccess;

public partial class Main : Node2D
{
    private GameState game = null!;
    private string assetRoot = "res://Assets";
    private SpectrumScreen presentationScreen = null!;
    private ImageTexture texture = null!;
    private Image screenImage = null!;
    private AudioStreamPlayer music = null!;
    private AudioStreamPlayer effects = null!;
    private readonly byte[] pixels = new byte[256 * 192 * 4];
    private bool title = true;
    private bool pause;
    private double blockedSeconds;
    private int flash;
    private string notice = "";
    private bool smokeTest;
    private bool pendingTitle;
    private int currentMusic;
    private int idlePose = -1;
    private int previousIdlePose;
    private int idlePoseChanges;

    public override void _Ready()
    {
        if (!HasGameData(assetRoot)) assetRoot = "user://Assets";
        smokeTest = OS.GetCmdlineUserArgs().Contains("--smoke-test");
        string? fixture = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--verify-fixture="));
        if (!HasGameData(assetRoot))
        {
            SetPhysicsProcess(false);
            SetProcessUnhandledKeyInput(false);
            if (smokeTest || fixture != null)
            {
                GD.PushError("The gameplay checks require local game data.");
                GetTree().Quit(1);
                return;
            }
            AddChild(new Label
            {
                Position = new Vector2(12, 84),
                Size = new Vector2(232, 60),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Text = "Local game data required\nSee the project README."
            });
            return;
        }

        var data = new WorldData(FileAccess.GetFileAsString($"{assetRoot}/world.json"));
        using var boot = JsonDocument.Parse(FileAccess.GetFileAsString($"{assetRoot}/start-state.json"));
        game = GameState.FromReference(data, boot.RootElement);
        presentationScreen = new SpectrumScreen(data);
        music = new AudioStreamPlayer { VolumeDb = -8 };
        AddChild(music);
        effects = new AudioStreamPlayer { VolumeDb = -10 };
        AddChild(effects);
        music.Finished += () => { if (title) { if (currentMusic == 0) music.Play(); else PlayMusic(0); } };
        game.ReturnToTitle();
        screenImage = Image.CreateEmpty(256, 192, false, Image.Format.Rgba8);
        texture = ImageTexture.CreateFromImage(screenImage);
        RefreshScreen();
        if (fixture != null)
        {
            try
            {
                VerifyFixture(data, fixture["--verify-fixture=".Length..]);
                GetTree().Quit();
            }
            catch (Exception error)
            {
                GD.PushError(error.ToString());
                GetTree().Quit(1);
            }
        }
        else if (smokeTest)
        {
            StartGame();
            for (int i = 0; i < 240; i++) game.Step(Controls.None);
            RefreshScreen();
            string path = ProjectSettings.GlobalizePath("res://Artifacts/smoke-screen.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            screenImage.SavePng(path);
            GD.Print($"GODOT_SMOKE_OK room={game.Player["room"]} x={game.Player["x"]} y={game.Player["y"]} lives={game.Lives} screenshot={path}");
            GetTree().Quit();
        }
        else
        {
            using var loadingMusic = OS.HasFeature("web") ? JavaScriptBridge.GetInterface("dizzyLoadingMusic") : null;
            PlayMusic(0, loadingMusic?.Call("finish").AsSingle() ?? 0f);
            GD.Print("DIZZY_TITLE_READY");
        }
    }

    private static bool HasGameData(string root) =>
        FileAccess.FileExists($"{root}/world.json") && FileAccess.FileExists($"{root}/start-state.json");

    private void VerifyFixture(WorldData data, string path)
    {
        using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var fixture = document.RootElement;
        game = GameState.FromReference(data, fixture.GetProperty("initial_state"));
        int ticks = 0;
        string status = "";
        foreach (var wanted in fixture.GetProperty("states").EnumerateArray())
        {
            Controls keys = Controls.None;
            foreach (var key in wanted.GetProperty("keys").EnumerateArray()) keys |= key.GetString() switch
            { "left" => Controls.Left, "right" => Controls.Right, "jump" => Controls.Jump, "action" => Controls.Action, _ => Controls.None };
            status = game.Step(keys);
            foreach (var p in wanted.GetProperty("player").EnumerateObject())
                if (game.Player[p.Name] != p.Value.GetInt32()) throw new Exception($"Godot replay {ticks} {p.Name}");
            string Hash(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
            void Equal(string name, object actual)
            { if (JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(wanted.GetProperty(name))) throw new Exception($"Godot replay {ticks} {name}"); }
            Equal("screen_sha256", Hash(game.Screen.Memory));
            Equal("objects_sha256", Hash(game.Records.SelectMany(r => r.Select(x => (byte)x)).ToArray()));
            Equal("status", status); Equal("lives", game.Lives); Equal("random_state", game.Random);
            Equal("score", game.Score); Equal("shown_score", game.ShownScore); Equal("visited", game.Visited);
            foreach (string name in new[] { "ingredients", "victory_timer", "loop_counter" }) Equal(name, game.Puzzle[name]);
            ticks++;
        }
        RefreshScreen();
        string output = ProjectSettings.GlobalizePath("res://Artifacts");
        System.IO.Directory.CreateDirectory(output);
        screenImage.SavePng(output + "/replay-screen.png");
        string result = JsonSerializer.Serialize(new
        { fixture = System.IO.Path.GetFileName(path), complete_cycles = ticks, status, lives = game.Lives, room = game.Player["room"], all_fields_matched = true });
        string label = System.IO.Path.GetFileNameWithoutExtension(path);
        screenImage.SavePng(output + "/" + label + "-screen.png");
        System.IO.File.WriteAllText(output + "/" + label + "-result.json", result);
        System.IO.File.WriteAllText(output + "/godot-replay-result.json", result);
        GD.Print($"GODOT_REPLAY_OK cycles={ticks} status={status} lives={game.Lives}");
    }

    private void PlayMusic(int number, float fromPosition = 0f)
    {
        string[] names = ["0-title", "1-start", "2-death", "3-game-over", "4-victory"];
        currentMusic = number;
        string path = $"{assetRoot}/Audio/{names[number]}.wav";
        music.Stream = assetRoot == "res://Assets"
            ? (ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null)
            : (FileAccess.FileExists(path) ? AudioStreamWav.LoadFromFile(path) : null);
        if (music.Stream != null && DisplayServer.GetName() != "headless") music.Play(fromPosition);
    }

    private void StartGame()
    {
        title = false; pause = false; pendingTitle = false; notice = "";
        idlePose = -1;
        game.NewGame();
        if (!smokeTest) PlayMusic(1);
        blockedSeconds = smokeTest ? 0 : .979;
        RefreshScreen();
        GD.Print($"DIZZY_GAME_STARTED room={game.Player["room"]}");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode == Key.F5) { game.ReturnToTitle(); StartGame(); }
        else if (title && key.Keycode is Key.Space or Key.Enter) StartGame();
    }

    public override void _PhysicsProcess(double delta)
    {
        flash++;
        if (title) { if ((flash & 15) == 0) RefreshScreen(); return; }
        if (blockedSeconds > 0) { blockedSeconds -= delta; return; }
        if (pendingTitle)
        { pendingTitle = false; title = true; game.ReturnToTitle(); PlayMusic(0); RefreshScreen(); return; }
        Controls keys = Controls.None;
        if (Input.IsPhysicalKeyPressed(Key.Z) || Input.IsPhysicalKeyPressed(Key.Left)) keys |= Controls.Left;
        if (Input.IsPhysicalKeyPressed(Key.X) || Input.IsPhysicalKeyPressed(Key.Right)) keys |= Controls.Right;
        if (Input.IsPhysicalKeyPressed(Key.Space) || Input.IsPhysicalKeyPressed(Key.Up)) keys |= Controls.Jump;
        if (Input.IsPhysicalKeyPressed(Key.Enter)) keys |= Controls.Action;
        if (Input.IsPhysicalKeyPressed(Key.P)) keys |= Controls.Pause;
        if (Input.IsPhysicalKeyPressed(Key.Q)) keys |= Controls.Quit;
        string status = game.Step(keys);
        if (game.Sounds.Count != 0)
        {
            var timelines = game.Sounds.Select(Beeper.Effect).ToArray();
            const int sampleRate = 44100;
            byte[] pcm = timelines.SelectMany(timeline => Beeper.Render(timeline, sampleRate)).ToArray();
            effects.Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = sampleRate, Data = pcm };
            effects.Play();
            blockedSeconds = timelines.Sum(t => t.TStates) / 3500000.0;
        }
        pause = status == "paused";
        if (status is "game_over" or "victory_finished")
        {
            pendingTitle = true; pause = false;
            notice = status == "victory_finished" ? "ZAKS IS DEFEATED" : status == "game_over" ? "GAME OVER" : "";
            PlayMusic(status == "victory_finished" ? 4 : 3);
            blockedSeconds = status == "victory_finished" ? 4.453 : 2.816;
        }
        else if (status == "quit") { title = true; pause = false; game.ReturnToTitle(); PlayMusic(0); }
        else if (status == "respawn") { PlayMusic(2); blockedSeconds = .739; }
        UpdateIdlePose();
        RefreshScreen();
    }

    private void UpdateIdlePose()
    {
        var player = game.Player;
        if (title || pendingTitle || player["state"] != 0 || player["animation"] != 0 ||
            player["grounded"] == 0 || player["velocity"] != 0)
        {
            idlePose = -1;
            return;
        }

        int pose = player["draw_frame"];
        if (idlePose < 0)
        {
            idlePose = previousIdlePose = pose;
            idlePoseChanges = 0;
        }
        else if (pose != previousIdlePose)
        {
            // Hold each visible pose across two changes in the underlying animation.
            if (++idlePoseChanges == 2)
            {
                idlePose = idlePose == pose ? previousIdlePose : pose;
                idlePoseChanges = 0;
            }
            previousIdlePose = pose;
        }
    }

    private void RefreshScreen()
    {
        var memory = game.Screen.Memory;
        var player = game.Player;
        if (idlePose >= 0 && idlePose != player["draw_frame"])
        {
            // Swap the XOR sprite on a copy. Collisions read the original screen.
            memory.CopyTo(presentationScreen.Memory, 0);
            int x = player["draw_x"], y = player["draw_y"];
            presentationScreen.DrawCharacter(x, y, player["draw_frame"]);
            presentationScreen.DrawCharacter(x, y, idlePose);
            memory = presentationScreen.Memory;
        }
        for (int y = 0; y < 192; y++)
            for (int x = 0; x < 256; x++)
            {
                int attr = memory[6144 + (y / 8) * 32 + x / 8];
                bool ink = (memory[SpectrumScreen.Line(y) + x / 8] & (128 >> (x & 7))) != 0;
                if ((attr & 128) != 0 && (flash & 16) != 0) ink = !ink;
                int color = ink ? attr & 7 : (attr >> 3) & 7;
                byte intensity = (byte)((attr & 64) != 0 ? 255 : 205);
                int p = (y * 256 + x) * 4;
                pixels[p] = (byte)((color & 2) != 0 ? intensity : 0);
                pixels[p + 1] = (byte)((color & 4) != 0 ? intensity : 0);
                pixels[p + 2] = (byte)((color & 1) != 0 ? intensity : 0); pixels[p + 3] = 255;
            }
        screenImage.SetData(256, 192, false, Image.Format.Rgba8, pixels);
        texture.Update(screenImage);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (texture == null) return;
        DrawTexture(texture, Vector2.Zero);
        string status = pause ? "PAUSED" : title ? notice : "";
        if (status.Length == 0) return;
        DrawStringOutline(ThemeDB.FallbackFont, new Vector2(0, 96), status, HorizontalAlignment.Center, 256, 10, 3, Colors.Black);
        DrawString(ThemeDB.FallbackFont, new Vector2(0, 96), status, HorizontalAlignment.Center, 256, 10, Colors.White);
    }
}
