using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVABumpNJump.Engine;
using AVABumpNJump.Views;
using Controls = AVABumpNJump.Engine.Controls;

namespace AVABumpNJump.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set BUMPNJUMP_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avabumpnjump-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("BUMPNJUMP_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVABumpNJump-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Jump = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 98400, 6);
        g.HighScores.Insert("AVA", 52200, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A stage, and the computer at work in it until something worth showing.</summary>
    private static void Drive(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 120)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Player.Alive && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void Spring() => Drive(1, "spring", g => g.Cars.Count(k => Math.Abs(k.D - g.Player.D) < 500) >= 3 && g.Player.Speed > 250);

    [AvaloniaFact]
    public void A_leap_over_the_water() => Drive(1, "leap", g => g.Player.Airborne && g.Player.Air < g.Player.AirTime * 0.6 && g.Course.IsGap(g.Player.D));

    [AvaloniaFact]
    public void A_bump() => Drive(2, "bump", g => g.Effects.Any(e => e.Kind == EffectKind.Blast && e.Age < 0.2) && g.Effects.Any(e => e.Kind == EffectKind.Score), seconds: 200);

    [AvaloniaFact]
    public void An_island() => Drive(3, "island", g => g.Course.Road(g.Player.D + 200).Count == 2 && g.Cars.Count(k => Math.Abs(k.D - g.Player.D) < 500) >= 2, seconds: 200);

    [AvaloniaFact]
    public void Winter() => Drive(4, "winter", g => g.Cars.Any(k => k.Kind == CarKind.Truck && k.D > g.Player.D && k.D - g.Player.D < 400) && g.Player.D > 2000, seconds: 200);

    [AvaloniaFact]
    public void A_stage_complete()
    {
        var (w, c, g) = Open(4);
        g.Go(1);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 1, 150));
        Shot(w, c, "complete");
    }
}
