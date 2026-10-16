namespace AVABumpNJump.Engine;

/// <summary>The controls: steering (-1 left, 1 right), the pedals (1 faster, -1 slower), and jump.</summary>
public readonly record struct Controls(int Steer = 0, int Pedal = 0, bool Jump = false);

/// <summary>
/// The screen and the road. The road runs up the screen; a car's place along it is its distance (d), and the
/// screen follows the player's buggy, which stays at the same height on the screen.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>Where on the screen the buggy is.</summary>
    public const double PlayerY = 650;

    /// <summary>The screen y of something a distance along the road, with the buggy at <paramref name="at"/>.</summary>
    public static double ScreenY(double d, double at) => PlayerY - (d - at);
}

public enum LegKind { Road, Gap, Split }

/// <summary>A stretch of the course: from one distance to the next, the road's middle and width changing smoothly
/// across it; or a gap (water, nothing to drive on); or a split round an island.</summary>
public sealed record Leg(double D0, double D1, LegKind Kind, double Mid0, double Mid1, double Width0, double Width1, double Island = 0);

/// <summary>
/// A stage: a course of legs from the start to the finish, built the same for the same stage every time, and its
/// season (the colour of the country it runs through).
/// </summary>
public sealed class Course
{
    public string Name { get; }
    public int Season { get; }
    public List<Leg> Legs { get; } = [];
    public double Length { get; private set; }

    private static readonly string[] Seasons = ["SPRING", "SUMMER", "AUTUMN", "WINTER"];

    private Course(string name, int season)
    {
        Name = name;
        Season = season;
    }

    public Leg At(double d)
    {
        if (d <= 0) return Legs[0];
        if (d >= Length) return Legs[^1];
        int lo = 0, hi = Legs.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Legs[mid].D0 <= d) lo = mid; else hi = mid - 1;
        }
        return Legs[lo];
    }

    /// <summary>The stretches of road there are to drive on at a distance (none over a gap; two round an island).</summary>
    public IReadOnlyList<(double X0, double X1)> Road(double d)
    {
        var l = At(d);
        if (l.Kind == LegKind.Gap) return [];
        double t = Math.Clamp((d - l.D0) / Math.Max(1, l.D1 - l.D0), 0, 1), s = t * t * (3 - 2 * t);
        double mid = l.Mid0 + (l.Mid1 - l.Mid0) * s, w = l.Width0 + (l.Width1 - l.Width0) * s;
        if (l.Kind == LegKind.Split)
        {
            // The island swells out of the middle and back.
            double island = l.Island * Math.Sin(Math.PI * t);
            if (island > 20) return [(mid - w / 2, mid - island / 2), (mid + island / 2, mid + w / 2)];
        }
        return [(mid - w / 2, mid + w / 2)];
    }

    /// <summary>Whether a point (a car's middle, with some half-width of it) is on the road.</summary>
    public bool OnRoad(double x, double d, double half = 0) => Road(d).Any(r => x - half >= r.X0 && x + half <= r.X1);

    public bool IsGap(double d) => At(d).Kind == LegKind.Gap;

    /// <summary>The next gap at or beyond a distance: where it starts and ends (or null).</summary>
    public (double D0, double D1)? NextGap(double d)
    {
        foreach (var l in Legs)
            if (l.Kind == LegKind.Gap && l.D1 > d) return (l.D0, l.D1);
        return null;
    }

    public static int Count => Seasons.Length;

    /// <summary>Stage <paramref name="number"/>: longer, with more and longer gaps, as the numbers go up.</summary>
    public static Course Build(int number)
    {
        int season = (number - 1) % Seasons.Length;
        var c = new Course(Seasons[season], season);
        var rng = new Random(1982 + number * 977);
        double d = 0, mid = Field.Width / 2, width = 420;
        void Add(LegKind kind, double length, double toMid, double toWidth, double island = 0)
        {
            c.Legs.Add(new Leg(d, d + length, kind, mid, toMid, width, toWidth, island));
            d += length;
            mid = toMid;
            width = toWidth;
        }
        Add(LegKind.Road, 900, mid, width);
        double length = 18000 + 2200 * Math.Min(6, number - 1);
        int since = 0;
        while (d < length)
        {
            int roll = rng.Next(10);
            if (since >= 2 && roll < 3 + Math.Min(3, number / 2))
            {
                // A gap: a straight run up to it, the water, and a straight run on from it.
                Add(LegKind.Road, 420, mid, width);
                Add(LegKind.Gap, 240 + rng.NextDouble() * 90 + Math.Min(120, 20 * (number - 1)), mid, width);
                Add(LegKind.Road, 380, mid, width);
                since = 0;
            }
            else if (roll < 7)
            {
                // A bend: the road wanders across, narrowing or widening.
                double toMid = Math.Clamp(mid + (rng.NextDouble() - 0.5) * 360, 300, Field.Width - 300);
                double toWidth = Math.Clamp(width + (rng.NextDouble() - 0.5) * 200, 260, 480);
                Add(LegKind.Road, 750 + rng.NextDouble() * 600, toMid, toWidth);
                since++;
            }
            else
            {
                // An island splits the road in two, wider round it.
                double wide = Math.Min(560, width + 160);
                Add(LegKind.Road, 250, Math.Clamp(mid, 320, Field.Width - 320), wide);
                Add(LegKind.Split, 1200 + rng.NextDouble() * 450, mid, wide, 160 + rng.NextDouble() * 80);
                Add(LegKind.Road, 250, mid, Math.Max(300, width));
                since++;
            }
        }
        Add(LegKind.Road, 1800, mid, 420);
        c.Length = d - 1200;
        return c;
    }
}

/// <summary>The player's buggy: along the road, across it, how fast, and whether (and how high) it's in the air.</summary>
public sealed class Buggy
{
    public double X = Field.Width / 2, D, Speed, Vx, Air, AirTime;
    public bool Alive = true;
    public double Safe;
    public bool Airborne => Air > 0;
    /// <summary>How high it is, for the drawing: up and down over the jump.</summary>
    public double Height => Airborne ? Math.Sin(Math.PI * (1 - Air / AirTime)) * 1 : 0;
    public const double Width = 30, Length = 48;
}

public enum CarKind
{
    /// <summary>An everyday car: bump it off the road, or land on it.</summary>
    Sedan,
    /// <summary>A hot rod: quicker, and it comes at you to bump you off.</summary>
    Racer,
    /// <summary>A lorry: too heavy to bump; land on it.</summary>
    Truck,
}

public sealed class Car
{
    public CarKind Kind;
    public double X, D, Speed, Vx, Lane, Air, AirTime, T;
    public bool Dead;
    public double Width => Kind == CarKind.Truck ? 40 : 30;
    public double Length => Kind == CarKind.Truck ? 76 : 46;
    public bool Airborne => Air > 0;
}

public enum EffectKind { Score, Blast, Splash, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, D, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
