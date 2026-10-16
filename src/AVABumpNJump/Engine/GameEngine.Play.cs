namespace AVABumpNJump.Engine;

public sealed partial class GameEngine
{
    public const double MaxSpeed = 700, Accel = 480, Brake = 800, SteerSpeed = 340, JumpTime = 0.85, JumpMin = 120, BumpPush = 420;
    public const double StartSeconds = 2.4, DyingSeconds = 2.4, SafeSeconds = 2.2, ClearSeconds = 3.2, StartD = 200;
    public const int ExtraLifeEvery = 30000, BumpPoints = 500, RacerPoints = 800, CrushPoints = 800, TruckPoints = 1500, PerCarBonus = 200;

    /// <summary>The stage: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Course Course { get; private set; } = Course.Build(1);
    public Buggy Player { get; } = new();
    public List<Car> Cars { get; } = [];
    public DriverPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>Cars put off the road (or landed on) this stage.</summary>
    public int Wrecked { get; private set; }
    /// <summary>For tests: no cars come, and those put there hold their line and speed.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _carIn, _clock;
    private readonly Dictionary<Car, double> _bumped = [];

    public double SpawnEvery => Math.Max(0.6, 1.3 - 0.1 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Course = Course.Build(1);
        Cars.Clear();
        PlaceBuggy(StartD);
        SetPhase(Phase.Start);
    }

