using System.Security.Cryptography;
using System.Text.Json;
using Dizzy.Core;

string dataPath = args.Length > 0 ? Path.GetFullPath(args[0]) :
    Path.GetFullPath("../../DizzyUltimate/extracted/godot-port-data");
var data = new WorldData(File.ReadAllText(Path.Combine(dataPath, "world.json")));
using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataPath, "core-reference.json")));
var reference = document.RootElement;
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
for (int room = 0; room < 128; room++)
{
    var screen = new SpectrumScreen(data);
    screen.RenderBackground(room);
    Require(Hash(screen.Memory) == reference.GetProperty("background_sha256")[room].GetString(), $"background {room}");
}
int drawingCases = 0;
foreach (var c in reference.GetProperty("object_draws").EnumerateArray())
{
    var screen = new SpectrumScreen(data);
    screen.DrawObject(c.GetProperty("record").EnumerateArray().Select(x => x.GetInt32()).ToArray());
    Require(Hash(screen.Memory) == c.GetProperty("sha256").GetString(), $"object draw {drawingCases}");
    drawingCases++;
}
foreach (var c in reference.GetProperty("partial_draws").EnumerateArray())
{
    var screen = new SpectrumScreen(data);
    screen.DrawObject(c.GetProperty("record").EnumerateArray().Select(x => x.GetInt32()).ToArray(), c.GetProperty("height").GetInt32());
    Require(Hash(screen.Memory) == c.GetProperty("sha256").GetString(), $"partial draw {drawingCases}");
    drawingCases++;
}
foreach (var c in reference.GetProperty("character_draws").EnumerateArray())
{
    var screen = new SpectrumScreen(data);
    screen.DrawCharacter(c.GetProperty("x").GetInt32(), c.GetProperty("y").GetInt32(), c.GetProperty("frame").GetInt32());
    Require(Hash(screen.Memory) == c.GetProperty("sha256").GetString(), $"character draw {drawingCases}");
    drawingCases++;
}
var fields = reference.GetProperty("boundary_fields").EnumerateArray().Select(x => x.GetString()!).ToArray();
var frames = reference.GetProperty("player_frame_table").EnumerateArray().Select(x => x.GetInt32()).ToArray();
byte[] expected = File.ReadAllBytes(Path.Combine(dataPath, "static-boundary-golden.bin"));
Require(Hash(expected) == reference.GetProperty("boundary_sha256").GetString(), "boundary fixture checksum");
int position = 0, cases = 0;
for (int room = 1; room < 128; room++)
    foreach (string direction in new[] { "left", "right", "up", "down" })
    {
        bool horizontal = direction is "left" or "right";
        for (int c = horizontal ? 9 : 2; c < (horizontal ? 130 : 57); c++)
        {
            var p = new PlayerState();
            foreach (var field in fields) p[field] = 0;
            p["room"] = room;
            p["x"] = direction == "left" ? 2 : direction == "right" ? 56 : c;
            p["y"] = horizontal ? c : direction == "up" ? 129 : 9;
            p["delay"] = 1; p["velocity"] = direction == "up" ? 2 : 0;
            p["animation"] = 3; p["animation_step"] = 1;
            p["left"] = direction == "left" ? 1 : 0;
            p["right"] = direction == "right" ? 1 : 0;
            var screen = new SpectrumScreen(data);
            screen.RenderBackground(room);
            PlayerPhysics.Step(p, Controls.None, frames, screen);
            foreach (string field in fields)
            {
                int wanted = expected[position++];
                Require(p[field] == wanted, $"boundary {room} {direction} {c}, {field}: {p[field]} != {wanted}");
            }
            cases++;
        }
    }
