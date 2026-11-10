using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Island;

/// <summary>
/// The app's monochrome icon set, drawn on a 24×24 grid in the spirit of SF Symbols: outlines of one weight with round
/// caps and joins, fills only where the symbol is filled by meaning (play, pause, a pinned pin).
/// Each icon is flattened into a single filled outline, so translucent and gradient brushes paint it evenly.
/// </summary>
public static class IconSet
{
    public const double GridSize = 24;
    public const double DefaultWeight = 1.6;
    private const double KnockoutGap = 1.3;
    private const double Tolerance = 0.005;

    private sealed record Part(Geometry Geometry, bool Fill, bool Stroke, double Weight, Geometry? Cut);

    private static readonly Dictionary<string, Part[]> Defs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, int), Geometry> Cache = new();
    private static readonly object Gate = new();

    /// <summary>Glyphs of the old icon font, still found in saved settings (shortcut icons), mapped to icon names.</summary>
    private static readonly Dictionary<char, string> Legacy = new()
    {
        ['\uE721'] = "magnifyingglass", ['\uE8A5'] = "doc", ['\uE718'] = "pin", ['\uE77A'] = "pin.fill", ['\uE842'] = "pin.fill",
        ['\uE74D'] = "trash", ['\uE711'] = "xmark", ['\uEA8F'] = "bell", ['\uE8D6'] = "music.note", ['\uE7BA'] = "exclamationmark.triangle",
        ['\uE80F'] = "house", ['\uE77F'] = "clipboard", ['\uE70F'] = "square.and.pencil", ['\uE706'] = "sun.max",
        ['\uE9E9'] = "slider.horizontal.2", ['\uE713'] = "gearshape", ['\uE892'] = "backward.fill", ['\uE893'] = "forward.fill",
        ['\uE768'] = "play.fill", ['\uE769'] = "pause.fill", ['\uE7A8'] = "crop", ['\uE740'] = "arrows.expand",
        ['\uE8D2'] = "text.viewfinder", ['\uE790'] = "eyedropper", ['\uE8E9'] = "textformat", ['\uE8B7'] = "folder",
        ['\uE767'] = "speaker.wave.2", ['\uE74F'] = "speaker.xmark", ['\uE710'] = "plus", ['\uE8DD'] = "bold", ['\uE8DB'] = "italic",
        ['\uE894'] = "eraser", ['\uE8FD'] = "list.bullet", ['\uE8C8'] = "doc.on.doc", ['\uE73E'] = "checkmark", ['\uE707'] = "location",
        ['\uE72C'] = "arrow.clockwise", ['\uE701'] = "wifi", ['\uE702'] = "bluetooth", ['\uE705'] = "lock.shield", ['\uE72E'] = "lock",
        ['\uE7E8'] = "power", ['\uE71D'] = "square.grid.2x2", ['\uE8BD'] = "bubble.left", ['\uE774'] = "globe", ['\uE823'] = "clock",
        ['\uE896'] = "arrow.down.circle", ['\uE756'] = "terminal", ['\uE8EF'] = "calculator",
    };

    public static IReadOnlyCollection<string> Names => Defs.Keys;

    /// <summary>Returns the icon name for a name or an old font glyph, or null when there is no such icon.</summary>
    public static string? Resolve(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        var trimmed = kind.Trim();
        if (Defs.ContainsKey(trimmed)) return trimmed;
        return trimmed.Length == 1 && Legacy.TryGetValue(trimmed[0], out var name) ? name : null;
    }

    /// <summary>The icon as one filled outline on the 24×24 grid for the given stroke weight (in grid units).</summary>
    public static Geometry? Outline(string kind, double weight = DefaultWeight)
    {
        var name = Resolve(kind);
        if (name is null) return null;
        var key = (name.ToLowerInvariant(), (int)Math.Round(weight * 100));
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var built = Build(Defs[name], key.Item2 / 100.0);
            Cache[key] = built;
            return built;
        }
    }

    private static Geometry Build(Part[] parts, double weight)
    {
        Geometry? result = null;
        foreach (var part in parts)
        {
            Geometry? piece = null;
            if (part.Fill) piece = part.Geometry.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute);
            if (part.Stroke)
            {
                var widened = part.Geometry.GetWidenedPathGeometry(RoundPen(weight * part.Weight), Tolerance, ToleranceType.Absolute);
                piece = piece is null ? widened : Union(piece, widened);
            }

            if (piece is null) continue;
            if (part.Cut is { } cut)
            {
                var area = Union(cut.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute),
                    cut.GetWidenedPathGeometry(RoundPen(weight + 2 * KnockoutGap), Tolerance, ToleranceType.Absolute));
                piece = Geometry.Combine(piece, area, GeometryCombineMode.Exclude, null, Tolerance, ToleranceType.Absolute);
            }

            result = result is null ? piece : Union(result, piece);
        }

        result ??= Geometry.Empty;
        result.Freeze();
        return result;
    }

    private static Geometry Union(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Union, null, Tolerance, ToleranceType.Absolute);

    private static Pen RoundPen(double thickness) => new(Brushes.Black, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        DashCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };

    // ---------- definitions ----------

    private sealed class Def
    {
        public readonly List<Part> Parts = new();
        public Def S(string data, double weight = 1) => Add(Geo(data), false, true, weight);
        public Def S(Geometry g, double weight = 1) => Add(g, false, true, weight);
        public Def F(string data) => Add(Geo(data), true, false, 1);
        public Def F(Geometry g) => Add(g, true, false, 1);
        /// <summary>A filled shape that is also stroked, so it has the same outer size as its outline twin.</summary>
        public Def SF(string data) => Add(Geo(data), true, true, 1);
        public Def SF(Geometry g) => Add(g, true, true, 1);
        /// <summary>A stroke with a gap cut around <paramref name="cut"/>, the way SF Symbols separates overlapping shapes.</summary>
        public Def SCut(Geometry g, Geometry cut) { Parts.Add(new Part(g, false, true, 1, cut)); return this; }
        private Def Add(Geometry g, bool fill, bool stroke, double weight) { Parts.Add(new Part(g, fill, stroke, weight, null)); return this; }
    }

    private static Def Add(string name)
    {
        var def = new Def();
        Pending.Add((name, def));
        return def;
    }

    private static readonly List<(string Name, Def Def)> Pending = new();

    static IconSet()
    {
        DefineGeneral();
        DefineMedia();
        DefineText();
        DefineSystem();
        DefineWeather();
        foreach (var (name, def) in Pending) Defs[name] = def.Parts.ToArray();
        Pending.Clear();
    }

    private static void DefineGeneral()
    {
        Add("magnifyingglass").S(Circle(10.5, 10.5, 6.75)).S("M15.5,15.5 L20.5,20.5");
        Add("doc")
            .S("M7,2.75 H13.25 L19,8.5 V19.25 A2,2 0 0 1 17,21.25 H7 A2,2 0 0 1 5,19.25 V4.75 A2,2 0 0 1 7,2.75 Z")
            .S("M13.25,2.75 V6.5 A2,2 0 0 0 15.25,8.5 H19");
        Add("doc.on.doc")
            .S(RoundRect(8.75, 7, 11.25, 14.25, 2))
            .S("M15.25,7 V4.75 A2,2 0 0 0 13.25,2.75 H6 A2,2 0 0 0 4,4.75 V15 A2,2 0 0 0 6,17 H8.75");
        Add("clipboard")
            .S("M8.25,4.5 H6.75 A2,2 0 0 0 4.75,6.5 V19.25 A2,2 0 0 0 6.75,21.25 H17.25 A2,2 0 0 0 19.25,19.25 V6.5 A2,2 0 0 0 17.25,4.5 H15.75")
            .S(RoundRect(8.25, 2.75, 7.5, 3.5, 1.25));
        var pin = RoundPoly(0.8, (8.75, 3.25), (15.25, 3.25), (14.25, 9.25), (17.5, 13.5), (6.5, 13.5), (9.75, 9.25));
        Add("pin").S(pin).S("M12,13.5 V20.75");
        Add("pin.fill").SF(pin).S("M12,13.5 V20.75");
        Add("trash")
            .S("M4,6.25 H20")
            .S("M9,6.25 V4.5 A1.5,1.5 0 0 1 10.5,3 H13.5 A1.5,1.5 0 0 1 15,4.5 V6.25")
            .S("M5.75,6.25 L6.75,19.25 A2,2 0 0 0 8.75,21 H15.25 A2,2 0 0 0 17.25,19.25 L18.25,6.25")
            .S("M10,10.25 V16.75 M14,10.25 V16.75");
        Add("xmark").S("M6.25,6.25 L17.75,17.75 M17.75,6.25 L6.25,17.75");
        Add("plus").S("M12,4.75 V19.25 M4.75,12 H19.25");
        Add("checkmark").S("M4.75,12.75 L9.5,17.25 L19.25,6.75");
        Add("checkmark.square").S(RoundRect(3.5, 3.5, 17, 17, 3.75)).S("M7.75,12.25 L10.75,15.25 L16.25,8.75");
        Add("bell")
            .S("M12,3 C8.9,3 6.75,5.4 6.75,8.6 V12.6 C6.75,14.1 6,15.3 4.6,16.5 C4.25,16.8 4.45,17.5 4.95,17.5 H19.05 C19.55,17.5 19.75,16.8 19.4,16.5 C18,15.3 17.25,14.1 17.25,12.6 V8.6 C17.25,5.4 15.1,3 12,3 Z")
            .S("M9.75,20.25 A2.5,2.5 0 0 0 14.25,20.25");
        Add("exclamationmark.triangle")
            .S(RoundPoly(2.2, (12, 3.25), (21.25, 19.75), (2.75, 19.75)))
            .S("M12,9.25 V13.5")
            .F(Circle(12, 16.6, 1.1));
        Add("house")
            .S("M3.25,11.25 L12,3.75 L20.75,11.25")
            .S("M5.75,9.25 V19 A1.75,1.75 0 0 0 7.5,20.75 H16.5 A1.75,1.75 0 0 0 18.25,19 V9.25")
            .S("M10,20.75 V15.5 A1,1 0 0 1 11,14.5 H13 A1,1 0 0 1 14,15.5 V20.75");
        Add("square.and.pencil")
            .S("M11.25,4.5 H6.75 A2.25,2.25 0 0 0 4.5,6.75 V17.25 A2.25,2.25 0 0 0 6.75,19.5 H17.25 A2.25,2.25 0 0 0 19.5,17.25 V12.75")
            .S(Stick((9.75, 14.25), (19, 5), 1.35, 2.6));
        Add("folder")
            .S("M3,7.25 A2.25,2.25 0 0 1 5.25,5 H9.25 L11.5,7.25 H18.75 A2.25,2.25 0 0 1 21,9.5 V17 A2.25,2.25 0 0 1 18.75,19.25 H5.25 A2.25,2.25 0 0 1 3,17 Z")
            .S("M3,10.25 H21");
        Add("square.grid.2x2")
            .S(RoundRect(3.75, 3.75, 6.75, 6.75, 1.75)).S(RoundRect(13.5, 3.75, 6.75, 6.75, 1.75))
            .S(RoundRect(3.75, 13.5, 6.75, 6.75, 1.75)).S(RoundRect(13.5, 13.5, 6.75, 6.75, 1.75));
        Add("bubble.left")
            .S("M12,3.75 C16.8,3.75 20.5,6.9 20.5,10.85 C20.5,14.8 16.8,17.95 12,17.95 C11.05,17.95 10.15,17.85 9.3,17.6 L5.6,19.9 C5.3,20.1 4.95,19.8 5.05,19.45 L5.95,15.95 C4.4,14.65 3.5,12.85 3.5,10.85 C3.5,6.9 7.2,3.75 12,3.75 Z");
        Add("globe")
            .S(Circle(12, 12, 9))
            .S(Ellipse(12, 12, 3.75, 9))
            .S("M3,12 H21");
        Add("clock").S(Circle(12, 12, 9)).S("M12,6.75 V12 L15.5,14.25");
        Add("arrow.down.circle").S(Circle(12, 12, 9)).S("M12,7.25 V16.25 M8.25,12.75 L12,16.5 L15.75,12.75");
        Add("arrow.clockwise").S("M18.5,9.25 A7.5,7.5 0 1 1 12,5.5").S("M9.5,3 L12.25,5.5 L9.5,8");
        Add("location").S(RoundPoly(0.9, (20.5, 3.5), (13, 20.5), (11.1, 12.9), (3.5, 11)));
    }

    private static void DefineMedia()
    {
        Add("play.fill").SF(RoundPoly(1.6, (7, 4.75), (19.75, 12), (7, 19.25)));
        Add("pause.fill").SF(RoundRect(6.5, 5, 3.5, 14, 1)).SF(RoundRect(14, 5, 3.5, 14, 1));
        Add("stop.fill").SF(RoundRect(6, 6, 12, 12, 2.25));
        Add("record.circle").S(Circle(12, 12, 9)).F(Circle(12, 12, 4.25));
        Add("video")
            .S(RoundRect(2.75, 6.25, 12.75, 11.5, 2.75))
            .S(RoundPoly(0.9, (15.5, 10.5), (21.25, 7.25), (21.25, 16.75), (15.5, 13.5)));
        Add("forward.fill")
            .SF(RoundPoly(1, (3.5, 6.75), (11.75, 12), (3.5, 17.25)))
            .SF(RoundPoly(1, (12.25, 6.75), (20.5, 12), (12.25, 17.25)));
        Add("backward.fill")
            .SF(RoundPoly(1, (20.5, 6.75), (12.25, 12), (20.5, 17.25)))
            .SF(RoundPoly(1, (11.75, 6.75), (3.5, 12), (11.75, 17.25)));
        Add("music.note")
            .SF(Ellipse(8.75, 17.5, 2.85, 2.2))
            .S("M11.6,17.5 V3.5 L18.75,5.25 V9 L11.6,7.25");
        const string speaker = "M3.75,9.75 A1.25,1.25 0 0 1 5,8.5 H7.75 L11.75,4.75 V19.25 L7.75,15.5 H5 A1.25,1.25 0 0 1 3.75,14.25 Z";
        Add("speaker.wave.2").S(speaker).S("M15,9.25 A4,4 0 0 1 15,14.75").S("M17.75,6.5 A7.75,7.75 0 0 1 17.75,17.5");
        Add("speaker.xmark").S(speaker).S("M15.25,9.5 L20.25,14.5 M20.25,9.5 L15.25,14.5");
    }

    private static void DefineText()
    {
        Add("bold").S("M7.25,4.75 H12.6 A3.6,3.6 0 0 1 12.6,11.95 H7.25 Z M7.25,11.95 H13.3 A3.65,3.65 0 0 1 13.3,19.25 H7.25 Z", 1.3);
        Add("italic").S("M10.25,4.75 H17.25 M6.75,19.25 H13.75 M13.75,4.75 L10.25,19.25");
        Add("list.bullet")
            .F(Circle(4.75, 6.5, 1.3)).F(Circle(4.75, 12, 1.3)).F(Circle(4.75, 17.5, 1.3))
            .S("M9,6.5 H20.25 M9,12 H20.25 M9,17.5 H20.25");
        Add("eraser")
            .S(RoundPoly(1.4, (14.5, 3.5), (20.5, 9.5), (11, 19), (5, 13)))
            .S("M8.35,9.65 L14.35,15.65")
            .S("M12.75,20.75 H20.25");
        Add("textformat").S("M2.5,19 L7.75,5.5 L13,19 M4.5,14.5 H11").S(Circle(17.5, 16, 3)).S("M20.5,13 V19");
        Add("text.viewfinder")
            .S("M3.25,8 V5.75 A2.5,2.5 0 0 1 5.75,3.25 H8 M16,3.25 H18.25 A2.5,2.5 0 0 1 20.75,5.75 V8 M20.75,16 V18.25 A2.5,2.5 0 0 1 18.25,20.75 H16 M8,20.75 H5.75 A2.5,2.5 0 0 1 3.25,18.25 V16")
            .S("M7.75,8.75 H16.25 M7.75,12 H16.25 M7.75,15.25 H12.75");
    }

    private static void DefineSystem()
    {
        Add("gearshape").S(Gear(12, 12, 7.1, 9.1, 8, 7.5, 12)).S(Circle(12, 12, 3));
        Add("slider.horizontal.2")
            .S("M3.5,7.5 H11.25").S(Circle(16.75, 7.5, 2.75))
            .S("M12.75,16.5 H20.5").S(Circle(7.25, 16.5, 2.75));
        Add("crop").S("M6.75,2.75 V15.25 A2,2 0 0 0 8.75,17.25 H21.25").S("M2.75,6.75 H15.25 A2,2 0 0 1 17.25,8.75 V21.25");
        Add("arrows.expand").S("M14.25,3.75 H20.25 V9.75 M20.25,3.75 L14,10 M9.75,20.25 H3.75 V14.25 M3.75,20.25 L10,14");
        Add("eyedropper")
            .S("M16.76,10.06 L19.59,7.23 A2,2 0 0 0 16.77,4.41 L13.94,7.24")
            .S("M12.2,6.2 L17.8,11.8")
            .S("M15.5,10.2 L8.8,16.9 L5.5,18.5 L7.1,15.2 L13.8,8.5");
        Add("wifi")
            .S("M3.51,9.01 A12,12 0 0 1 20.49,9.01 M6.34,11.84 A8,8 0 0 1 17.66,11.84 M9.17,14.67 A4,4 0 0 1 14.83,14.67")
            .F(Circle(12, 17.75, 1.55));
        Add("bluetooth").S("M6.75,7.5 L16.75,16.25 L12,20.75 V3.25 L16.75,7.75 L6.75,16.5");
        Add("lock.shield")
            .S("M12,2.75 L19.25,5.5 V11 C19.25,15.6 16.3,19.2 12,21.25 C7.7,19.2 4.75,15.6 4.75,11 V5.5 Z")
            .SF(RoundRect(9.5, 11, 5, 4, 0.6))
            .S("M10.5,11 V9.5 A1.5,1.5 0 0 1 13.5,9.5 V11");
        Add("lock").S(RoundRect(5.25, 10.25, 13.5, 10.5, 2.25)).S("M8.25,10.25 V7.25 A3.75,3.75 0 0 1 15.75,7.25 V10.25");
        Add("power").S("M12,3 V11").S("M7.6,6.3 A8,8 0 1 0 16.4,6.3");
        Add("circle.lefthalf.filled").S(Circle(12, 12, 9)).F("M12,3 A9,9 0 0 0 12,21 Z");
        Add("moon").S(MoonPath);
        Add("terminal").S(RoundRect(2.75, 4.25, 18.5, 15.5, 3)).S("M7,9.25 L10,12 L7,14.75").S("M12.25,15 H17");
        Add("calculator")
            .S(RoundRect(5, 2.75, 14, 18.5, 2.75))
            .S(RoundRect(8, 5.75, 8, 3.5, 0.75))
            .F(Circle(9.25, 13.25, 1.15)).F(Circle(14.75, 13.25, 1.15))
            .F(Circle(9.25, 17.25, 1.15)).F(Circle(14.75, 17.25, 1.15));
    }

    private const string MoonPath = "M19.75,14.5 A8.5,8.5 0 1 1 9.5,4.25 A7.6,7.6 0 0 0 19.75,14.5 Z";

    /// <summary>Cloud outline built from three lobes; spans x 3.5–21.75 and y 5–18.5 before placing.</summary>
    private const string CloudPath = "M7.5,18.5 A4,4 0 0 1 7.0,10.53 A5.5,5.5 0 0 1 17.81,9.07 A4.75,4.75 0 0 1 17,18.5 Z";

    private static void DefineWeather()
    {
        Add("sun.max").S(Circle(12, 12, 4.25)).S(Rays(12, 12, 7.25, 9.5, 8));
        // "moon" is defined with the system icons.
        Add("cloud").S(Place(CloudPath, 1, -0.625, 0.25));

        var cloudTop = Place(CloudPath, 0.88, 0.89, -1.4);
        Add("cloud.drizzle").S(cloudTop).S("M8.25,17.25 L7.75,18.75 M12.25,17.25 L11.75,18.75 M16.25,17.25 L15.75,18.75 M10.25,20.5 L9.75,22 M14.25,20.5 L13.75,22");
        Add("cloud.rain").S(cloudTop).S("M8.5,17.25 L7.25,21.25 M12.5,17.25 L11.25,21.25 M16.5,17.25 L15.25,21.25");
        Add("cloud.snow").S(cloudTop)
            .F(Circle(8, 18, 1.15)).F(Circle(12, 18, 1.15)).F(Circle(16, 18, 1.15))
            .F(Circle(10, 21.25, 1.15)).F(Circle(14, 21.25, 1.15));
        Add("cloud.fog").S(cloudTop).S("M4.75,18 H19.25 M7,21.25 H17");
        var bolt = Geo(RoundPoly(0.45, (13.5, 12.5), (9.25, 18.25), (12, 18.25), (10.75, 22.5), (15.75, 15.75), (12.75, 15.75), (14.75, 12.5)));
        Add("cloud.bolt").SCut(cloudTop, bolt).S(bolt);

        var smallCloud = Place(CloudPath, 0.78, -0.23, 5.32);
        var sun = new GeometryGroup();
        sun.Children.Add(Geo(Circle(15.25, 8.5, 3.1)));
        sun.Children.Add(Geo(Rays(15.25, 8.5, 5.2, 6.85, 8)));
        Add("cloud.sun").S(smallCloud).SCut(sun, smallCloud);
        var moon = Place(MoonPath, 0.55, 9.95, 0.6);
        Add("cloud.moon").S(smallCloud).SCut(moon, smallCloud);
    }

    // ---------- geometry helpers ----------

    private static string N(double v) => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);

    private static Geometry Geo(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    private static Geometry Place(string data, double scale, double dx, double dy)
    {
        var g = Geometry.Parse(data).Clone();
        g.Transform = new MatrixTransform(scale, 0, 0, scale, dx, dy);
        // Flatten the transform into the path so later widening uses the pen at full grid weight.
        var flat = PathGeometry.CreateFromGeometry(g);
        flat.Freeze();
        return flat;
    }

    private static string Circle(double cx, double cy, double r) => Ellipse(cx, cy, r, r);

    private static string Ellipse(double cx, double cy, double rx, double ry) =>
        $"M{N(cx + rx)},{N(cy)} A{N(rx)},{N(ry)} 0 1 1 {N(cx - rx)},{N(cy)} A{N(rx)},{N(ry)} 0 1 1 {N(cx + rx)},{N(cy)} Z";

    private static string RoundRect(double x, double y, double w, double h, double r) =>
        $"M{N(x + r)},{N(y)} H{N(x + w - r)} A{N(r)},{N(r)} 0 0 1 {N(x + w)},{N(y + r)} V{N(y + h - r)} " +
        $"A{N(r)},{N(r)} 0 0 1 {N(x + w - r)},{N(y + h)} H{N(x + r)} A{N(r)},{N(r)} 0 0 1 {N(x)},{N(y + h - r)} " +
        $"V{N(y + r)} A{N(r)},{N(r)} 0 0 1 {N(x + r)},{N(y)} Z";

    /// <summary>Closed polygon whose corners are softened with quadratic curves of the given reach.</summary>
    private static string RoundPoly(double radius, params (double X, double Y)[] pts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pts.Length; i++)
        {
            var v = pts[i];
            var prev = pts[(i + pts.Length - 1) % pts.Length];
            var next = pts[(i + 1) % pts.Length];
            var a = Toward(v, prev, radius);
            var b = Toward(v, next, radius);
            sb.Append(i == 0 ? "M" : " L").Append(N(a.X)).Append(',').Append(N(a.Y));
            sb.Append(" Q").Append(N(v.X)).Append(',').Append(N(v.Y)).Append(' ').Append(N(b.X)).Append(',').Append(N(b.Y));
        }

        return sb.Append(" Z").ToString();
    }

    private static (double X, double Y) Toward((double X, double Y) from, (double X, double Y) to, double distance)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var t = Math.Min(distance / len, 0.5);
        return (from.X + dx * t, from.Y + dy * t);
    }

    private static string Rays(double cx, double cy, double inner, double outer, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var angle = i * 2 * Math.PI / count;
            var (c, s) = (Math.Cos(angle), Math.Sin(angle));
            sb.Append($"M{N(cx + inner * c)},{N(cy + inner * s)} L{N(cx + outer * c)},{N(cy + outer * s)} ");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>A pencil: a stick from <paramref name="tip"/> to <paramref name="end"/> with a pointed tip.</summary>
    private static string Stick((double X, double Y) tip, (double X, double Y) end, double halfWidth, double tipLength)
    {
        var (dx, dy) = (end.X - tip.X, end.Y - tip.Y);
        var len = Math.Sqrt(dx * dx + dy * dy);
        var (ux, uy) = (dx / len, dy / len);
        var (nx, ny) = (-uy, ux);
        var shoulder = (tip.X + ux * tipLength, tip.Y + uy * tipLength);
        return RoundPoly(0.7,
            tip,
            (shoulder.Item1 + nx * halfWidth, shoulder.Item2 + ny * halfWidth),
            (end.X + nx * halfWidth, end.Y + ny * halfWidth),
            (end.X - nx * halfWidth, end.Y - ny * halfWidth),
            (shoulder.Item1 - nx * halfWidth, shoulder.Item2 - ny * halfWidth));
    }

    /// <summary>Gear outline: <paramref name="teeth"/> teeth between two radii, tooth top and base half-widths in degrees.</summary>
    private static string Gear(double cx, double cy, double inner, double outer, int teeth, double topHalf, double baseHalf)
    {
        var pts = new List<(double, double)>();
        for (var i = 0; i < teeth; i++)
        {
            var mid = -90 + i * 360.0 / teeth;
            foreach (var (deg, r) in new[] { (mid - baseHalf, inner), (mid - topHalf, outer), (mid + topHalf, outer), (mid + baseHalf, inner) })
            {
                var rad = deg * Math.PI / 180;
                pts.Add((cx + r * Math.Cos(rad), cy + r * Math.Sin(rad)));
            }
        }

        return RoundPoly(0.6, pts.ToArray());
    }
}
