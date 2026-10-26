using System.Windows.Media;
using ThodMonitor.Models;

namespace ThodMonitor.Visuals;

/// <summary>Плавная шкала «зелёный → жёлтый → красный».</summary>
public static class ColorScale
{
    public static readonly Color Green = Color.FromRgb(0x3F, 0xD1, 0x7F);
    public static readonly Color Yellow = Color.FromRgb(0xF2, 0xB8, 0x4B);
    public static readonly Color Red = Color.FromRgb(0xF0, 0x61, 0x5D);
    public static readonly Color Neutral = Color.FromRgb(0xE8, 0xEC, 0xF4);
    public static readonly Color Dim = Color.FromRgb(0x5A, 0x61, 0x70);

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Больше — хуже (загрузка, температура).</summary>
    public static Color HighIsBad(double v, double warn, double crit)
    {
        double band = Math.Min(10, warn * 0.5);
        if (v <= warn - band) return Green;
        if (v < warn) return Lerp(Green, Yellow, (v - (warn - band)) / band);
        if (v < crit) return Lerp(Yellow, Red, (v - warn) / (crit - warn));
        return Red;
    }

    /// <summary>Меньше — хуже (FPS).</summary>
    public static Color LowIsBad(double v, double good, double bad)
    {
        if (v >= good) return Green;
        if (v <= bad) return Red;
        double t = (v - bad) / (good - bad);
        return t > 0.5 ? Lerp(Yellow, Green, (t - 0.5) * 2) : Lerp(Red, Yellow, t * 2);
    }

    public static Color For(MetricDef def, double v, AppSettings s)
    {
        if (def.Id is "fps.now" or "fps.avg" or "fps.low") return LowIsBad(v, s.FpsGood, s.FpsBad);
        if (def.Id == "fps.ft") return v > 0 ? LowIsBad(1000.0 / v, s.FpsGood, s.FpsBad) : Dim;
        if (def.Unit == "%") return HighIsBad(v, s.LoadWarn, s.LoadCrit);
        if (def.Unit == "°C") return HighIsBad(v, s.TempWarn, s.TempCrit);
        return Neutral;
    }

    public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
