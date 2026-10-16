using AVABumpNJump.Engine;

namespace AVABumpNJump.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avabumpnjump-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A stage in play, calm: no traffic comes, and any put there holds its line and speed.</summary>
    private static GameEngine Game(int wave = 1, bool calm = true, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        g.Cars.Clear();
        return g;
    }

    private static readonly Controls Gas = new(Pedal: 1), JumpButton = new(Jump: true);

    /// <summary>A distance along a plain stretch of road (no gap or island near), and its middle.</summary>
    private static (double D, double X) Plain(GameEngine g, double from = 300)
    {
        for (double d = from; ; d += 50)
        {
            if (Enumerable.Range(0, 20).All(i => g.Course.Road(d + i * 50 - 300).Count == 1)) return (d, (g.Course.Road(d)[0].X0 + g.Course.Road(d)[0].X1) / 2);
        }
    }

    // ───────────────────────────── the course ─────────────────────────────

    [Fact]
    public void Every_stage_is_a_road_with_gaps_and_islands_the_same_each_time_and_longer_as_they_go()
    {
        for (int n = 1; n <= 4; n++)
        {
            var c = Course.Build(n);
            Assert.Contains(c.Legs, l => l.Kind == LegKind.Gap);
            Assert.Contains(c.Legs, l => l.Kind == LegKind.Split);
            Assert.True(c.Legs.Zip(c.Legs.Skip(1)).All(p => p.First.D1 == p.Second.D0));
            // Every gap can be jumped at full speed.
            Assert.All(c.Legs.Where(l => l.Kind == LegKind.Gap), l => Assert.True(l.D1 - l.D0 < GameEngine.MaxSpeed * GameEngine.JumpTime - 60));
            Assert.Empty(c.Road((c.Legs.First(l => l.Kind == LegKind.Gap).D0 + c.Legs.First(l => l.Kind == LegKind.Gap).D1) / 2));
        }
        Assert.Equal(Course.Build(2).Legs, Course.Build(2).Legs);
        Assert.True(Course.Build(4).Length > Course.Build(1).Length);
        Assert.Equal(["SPRING", "SUMMER", "AUTUMN", "WINTER"], Enumerable.Range(1, 4).Select(n => Course.Build(n).Name));
    }

    // ───────────────────────────── the buggy ─────────────────────────────

    [Fact]
    public void The_pedals_set_the_speed_and_it_holds_it_and_steering_moves_it_across()
    {
        var g = Game();
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, 0);
        g.Run(60, Gas);
        Assert.Equal(GameEngine.Accel, g.Player.Speed, 1);
        g.Idle(60);
        Assert.Equal(GameEngine.Accel, g.Player.Speed, 1);
        // Flat out, and the brake (on a buggy of its own, not to run out of road).
        var b = new Buggy();
        for (int i = 0; i < 180; i++) GameEngine.Step(b, Gas, Field.Step);
        Assert.Equal(GameEngine.MaxSpeed, b.Speed);
        for (int i = 0; i < 30; i++) GameEngine.Step(b, new Controls(Pedal: -1), Field.Step);
        Assert.Equal(GameEngine.MaxSpeed - GameEngine.Brake / 2, b.Speed, 1);
        x = g.Player.X;
        g.Run(15, new Controls(Steer: 1));
        Assert.Equal(x + GameEngine.SteerSpeed / 4, g.Player.X, 1);
    }

    [Fact]
    public void Driving_off_the_road_is_a_crash()
    {
        var g = Game();
        g.SetLives(3);
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, 100);
        int t = 0;
        while (g.Phase == Phase.Play && t++ < 300) g.Run(1, new Controls(Steer: 1));
        Assert.Equal("off the road", g.Cause);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Player.Alive && g.Player.Safe > 0);
        Assert.True(g.Course.OnRoad(g.Player.X, g.Player.D, 20));
    }

    [Fact]
    public void A_jump_clears_the_water_fast_enough_and_falls_in_too_slow()
    {
        var g = Game();
        g.SetLives(3);
        var gap = g.Course.NextGap(0)!.Value;
        double x = (g.Course.Road(gap.D0 - 10)[0].X0 + g.Course.Road(gap.D0 - 10)[0].X1) / 2;
        g.PutBuggy(x, gap.D0 - 5, GameEngine.MaxSpeed);
        g.Run(1, JumpButton);
        g.Idle(90);
        Assert.True(g.Player.Alive);
        Assert.True(g.Player.D > gap.D1);
        var h = Game();
        h.SetLives(3);
        h.PutBuggy(x, gap.D0 - 5, GameEngine.JumpMin + 10);
        h.Run(1, JumpButton);
        h.Idle(90);
        Assert.Equal("into the water", h.Cause);
        // And without jumping at all, straight in.
        var k = Game();
        k.SetLives(3);
        k.PutBuggy(x, gap.D0 - 5, 300);
        k.Idle(30);
        Assert.Equal("into the water", k.Cause);
    }

    [Fact]
    public void Too_slow_to_jump_at_all()
    {
        var g = Game();
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, GameEngine.JumpMin - 10);
        g.Run(1, JumpButton);
        Assert.False(g.Player.Airborne);
    }

    // ───────────────────────────── the cars ─────────────────────────────

    [Fact]
    public void A_bump_from_the_side_shoves_a_car_off_the_road_for_points()
    {
        var g = Game(calm: false);
        var (d, x) = Plain(g);
        var road = g.Course.Road(d)[0];
        g.PutBuggy(road.X1 - 90, d, 200);
        var k = g.PutCar(CarKind.Sedan, road.X1 - 60, d, 200);
        g.Run(10, new Controls(Steer: 1, Pedal: 0));
        Assert.True(k.Vx > 100);
        int t = 0;
        while (!k.Dead && t++ < 120) g.Idle(1);
        Assert.True(k.Dead);
        Assert.Equal(GameEngine.BumpPoints, g.Score);
        Assert.Equal(1, g.Wrecked);
    }

    [Fact]
    public void A_lorry_shoves_back_instead()
    {
        var g = Game(calm: false);
        var (d, x) = Plain(g);
        g.PutBuggy(x - 30, d, 150);
        var k = g.PutCar(CarKind.Truck, x + 10, d, 150);
        g.Run(5, new Controls(Steer: 1));
        Assert.True(g.Player.Vx < -100);
        Assert.Equal(0, k.Vx);
    }

    [Fact]
    public void Driving_into_the_back_of_a_car_is_a_crash()
    {
        var g = Game(calm: false);
        g.SetLives(3);
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, 300);
        var k = g.PutCar(CarKind.Sedan, x, d + 120, 100);
        k.Lane = 0.5;
        int t = 0;
        while (g.Phase == Phase.Play && t++ < 120) g.Idle(1);
        Assert.Equal("into a car", g.Cause);
    }

    [Fact]
    public void Landing_on_a_car_crushes_it()
    {
        var g = Game();
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, 300);
        // Where the car will be when the jump comes down.
        var k = g.PutCar(CarKind.Truck, x, d + 300 * GameEngine.JumpTime - 120 * GameEngine.JumpTime, 120);
        g.Run(1, JumpButton);
        g.Idle((int)(GameEngine.JumpTime * 60) + 2);
        Assert.True(k.Dead || !g.Cars.Contains(k));
        Assert.Equal(GameEngine.TruckPoints, g.Score);
        Assert.True(g.Player.Alive);
    }

    [Fact]
    public void A_hot_rod_comes_at_the_buggy()
    {
        var g = Game(2, calm: false);
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, 0);
        g.Player.Safe = 1000;
        var k = g.PutCar(CarKind.Racer, x + 100, d - 60, 50);
        g.Idle(30);
        Assert.True(Math.Abs(k.X - g.Player.X) < 100);
    }

    [Fact]
    public void Traffic_joins_the_road_and_the_cars_leap_the_gaps_too()
    {
        var g = Game(calm: false);
        g.Player.Safe = 1000;
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, 0);
        g.Player.Safe = 1000;
        g.Idle(60 * 5);
        Assert.NotEmpty(g.Cars);
        var gap = g.Course.NextGap(0)!.Value;
        var h = Game(calm: false);
        double gx = (h.Course.Road(gap.D0 - 10)[0].X0 + h.Course.Road(gap.D0 - 10)[0].X1) / 2;
        h.PutBuggy(gx, gap.D0 - 700, 0);
        h.Player.Safe = 1000;
        var k = h.PutCar(CarKind.Sedan, gx + 60, gap.D0 - 100, 200);
        int t = 0;
        while (k.D < gap.D1 + 20 && t++ < 300) h.Idle(1);
        Assert.False(k.Dead);
    }

    // ───────────────────────────── stages and the whole game ─────────────────────────────

    [Fact]
    public void Over_the_finish_line_pays_for_the_cars_wrecked_and_on_to_the_next_season()
    {
        var g = Game();
        var last = g.Course.Legs[^1];
        g.PutBuggy(Field.Width / 2, g.Course.Length - 5, 200);
        g.Idle(2);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(1000, g.Bonus);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal((2, "SUMMER"), (g.Wave, g.Course.Name));
    }

    [Fact]
    public void An_extra_life_every_30000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.ExtraLifeEvery - 100);
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, 300);
        var k = g.PutCar(CarKind.Truck, x, d + 300 * GameEngine.JumpTime - 120 * GameEngine.JumpTime, 120);
        g.Run(1, JumpButton);
        g.Idle(60);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_life_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(99999);
        var (d, x) = Plain(g);
        g.PutBuggy(x, d, 100);
        int t = 0;
        while (g.Phase == Phase.Play && t++ < 300) g.Run(1, new Controls(Steer: 1));
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "VRM") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("VRM", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_through_the_seasons_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var seasons = new List<string>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            seasons.Add(g.Course.Name);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal(["SPRING", "SUMMER"], seasons);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3), (g.Wave, g.Lives));
        Assert.Equal(0, g.Player.Speed);
    }

    [Fact]
    public void The_computer_player_finishes_the_first_stage()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        for (int i = 0; i < 60 * 150 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on stage {g.Wave}, at {g.Player.D:0} of {g.Course.Length:0}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(2);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Player.X:0.00} {g.Player.D:0.00} {g.Cars.Count} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Game(calm: false);
        g.Run(30, Gas);
        double d = g.Player.D;
        g.Pause();
        g.Idle(120);
        Assert.Equal(d, g.Player.D);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
