namespace Dizzy.Core;

public sealed record PortEvent(long T, int Value);
public sealed record BeeperTimeline(List<PortEvent> Events, long TStates);

/// <summary>Original F54D–F5B9 digital sound effects at a nominal 3.5 MHz.</summary>
public static class Beeper
{
    public static BeeperTimeline Effect(SoundEvent sound)
    {
        var events = new List<PortEvent>();
        long now = 0;
        void Primitive(int de, int hl)
        {
            static int Iterations(int value) => value == 0x8000 ? 0x8001 : (value & 0x8000) != 0 ? 1 : value + 1;
            int inner = Iterations(de), outer = Iterations(hl), value = 0;
            now += 33;
            for (int i = 0; i < outer; i++)
            {
                value ^= 16; events.Add(new(now, value));
                now += 11 + (26L * inner - 5) + 6 + 8 + (i + 1 < outer ? 12 : 7);
                if (i + 1 < outer) now += 15;
            }
            now += 10; events.Add(new(now, 0)); now += 21;
        }
        switch (sound.Kind)
        {
            case "footstep": now += 66; Primitive(sound.FootstepState << 8, 1); break;
            case "pickup":
            case "drop":
                now += 10;
                for (int i = 0; i < (sound.Kind == "pickup" ? 64 : 63); i++)
                {
                    int value = sound.Kind == "pickup" ? 128 - 2 * i : 2 + 2 * i;
                    now += 27; Primitive(value, 1);
                    now += sound.Kind == "pickup" ? 8 + (value == 2 ? 7 : 12) : 16 + (value == 126 ? 7 : 12);
                }
                now += 10; break;
            case "interaction":
                now += 37; Primitive(0x100, 0x50);
                now += 37; Primitive(0x80, 0x28); now += 10; break;
            case "destroy":
                var random = (int[])(sound.RandomState ?? throw new ArgumentException("Missing random state")).Clone();
                now += 7;
                for (int i = 0; i < 60; i++)
                {
                    now += 235;
                    int mixed = ((random[0] - 1) & 255) ^ random[1];
                    int a = mixed << 1 & 255, c = random[2] >> 1 | mixed & 128;
                    int b = (((~a & 255) << 1 | random[2] & 1) & 255) ^ 0x30 ^ random[1];
                    random[0] = a; random[1] = b; random[2] = c;
                    now += 45; Primitive((b ^ c) & 31, 1); now += 10 + (i == 59 ? 8 : 13);
                }
                now += 10; break;
            default: throw new ArgumentException($"Unknown sound {sound.Kind}");
        }
        return new(events, now);
    }

    public static byte[] Render(BeeperTimeline sound, int sampleRate = 44100)
    {
        const long clock = 3500000;
        long cursor = 0;
        double energy = 0, lastInput = 0, lastOutput = 0, alpha = Math.Exp(-2 * Math.PI * 20 / sampleRate);
        int level = -12000, sample = 0;
        byte[] pcm = new byte[(sound.TStates * sampleRate / clock + 1) * 2];
        void Advance(long t)
        {
            long target = t * sampleRate;
            while (cursor < target)
            {
                long boundary = (sample + 1L) * clock, end = Math.Min(target, boundary);
                energy += (end - cursor) * level; cursor = end;
                if (end != boundary) continue;
                double input = energy / clock, output = input - lastInput + alpha * lastOutput;
                lastInput = input; lastOutput = output;
                short value = (short)Math.Clamp(Math.Round(output), short.MinValue, short.MaxValue);
                pcm[sample * 2] = (byte)value; pcm[sample * 2 + 1] = (byte)(value >> 8);
                sample++; energy = 0;
            }
        }
        foreach (var e in sound.Events) { Advance(e.T); level = (e.Value & 16) != 0 ? 8000 : -12000; }
        Advance(sound.TStates);
        return pcm.AsSpan(0, sample * 2).ToArray();
    }
}
