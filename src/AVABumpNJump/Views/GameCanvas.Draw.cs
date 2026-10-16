using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVABumpNJump.Engine;
using Effect = AVABumpNJump.Engine.Effect;

namespace AVABumpNJump.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVABumpNJump/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);
    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>Each season's country: the ground, the trees, and the flecks in the grass.</summary>
    private static readonly (uint Ground, uint Tree, uint Fleck)[] Seasons =
    [
        (0xFF4A9A3A, 0xFF2A6A2A, 0xFFE8E06A),
        (0xFFC8B070, 0xFF5A8A3A, 0xFFB09A5A),
        (0xFFB0783A, 0xFFD0602A, 0xFF8A5A2A),
        (0xFFE8EEF4, 0xFF2A4A3A, 0xFFC8D8E8),
    ];

    private double Sy(double d) => Field.ScreenY(d, Engine.Player.D);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        if (InGame || g.State == GameState.Title) DrawRoad(dc, g.Course, g.Player.D);
        if (InGame)
        {
            foreach (var k in g.Cars.Where(k => !k.Airborne)) DrawCar(dc, k.Kind, k.X, Sy(k.D), 0, Hash((int)k.D, 3, 1), k.T);
            DrawBuggy(dc);
            foreach (var k in g.Cars.Where(k => k.Airborne))
                DrawCar(dc, k.Kind, k.X, Sy(k.D), Math.Sin(Math.PI * (1 - k.Air / Math.Max(0.01, k.AirTime))), Hash((int)k.D, 3, 1), k.T);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 300), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the road ─────────────────────────────

    /// <summary>
    /// The country and the road, a band at a time up the screen: the season's ground with trees beside the road;
    /// the road with its white edges and dashed middle; and where the road gives out, water.
    /// </summary>
    private void DrawRoad(DrawingContext dc, Course course, double at)
    {
        var (ground, tree, fleck) = Seasons[course.Season];
        dc.FillRectangle(Brush(ground), new Rect(0, 0, Field.Width, Field.Height));
        const double band = 4;
        for (double y = 0; y < Field.Height; y += band)
        {
            double d = at + (Field.PlayerY - y);
            if (course.IsGap(d))
            {
                dc.FillRectangle(Brush(0xFF2A6AB0), new Rect(0, y, Field.Width, band));
                if (((int)(d / 8) + (int)(_clock * 4)) % 9 == 0) dc.FillRectangle(Brush(0x60C0E0FF), new Rect((Hash((int)(d / 8), 1, 2) % 900) + 40, y, 50, 2));
                continue;
            }
            foreach (var (x0, x1) in course.Road(d))
            {
                dc.FillRectangle(Brush(0xFF55555E), new Rect(x0, y, x1 - x0, band));
                dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x0, y, 5, band));
                dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x1 - 5, y, 5, band));
                if (x1 - x0 > 200 && ((int)Math.Floor(d / 40)) % 2 == 0) dc.FillRectangle(Brush(0xFFF0E06A), new Rect((x0 + x1) / 2 - 3, y, 6, band));
            }
        }
        // The ends of the road at the water's edge: broken.
        foreach (var l in course.Legs.Where(l => l.Kind == LegKind.Gap))
        {
            foreach (double edge in new[] { l.D0, l.D1 })
            {
                double y = Field.ScreenY(edge, at);
                if (y < -20 || y > Field.Height + 20) continue;
                var road = course.Road(edge == l.D0 ? edge - 2 : edge + 2);
                foreach (var (x0, x1) in road)
                    for (double x = x0; x < x1; x += 22)
                        Poly(dc, 0xFF3A3A40, null, 0, new Point(x, y), new Point(x + 11, y + (edge == l.D0 ? -9 : 9)), new Point(x + 22, y));
            }
        }
        // Trees and flecks off the road, fixed to the ground.
        double first = Math.Floor((at - 200) / 70) * 70;
        for (double d = first; d < at + Field.PlayerY + 120; d += 70)
        {
            uint h = Hash((int)(d / 70), 7, course.Season);
            double y = Field.ScreenY(d, at);
            if (course.IsGap(d)) continue;
            foreach (int side in new[] { 0, 1 })
            {
                double x = side == 0 ? 20 + h % 160 : Field.Width - 20 - (h >> 8) % 160;
                if (course.OnRoad(x, d, 30) || Math.Abs(x - Field.Width / 2) < 40 && course.Road(d).Count < 2) continue;
                if (course.Road(d).Any(r => x > r.X0 - 30 && x < r.X1 + 30)) continue;
                DrawTree(dc, x, y, tree, course.Season, h);
            }
            dc.FillRectangle(Brush(fleck), new Rect((h >> 4) % 1000, y + 20, 4, 3));
        }
        // On the islands: a tree or two.
        foreach (var l in course.Legs.Where(l => l.Kind == LegKind.Split))
        {
            double mid = (l.D0 + l.D1) / 2, y = Field.ScreenY(mid, at);
            if (y < -100 || y > Field.Height + 100) continue;
            var road = course.Road(mid);
            if (road.Count == 2) DrawTree(dc, (road[0].X1 + road[1].X0) / 2, y, tree, course.Season, 3);
        }
    }

    private static void DrawTree(DrawingContext dc, double x, double y, uint colour, int season, uint h)
    {
        dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 6, y + 8), 22, 14);
        if (season == 3)
        {
            // A snowy pine.
            Poly(dc, colour, null, 0, new Point(x, y - 30), new Point(x + 20, y + 14), new Point(x - 20, y + 14));
            Poly(dc, 0xFFF4F8FF, null, 0, new Point(x, y - 30), new Point(x + 8, y - 12), new Point(x - 8, y - 12));
            return;
        }
        dc.DrawEllipse(Brush(colour), null, new Point(x, y), 22 + h % 6, 20 + h % 5);
        dc.DrawEllipse(Brush(0x30FFFFFF), null, new Point(x - 7, y - 7), 9, 7);
    }

    // ───────────────────────────── the cars ─────────────────────────────

    private void DrawBuggy(DrawingContext dc)
    {
        var p = Engine.Player;
        if (!p.Alive) return;
        if (p.Safe > 0 && Engine.Phase == Phase.Play && (int)(_clock * 10) % 2 == 0) return;
        double h = p.Airborne ? Math.Sin(Math.PI * (1 - p.Air / Math.Max(0.01, p.AirTime))) : 0;
        DrawBuggyAt(dc, p.X, Field.PlayerY, h, p.Speed);
    }

    /// <summary>The buggy from above: chunky tyres, a blue body with a roll bar, and the driver's helmet; off the ground, bigger, with its shadow left below.</summary>
    private void DrawBuggyAt(DrawingContext dc, double x, double y, double h, double speed)
    {
        double k = 1 + 0.45 * h, lift = 46 * h;
        dc.DrawRectangle(Brush(0x50000000), null, new Rect(x - 16 + 10 * h, y - 22 + 14 * h, 32, 48), 8, 8);
        y -= lift;
        foreach (var (wx, wy) in new[] { (-15.0, -15.0), (15.0, -15.0), (-16.0, 15.0), (16.0, 15.0) })
        {
            dc.DrawRectangle(Brush(0xFF1A1A1A), null, new Rect(x + wx * k - 6 * k, y + wy * k - 9 * k, 12 * k, 18 * k), 3, 3);
            if (((int)(speed * _clock / 20) + (int)wy) % 2 == 0) dc.FillRectangle(Brush(0xFF4A4A4A), new Rect(x + wx * k - 6 * k, y + wy * k - 2 * k, 12 * k, 3 * k));
        }
        dc.DrawRectangle(Brush(0xFF2A6AE0), Pen(0xFF0A2A6A, 1.5), new Rect(x - 12 * k, y - 22 * k, 24 * k, 44 * k), 8 * k, 8 * k);
        dc.DrawRectangle(Brush(0xFF8AC8FF), null, new Rect(x - 9 * k, y - 15 * k, 18 * k, 7 * k), 2, 2);
        dc.DrawLine(Pen(0xFFE0E0E0, 2.5 * k), new Point(x - 10 * k, y + 2 * k), new Point(x + 10 * k, y + 2 * k));
        dc.DrawEllipse(Brush(0xFFFFE04A), Pen(0xFF6A5A10, 1), new Point(x, y - 2 * k), 5 * k, 5 * k);
        dc.FillRectangle(Brush(0xFFFF6A3A), new Rect(x - 10 * k, y + 16 * k, 6 * k, 4 * k));
        dc.FillRectangle(Brush(0xFFFF6A3A), new Rect(x + 4 * k, y + 16 * k, 6 * k, 4 * k));
    }

    /// <summary>A car from above, by kind: a saloon in one of a few colours, a striped hot rod, or a lorry with its load.</summary>
    private void DrawCar(DrawingContext dc, CarKind kind, double x, double y, double h, uint look, double t)
    {
        double k = 1 + 0.4 * h, lift = 40 * h;
        double w = kind == CarKind.Truck ? 40 : 30, l = kind == CarKind.Truck ? 76 : 46;
        dc.DrawRectangle(Brush(0x50000000), null, new Rect(x - w / 2 + 8 * h, y - l / 2 + 12 * h, w, l), 6, 6);
        y -= lift;
        w *= k;
        l *= k;
        foreach (int sx in new[] { -1, 1 })
        foreach (double sy in new[] { -0.3, 0.3 })
            dc.DrawRectangle(Brush(0xFF1A1A1A), null, new Rect(x + sx * w / 2 - (sx > 0 ? 4 : 2) * k, y + sy * l - 6 * k, 6 * k, 12 * k), 2, 2);
        switch (kind)
        {
            case CarKind.Truck:
                dc.DrawRectangle(Brush(0xFF8A8A92), Pen(0xFF3A3A40, 1.5), new Rect(x - w / 2, y - l / 2, w, l * 0.32), 5, 5);
                dc.DrawRectangle(Brush(0xFF6AA8E0), null, new Rect(x - w / 2 + 4 * k, y - l / 2 + 4 * k, w - 8 * k, 7 * k), 2, 2);
                dc.DrawRectangle(Brush(0xFFE0B02A), Pen(0xFF6A5010, 1.5), new Rect(x - w / 2, y - l / 2 + l * 0.34, w, l * 0.66), 3, 3);
                for (int i = 1; i < 4; i++) dc.DrawLine(Pen(0xFF8A6A1A, 1.5), new Point(x - w / 2, y - l / 2 + l * (0.34 + i * 0.16)), new Point(x + w / 2, y - l / 2 + l * (0.34 + i * 0.16)));
                break;
            case CarKind.Racer:
                dc.DrawRectangle(Brush(0xFF2AB04A), Pen(0xFF0A4A1A, 1.5), new Rect(x - w / 2, y - l / 2, w, l), 9, 9);
                dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x - 4 * k, y - l / 2, 8 * k, l));
                dc.DrawRectangle(Brush(0xFF1A2A3A), null, new Rect(x - w / 2 + 4 * k, y - l / 2 + 8 * k, w - 8 * k, 8 * k), 2, 2);
                break;
            default:
            {
                uint[] colours = [0xFFE03A3A, 0xFFF0D040, 0xFFF0F0F0, 0xFFE07A2A, 0xFF8A6AE0];
                uint body = colours[look % (uint)colours.Length];
                dc.DrawRectangle(Brush(body), Pen(0xFF2A2A2A, 1.5), new Rect(x - w / 2, y - l / 2, w, l), 7, 7);
                dc.DrawRectangle(Brush(0xFF2A3A4A), null, new Rect(x - w / 2 + 4 * k, y - l / 2 + 8 * k, w - 8 * k, 9 * k), 2, 2);
                dc.DrawRectangle(Brush(0xFF2A3A4A), null, new Rect(x - w / 2 + 4 * k, y + l / 2 - 14 * k, w - 8 * k, 6 * k), 2, 2);
                break;
            }
        }
        dc.FillRectangle(Brush(0xFFFF3A2A), new Rect(x - w / 2 + 3 * k, y + l / 2 - 3 * k, 6 * k, 3 * k));
        dc.FillRectangle(Brush(0xFFFF3A2A), new Rect(x + w / 2 - 9 * k, y + l / 2 - 3 * k, 6 * k, 3 * k));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, Sy(e.D));
        switch (e.Kind)
        {
            case EffectKind.Blast:
            {
                double r = (12 + 46 * t) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFF3A3A3A, 0.5 * (1 - t))), null, new Point(c.X, c.Y - t * 20), r * 1.1, r);
                dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 1 - t)), null, c, r * 0.8, r * 0.7);
                dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 1 - t)), null, c, r * 0.4, r * 0.35);
                break;
            }
            case EffectKind.Splash:
                for (int i = 0; i < 10; i++)
                {
                    double a = i * Math.PI / 5, r = 10 + 40 * t;
                    dc.DrawEllipse(Brush(Alpha(0xFFC8E8FF, 1 - t)), null, new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r * 0.6 - 20 * Math.Sin(Math.PI * t)), 5, 5);
                }
                dc.DrawEllipse(null, Pen(Alpha(0xFFFFFFFF, 1 - t), 2), c, 20 + 40 * t, 12 + 24 * t);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 26), 24, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, Field.Width, 62));
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 32, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 44), 28, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"STAGE {g.Wave}  {g.Course.Name}", new Point(512, 16), 24, 0xFF8AD8FF, center: true);
        // How far to the finish, and how fast.
        double frac = Math.Clamp(g.Player.D / g.Course.Length, 0, 1);
        dc.DrawRectangle(null, Pen(0xFF8AD8FF, 1.5), new Rect(412, 36, 200, 8), 2, 2);
        dc.FillRectangle(Brush(0xFF8AD8FF), new Rect(414, 38, 196 * frac, 4));
        double sp = g.Player.Speed / GameEngine.MaxSpeed;
        dc.DrawRectangle(null, Pen(0xFFFFB04A, 1.5), new Rect(412, 49, 200, 8), 2, 2);
        dc.FillRectangle(Brush(sp > 0.85 ? Red : 0xFFFFB04A), new Rect(414, 51, 196 * sp, 4));
        Text(dc, $"{g.Player.Speed * 0.4:0} KM/H", new Point(620, 53), 18, 0xFFFFB04A);
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++)
        {
            double x = 200 + i * 24;
            dc.DrawRectangle(Brush(0xFF2A6AE0), null, new Rect(x - 6, 32, 12, 22), 4, 4);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 220;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xD0101828), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(0xFF8AD8FF, 2), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 0, Field.Width, Field.Height));
        string title = "AVA BUMP 'N' JUMP";
        uint[] hues = [0xFF2A6AE0, 0xFFFFE04A, 0xFFE03A3A, 0xFF2AB04A];
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 23 + 23 + i * 46, 100 + Math.Sin(_clock * 3 + i * 0.5) * 4), 92, hues[(i + (int)(_clock * 2)) % hues.Length], center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 160), 28, Dim, center: true);
        // The buggy leaping a gap of water, over and over.
        double t = (_clock * 0.6) % 1;
        dc.FillRectangle(Brush(0xFF2A6AB0), new Rect(cx - 220, 230, 440, 70));
        dc.FillRectangle(Brush(0xFF55555E), new Rect(cx - 220, 300, 440, 60));
        dc.FillRectangle(Brush(0xFF55555E), new Rect(cx - 220, 170, 440, 60));
        DrawBuggyAt(dc, cx - 160 + t * 320, 265, Math.Sin(Math.PI * t), 300);
        Text(dc, "BUMP THE CARS OFF THE ROAD, OR JUMP AND LAND ON THEM - AND LEAP THE WATER", new Point(cx, 400), 22, White, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("SALOON", "BUMP 500 - LAND 800", x => DrawCar(dc, CarKind.Sedan, x, 470, 0, 0, _clock)),
            ("HOT ROD", "BUMP 800 - LAND 800", x => DrawCar(dc, CarKind.Racer, x, 470, 0, 0, _clock)),
            ("LORRY", "LAND ON IT 1500", x => DrawCar(dc, CarKind.Truck, x, 470, 0, 0, _clock)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 260 + i * 260;
            legend[i].Draw(x);
            Text(dc, legend[i].Name, new Point(x, 528), 22, 0xFF8AD8FF, center: true);
            Text(dc, legend[i].Points, new Point(x, 554), 20, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 616), 46, Yellow, center: true);
        Text(dc, "LEFT / RIGHT: STEER    UP / DOWN: FASTER / SLOWER    SPACE: JUMP", new Point(cx, 676), 22, Dim, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA LIFE EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK, TRIGGERS, A", new Point(cx, 706), 21, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF101828), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "ROAD KINGS", new Point(cx, 140), 68, 0xFF8AD8FF, center: true);
        Text(dc, "RANK NAME    SCORE   STAGE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF101828), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
