using AVABumpNJump.Engine;

namespace AVABumpNJump.Tests;

/// <summary>How the computer player gets on, stage by stage (set BUMPNJUMP_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that floors it, wanders and jumps now and then).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("BUMPNJUMP_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "bumpnjump-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var stages = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0, wrecked = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 6 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Steer: (t / 50) % 3 - 1, Pedal: 1, Jump: t % 120 == 0) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"S{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"S{g.Wave} {g.Cause}") + 1;
                if (g.Wave != wave) { stages.Add($"S{wave}:{(t - start) / 60:0}s"); wave = g.Wave; start = t; }
                if (g.Phase == Phase.Clear && last != Phase.Clear) wrecked += g.Wrecked;
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: stage {g.Wave} after {t / 60:0}s score {g.Score}, {wrecked} cars | {string.Join(" ", stages)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