    /// <summary>The buggy at a distance along the road, in the middle of it, stopped, and safe for a moment.</summary>
    private void PlaceBuggy(double d)
    {
        var p = Player;
        // Never over (or too close in front of) a gap: far enough back to get up the speed to clear it.
        if (Course.NextGap(d - 200) is { } gap)
        {
            double need = (gap.D1 - gap.D0 + 90) / JumpTime, runUp = need * need / (2 * Accel) + 160;
            if (gap.D0 - runUp < d) d = Math.Max(0, gap.D0 - runUp);
        }
        var road = Course.Road(d);
        p.D = d;
        p.X = road.Count > 0 ? (road[0].X0 + road[^1].X1) / 2 : Field.Width / 2;
        if (road.Count > 1) p.X = (road[0].X0 + road[0].X1) / 2;
        p.Speed = 0;
        p.Vx = 0;
        p.Air = 0;
        p.Alive = true;
        p.Safe = SafeSeconds;
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Course = Course.Build(wave);
        Cars.Clear();
        _bumped.Clear();
        Wrecked = 0;
        _carIn = 1;
        PlaceBuggy(StartD);
        SetPhase(Phase.Start);
        Say($"STAGE {wave}", Course.Name, StartSeconds + 0.3);
        Play(Sfx.StageStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c)
    {
        _clock += dt;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Drive(dt, c);
                if (Phase != Phase.Play) break;
                Traffic(dt);
                MoveCars(dt);
                Contact();
                if (Phase == Phase.Play && Player.D >= Course.Length) Finish();
                break;
            case Phase.Dying:
                MoveCars(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    PlaceBuggy(Player.D - 150);
                    Cars.RemoveAll(k => Math.Abs(k.D - Player.D) < 350);
                    Play(Sfx.Respawn);
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                Player.D += Player.Speed * dt;
                Player.Speed = Math.Max(0, Player.Speed - 200 * dt);
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the buggy ─────────────────────────────

    /// <summary>One step of driving: the pedals, the steering (and any shove from a bump), along the road, and in the air.</summary>
    public static void Step(Buggy p, Controls c, double dt)
    {
        if (c.Pedal > 0) p.Speed = Math.Min(MaxSpeed, p.Speed + Accel * dt);
        else if (c.Pedal < 0) p.Speed = Math.Max(0, p.Speed - Brake * dt);
        double steer = c.Steer * SteerSpeed * (p.Airborne ? 0.5 : 1);
        p.X += (steer + p.Vx) * dt;
        p.Vx *= 1 - Math.Min(1, 5 * dt);
        p.D += p.Speed * dt;
        if (c.Jump && !p.Airborne && p.Speed >= JumpMin)
        {
            p.Air = p.AirTime = JumpTime;
        }
        else if (p.Airborne) p.Air = Math.Max(0, p.Air - dt);
    }

    private void Drive(double dt, Controls c)
    {
        var p = Player;
        if (!p.Alive) return;
        p.Safe = Math.Max(0, p.Safe - dt);
        bool was = p.Airborne;
        Step(p, c, dt);
        if (!was && p.Airborne) Play(Sfx.Jump);
        if (was && !p.Airborne)
        {
            // Landing: on a car, it's crushed; in the water or off the road, that's the end.
            var under = Cars.FirstOrDefault(k => !k.Dead && !k.Airborne && Overlap(p.X, p.D, Buggy.Width, Buggy.Length, k) is not null);
            if (under != null)
            {
                Crush(under);
                return;
            }
            Play(Sfx.Land);
        }
        if (!p.Airborne && !Course.OnRoad(p.X, p.D, Buggy.Width / 2 - 8))
            Crash(Course.IsGap(p.D) ? "into the water" : "off the road");
    }

    // ───────────────────────────── the traffic ─────────────────────────────

    /// <summary>Cars join the road ahead (and, from the second stage, hot rods come up from behind), never near a gap.</summary>
    private void Traffic(double dt)
    {
        if (Calm || Player.D > Course.Length - 1000 || (_carIn -= dt) > 0) return;
        _carIn = SpawnEvery * (0.7 + _rng.NextDouble() * 0.6);
        var p = Player;
        int roll = _rng.Next(10);
        var kind = Wave >= 2 && roll < 2 ? CarKind.Truck : Wave >= 2 && roll < 4 ? CarKind.Racer : CarKind.Sedan;
        double d = kind == CarKind.Racer ? p.D - 300 : p.D + 1050;
        if (Course.NextGap(d - 260) is { } gap && gap.D0 - 260 < d && d < gap.D1 + 120) return;
        var road = Course.Road(d);
        if (road.Count == 0) return;
        var lane = road[_rng.Next(road.Count)];
        double frac = 0.2 + _rng.NextDouble() * 0.6;
        double speed = kind switch
        {
            CarKind.Racer => Math.Min(MaxSpeed - 30, 520 + 20 * Wave + _rng.NextDouble() * 60),
            CarKind.Truck => 220 + _rng.NextDouble() * 60,
            _ => 220 + _rng.NextDouble() * 160 + 12 * Wave,
        };
        Cars.Add(new Car { Kind = kind, D = d, X = lane.X0 + (lane.X1 - lane.X0) * frac, Lane = frac, Speed = speed });
    }

    /// <summary>
    /// The cars: each keeps to its place across the road as the road winds (a hot rod near the buggy swerves at it
    /// to bump it off instead), jumps the gaps, and is wrecked if it's shoved off the road.
    /// </summary>
    private void MoveCars(double dt)
    {
        var p = Player;
        foreach (var k in Cars)
        {
            if (k.Dead) continue;
            k.T += dt;
            if (Calm) { k.D += k.Speed * dt; continue; }
            k.D += k.Speed * dt;
            if (k.Airborne) { k.Air = Math.Max(0, k.Air - dt); continue; }
            // Over the water ahead: a leap.
            if (Course.NextGap(k.D) is { } gap && gap.D0 - k.D < k.Speed * 0.08 && gap.D0 > k.D - 4)
            {
                k.Air = k.AirTime = (gap.D1 - k.D + 50) / Math.Max(60, k.Speed);
                continue;
            }
            var road = Course.Road(k.D + 40);
            if (road.Count > 0)
            {
                var lane = road.OrderBy(r => Math.Abs((r.X0 + r.X1) / 2 - k.X)).First();
                double want = lane.X0 + (lane.X1 - lane.X0) * k.Lane;
                if (k.Kind == CarKind.Racer && p.Alive && Math.Abs(k.D - p.D) < 160) want = p.X;
                k.X += Math.Clamp(want - k.X, -150 * dt, 150 * dt);
            }
            k.X += k.Vx * dt;
            k.Vx *= 1 - Math.Min(1, 2.2 * dt);
            if (!Course.OnRoad(k.X, k.D, k.Width / 2 - 6)) Wreck(k);
        }
        Cars.RemoveAll(k => k.Dead || k.D < p.D - 700 || k.D > p.D + 1800);
    }

    /// <summary>A car off the road: wrecked, and worth something if the buggy shoved it there.</summary>
    private void Wreck(Car k)
    {
        k.Dead = true;
        bool shoved = _bumped.TryGetValue(k, out double at) && _clock - at < 2.5;
        _bumped.Remove(k);
        Effects.Add(new Effect { Kind = Course.IsGap(k.D) ? EffectKind.Splash : EffectKind.Blast, X = k.X, D = k.D, Life = 0.7 });
        Play(Sfx.Smash);
        if (!shoved) return;
        Wrecked++;
        Award(k.Kind == CarKind.Racer ? RacerPoints : BumpPoints, k.X, k.D);
    }

    private void Crush(Car k)
    {
        k.Dead = true;
        Wrecked++;
        Award(k.Kind == CarKind.Truck ? TruckPoints : CrushPoints, k.X, k.D);
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = k.X, D = k.D, Life = 0.6, Size = 0.8 });
        Play(Sfx.Crush);
    }

    /// <summary>How far two cars overlap across and along the road, if they do.</summary>
    public static (double Across, double Along)? Overlap(double x, double d, double w, double l, Car k)
    {
        double across = (w + k.Width) / 2 - Math.Abs(k.X - x), along = (l + k.Length) / 2 - Math.Abs(k.D - d);
        return across > 0 && along > 0 ? (across, along) : null;
    }

    /// <summary>
    /// The buggy against the cars on the road: side to side it's a bump that shoves the car across (a lorry
    /// shoves back instead); nose to tail it's a crash.
    /// </summary>
    private void Contact()
    {
        var p = Player;
        if (!p.Alive || p.Airborne) return;
        foreach (var k in Cars)
        {
            if (k.Dead || k.Airborne || Overlap(p.X, p.D, Buggy.Width, Buggy.Length, k) is not { } o) continue;
            double side = Math.Sign(k.X - p.X);
            if (side == 0) side = 1;
            if (o.Across < o.Along * 0.9)
            {
                if (k.Kind == CarKind.Truck) p.Vx = -side * 300;
                else
                {
                    k.Vx = side * BumpPush;
                    p.Vx = -side * 70;
                    _bumped[k] = _clock;
                }
                p.X -= side * o.Across / 2;
                k.X += side * o.Across / 2;
                Play(Sfx.Bump);
                continue;
            }
            if (p.Safe > 0) continue;
            Crash(k.D > p.D ? "into a car" : "hit from behind");
            return;
        }
    }

    private void Crash(string cause)
    {
        var p = Player;
        Cause = cause;
        p.Alive = false;
        p.Speed = 0;
        Lives--;
        Effects.Add(new Effect { Kind = cause == "into the water" ? EffectKind.Splash : EffectKind.Blast, X = p.X, D = p.D, Life = 0.8, Size = 1.4 });
        Play(cause == "into the water" ? Sfx.Splash : Sfx.Crash);
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Finish()
    {
        Bonus = 1000 * Wave + PerCarBonus * Wrecked;
        Play(Sfx.StageClear);
        Say($"STAGE {Wave} COMPLETE", $"{Wrecked} CARS - BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double d)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, D = d + 30, Text = points.ToString(), Life = 0.8, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a stage, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Player.Safe = 0;
        Message = SubMessage = null;
    }

    internal Car PutCar(CarKind kind, double x, double d, double speed)
    {
        var road = Course.Road(d);
        var lane = road.Count > 0 ? road.OrderBy(r => Math.Abs((r.X0 + r.X1) / 2 - x)).First() : (X0: x - 1, X1: x + 1);
        var k = new Car { Kind = kind, X = x, D = d, Speed = speed, Lane = Math.Clamp((x - lane.X0) / Math.Max(1, lane.X1 - lane.X0), 0, 1) };
        Cars.Add(k);
        return k;
    }

    internal void PutBuggy(double x, double d, double speed)
    {
        var p = Player;
        p.X = x;
        p.D = d;
        p.Speed = speed;
        p.Vx = 0;
        p.Air = 0;
        p.Alive = true;
        p.Safe = 0;
    }

    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