Require(position == expected.Length && cases == reference.GetProperty("boundary_cases").GetInt32(), "boundary count");
Console.WriteLine($"C# kernel matched {cases} native-validated boundary states, 128 room screens, {drawingCases} sprite cases.");
using (var startDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataPath, "../object-analysis/native-new-game.json"))))
{
    var game = GameState.FromReference(data, startDoc.RootElement.GetProperty("before"));
    game.NewGame();
    var wanted = GameState.FromReference(data, startDoc.RootElement.GetProperty("after"));
    foreach (var p in startDoc.RootElement.GetProperty("after").GetProperty("player").EnumerateObject())
        Require(game.Player[p.Name] == p.Value.GetInt32(), $"new game {p.Name}");
    Require(game.Screen.Memory.SequenceEqual(wanted.Screen.Memory), "new game screen");
    Require(game.Records.SelectMany(x => x).SequenceEqual(wanted.Records.SelectMany(x => x)), "new game objects");
    Require(game.Visited.SequenceEqual(wanted.Visited) && game.Random.SequenceEqual(wanted.Random) &&
        game.Score.SequenceEqual(wanted.Score) && game.ShownScore.SequenceEqual(wanted.ShownScore) && game.Lives == wanted.Lives &&
        game.Puzzle.All(x => wanted.Puzzle[x.Key] == x.Value), "new game counters");
    Console.WriteLine("C# new-game transition matched the original D291–D2D1 state.");
}
using (var audioDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataPath, "beeper-reference.json"))))
{
    int audioCases = 0;
    foreach (var c in audioDoc.RootElement.EnumerateArray())
    {
        var input = c.GetProperty("input");
        var effect = Beeper.Effect(new SoundEvent(input.GetProperty("kind").GetString()!,
            input.TryGetProperty("footstep_state", out var foot) ? foot.GetInt32() : 1,
            input.TryGetProperty("random_state", out var seed) ? seed.EnumerateArray().Select(x => x.GetInt32()).ToArray() : null));
        var wanted = c.GetProperty("expected");
        Require(effect.TStates == wanted.GetProperty("tstates").GetInt64(), $"beeper duration {audioCases}: {effect.TStates}");
        Require(effect.Events.Select(x => new long[] { x.T, x.Value }).Zip(wanted.GetProperty("events").EnumerateArray())
            .All(x => x.First.SequenceEqual(x.Second.EnumerateArray().Select(v => v.GetInt64()))) &&
            effect.Events.Count == wanted.GetProperty("events").GetArrayLength(), $"beeper FE writes {audioCases}");
        Require(Beeper.Render(effect).Length > 0, "beeper PCM");
        audioCases++;
    }
    Console.WriteLine($"C# beeper matched {audioCases} complete FE timelines and T-state durations.");
}
using (var conditionDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataPath, "placed-condition-reference.json"))))
{
    int conditions = 0;
    foreach (var c in conditionDoc.RootElement.EnumerateArray())
    {
        var game = GameState.FromReference(data, c.GetProperty("initial_state"));
        game.TickObjects();
        var wanted = c.GetProperty("expected");
        string label = $"item {c.GetProperty("item")} {c.GetProperty("mode")}";
        Require(game.Screen.Memory.SequenceEqual(Convert.FromHexString(wanted.GetProperty("screen_hex").GetString()!)), $"condition screen {label}");
        Require(game.Records.SelectMany(x => x).SequenceEqual(Convert.FromHexString(wanted.GetProperty("objects_hex").GetString()!).Select(x => (int)x)), $"condition objects {label}");
        foreach (var (name, values) in new[] { ("random_state", game.Random), ("score", game.Score), ("shown_score", game.ShownScore) })
            Require(values.SequenceEqual(wanted.GetProperty(name).EnumerateArray().Select(x => x.GetInt32())), $"condition {name} {label}");
        Require(game.Puzzle.All(x => wanted.GetProperty("puzzle").GetProperty(x.Key).GetInt32() == x.Value) && game.Lives == wanted.GetProperty("lives").GetInt32() && game.Player["state"] == wanted.GetProperty("player_state").GetInt32(), $"condition counters {label}");
        conditions++;
    }
    Console.WriteLine($"C# dispatcher matched {conditions} native-validated placed-item conditions.");
}
using (var rngDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataPath, "rng-interrupt-reference.json"))))
{
    int rngCases = 0;
    foreach (var c in rngDoc.RootElement.EnumerateArray())
    {
        int[] state = c.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        int output = GameState.AdvanceRandom(state, c.GetProperty("interrupt_stage").GetString());
        Require(output == c.GetProperty("output").GetInt32() && state.SequenceEqual(c.GetProperty("final_state").EnumerateArray().Select(x => x.GetInt32())), $"ROM interrupt RNG {rngCases}");
        rngCases++;
    }
    Console.WriteLine($"C# RNG matched {rngCases} native ROM interrupt windows, with externally supplied interrupt positions.");
}
foreach (string fixturePath in args.Skip(1))
{
    using var fixtureDoc = JsonDocument.Parse(File.ReadAllText(fixturePath));
    var fixture = fixtureDoc.RootElement;
    var game = GameState.FromReference(data, fixture.GetProperty("initial_state"));
    int ticks = 0;
    foreach (var expectedState in fixture.GetProperty("states").EnumerateArray())
    {
        Controls keys = Controls.None;
        foreach (var key in expectedState.GetProperty("keys").EnumerateArray()) keys |= key.GetString() switch
        {
            "left" => Controls.Left, "right" => Controls.Right, "jump" => Controls.Jump,
            "action" => Controls.Action, "pause" => Controls.Pause, "quit" => Controls.Quit,
            _ => Controls.None
        };
        string status = game.Step(keys);
        void Equal(string name, object actual)
        {
            var wanted = expectedState.GetProperty(name);
            string a = JsonSerializer.Serialize(actual), b = JsonSerializer.Serialize(wanted);
            Require(a == b, $"{Path.GetFileName(fixturePath)} tick {ticks} {name}: {a} != {b}");
        }
        foreach (var field in expectedState.GetProperty("player").EnumerateObject())
            Require(game.Player[field.Name] == field.Value.GetInt32(), $"{fixturePath} tick {ticks} player.{field.Name}: {game.Player[field.Name]} != {field.Value}");
        Equal("status", status); Equal("lives", game.Lives);
        Equal("screen_sha256", Hash(game.Screen.Memory));
        Equal("objects_sha256", Hash(game.Records.SelectMany(r => r.Select(x => (byte)x)).ToArray()));
        Equal("random_state", game.Random); Equal("score", game.Score); Equal("shown_score", game.ShownScore); Equal("visited", game.Visited);
        foreach (string field in new[] { "ingredients", "victory_timer", "loop_counter" }) Equal(field, game.Puzzle[field]);
        ticks++;
    }
    Console.WriteLine($"C# replay matched {ticks} complete cycles: {Path.GetFileName(fixturePath)}");
}
