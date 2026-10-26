using System.Windows;
using System.Windows.Media;
using ThodMonitor.Services;

namespace ThodMonitor.Visuals;

/// <summary>Собственный набор линейных векторных иконок (сетка 24×24, одинаковая толщина линий).</summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["cpu"] = "M7 7h10v10H7z M10 10h4v4h-4z M9 3v4 M15 3v4 M9 17v4 M15 17v4 M3 9h4 M3 15h4 M17 9h4 M17 15h4",
        ["gpu"] = "M3 7h18v10H3z M9.5 12a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0 -5 0 M6 17v3 M18 17v3",
        ["vram"] = "M3 7h18v10H3z M7 10.5h3v3H7z M14 10.5h3v3h-3z M6 17v2.5 M10 17v2.5 M14 17v2.5 M18 17v2.5",
        ["clock"] = "M4 12a8 8 0 1 0 16 0a8 8 0 1 0 -16 0 M12 8v4l3 2",
        ["ram"] = "M2 8h20v8H2z M6 11v2 M10 11v2 M14 11v2 M18 11v2 M5 16v2 M9 16v2 M15 16v2 M19 16v2",
        ["disk"] = "M3 6h18v12H3z M3 13h18 M16.5 16h1.5",
        ["net"] = "M8 5v14 M4 15l4 4 4-4 M16 19V5 M12 9l4-4 4 4",
        ["thermo"] = "M10 14.5V5a2 2 0 0 1 4 0v9.5a4 4 0 1 1 -4 0z",
        ["bolt"] = "M13 3L5 14h6l-1 7 8-11h-6z",
        ["fps"] = "M3 5h18v11H3z M8 20h8 M12 16v4",
        ["warn"] = "M12 4L3 20h18z M12 10v4 M12 17v0.5",
        ["home"] = "M4 11l8-7 8 7 M6 10v10h12V10 M10 20v-6h4v6",
        ["layers"] = "M12 3l9 5-9 5-9-5z M3 13l9 5 9-5",
        ["edge"] = "M3 4h18v16H3z M15 4v16 M12 10l-2 2 2 2",
        ["bell"] = "M6 16v-5a6 6 0 0 1 12 0v5l2 2H4z M10 21h4",
        ["gear"] = "M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M12 3v3 M12 18v3 M3 12h3 M18 12h3 M5.6 5.6l2.1 2.1 M16.3 16.3l2.1 2.1 M18.4 5.6l-2.1 2.1 M7.7 16.3l-2.1 2.1",
        ["pulse"] = "M3 12h4l3-7 4 14 3-7h4",
    };

    private static readonly Dictionary<string, Geometry?> Cache = new();

    public static Geometry? Get(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Cache.TryGetValue(name, out var g)) return g;
        try
        {
            g = Paths.TryGetValue(name, out var p) ? Geometry.Parse(p) : null;
            g?.Freeze();
        }
        catch (Exception ex) { Log.Error("Неверная геометрия иконки " + name, ex); g = null; }
        Cache[name] = g;
        return g;
    }

    public static void Draw(DrawingContext dc, string? name, Rect box, Brush stroke, double thickness = 1.7)
    {
        var geo = Get(name);
        if (geo == null || box.Width <= 0) return;
        var pen = new Pen(stroke, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform(box.X, box.Y));
        dc.PushTransform(new ScaleTransform(box.Width / 24.0, box.Height / 24.0));
        dc.DrawGeometry(null, pen, geo);
        dc.Pop();
        dc.Pop();
    }
}
