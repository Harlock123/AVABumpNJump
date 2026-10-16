namespace AVABumpNJump.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It drives at a steady pace, quicker when there's a
/// gap coming (quick enough that the jump clears it), and jumps at the water's edge. It keeps to the road ahead,
/// round the islands by whichever side it's on. A car ahead it either lands on (if a jump now would come down on
/// it, on the road) or goes round; a car alongside it shoves towards the nearer edge; a car closing from behind it
/// gets out of the way of.
/// </summary>
public sealed class DriverPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public const double Cruise = 300;

    public Controls Decide(GameEngine g)
    {
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; return default; }
        if (p.Airborne)
        {
            // Steer for the road where it'll come down.
            double land = p.D + p.Speed * p.Air;
            Doing = "in the air";
            return new Controls(SteerTo(p, RoadMiddle(g, land, p.X)));
        }
        var course = g.Course;
        double want = Cruise;
        bool jump = false;
        // A gap ahead: fast enough to clear it, and off at its edge.
        if (course.NextGap(p.D) is { } gap && gap.D0 - p.D < 900)
        {
            double needed = (gap.D1 - gap.D0 + 90) / GameEngine.JumpTime;
            want = Math.Max(want, Math.Min(GameEngine.MaxSpeed, needed + 20));
            if (gap.D0 - p.D < p.Speed * Field.Step * 3 + 8) jump = true;
            Doing = "for the gap";
        }
        else Doing = "driving";
        double x = Keep(g, p, RoadMiddle(g, p.D + 140, p.X));
        // The cars about it.
        foreach (var k in g.Cars.Where(k => !k.Dead && !k.Airborne).OrderBy(k => Math.Abs(k.D - p.D)))
        {
            double ahead = k.D - p.D, across = k.X - p.X;
            // A jump now would come down on it (on the road): take it.
            double t = GameEngine.JumpTime, landD = p.D + p.Speed * t, carD = k.D + k.Speed * t;
            bool gapSoon = course.NextGap(p.D) is { } next && next.D0 < landD + 80;
            if (!jump && ahead > 0 && Math.Abs(landD - carD) < k.Length / 2 && Math.Abs(across) < 26 && p.Speed > GameEngine.JumpMin + 40
                && !gapSoon && course.OnRoad(p.X, landD, 6))
            {
                jump = true;
                Doing = "jumping on a car";
                break;
            }
            // Alongside: shove it towards the nearer edge (not a lorry, which shoves back).
            if (Math.Abs(ahead) < 40 && Math.Abs(across) < 80 && k.Kind != CarKind.Truck)
            {
                x = k.X;
                Doing = "bumping";
                break;
            }
            // In the way ahead and slower: round it (or, close, slow down).
            if (ahead > 0 && ahead < 220 && Math.Abs(across) < 46 && k.Speed < p.Speed)
            {
                var road = course.Road(k.D);
                var lane = road.OrderBy(r => Math.Abs((r.X0 + r.X1) / 2 - k.X)).FirstOrDefault();
                double left = k.X - lane.X0, right = lane.X1 - k.X;
                x = right > left ? k.X + 52 : k.X - 52;
                if (ahead < 90 && Math.Abs(across) < 36) want = Math.Min(want, k.Speed - 30);
                Doing = "going round";
                break;
            }
            // Coming up behind, fast: out of its way.
            if (ahead < 0 && ahead > -200 && Math.Abs(across) < 40 && k.Speed > p.Speed)
            {
                var road = course.Road(p.D);
                var lane = road.OrderBy(r => Math.Abs((r.X0 + r.X1) / 2 - p.X)).FirstOrDefault();
                x = p.X - lane.X0 > lane.X1 - p.X ? p.X - 50 : p.X + 50;
                want = Math.Max(want, k.Speed + 20);
                Doing = "out of the way";
                break;
            }
        }
        int pedal = p.Speed < want - 10 ? 1 : p.Speed > want + 20 ? -1 : 0;
        return new Controls(SteerTo(p, x), pedal, jump);
    }

    /// <summary>
    /// A point to steer for, kept inside the stretch of road the buggy is on now and just ahead (so it doesn't cut
    /// across an island that's still there because the road beyond it has joined up again).
    /// </summary>
    private static double Keep(GameEngine g, Buggy p, double x)
    {
        foreach (double ahead in new[] { 0.0, 60, 120 })
        {
            var road = g.Course.Road(p.D + ahead);
            if (road.Count < 2) continue;
            var lane = road.OrderBy(r => p.X < r.X0 ? r.X0 - p.X : p.X > r.X1 ? p.X - r.X1 : 0).First();
            double margin = Math.Min(30, (lane.X1 - lane.X0) / 3);
            x = Math.Clamp(x, lane.X0 + margin, lane.X1 - margin);
        }
        return x;
    }

    private static int SteerTo(Buggy p, double x) => Math.Abs(x - p.X) < 5 ? 0 : Math.Sign(x - p.X);

    /// <summary>The middle of the stretch of road (at a distance) the buggy is on or nearest, kept well in from its edges.</summary>
    private static double RoadMiddle(GameEngine g, double d, double x)
    {
        var road = g.Course.Road(d);
        if (road.Count == 0) road = g.Course.Road(d + 300);
        if (road.Count == 0) return x;
        var lane = road.OrderBy(r => x < r.X0 ? r.X0 - x : x > r.X1 ? x - r.X1 : 0).ThenBy(r => Math.Abs((r.X0 + r.X1) / 2 - x)).First();
        double margin = Math.Min(50, (lane.X1 - lane.X0) / 3);
        return Math.Clamp(x, lane.X0 + margin, lane.X1 - margin) * 0.5 + (lane.X0 + lane.X1) / 2 * 0.5;
    }
}
