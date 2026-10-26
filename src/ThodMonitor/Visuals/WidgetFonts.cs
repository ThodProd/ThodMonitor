using System.Windows;
using System.Windows.Media;

namespace ThodMonitor.Visuals;

/// <summary>Шрифты и начертания, доступные для оверлеев и панели.</summary>
public static class WidgetFonts
{
    public static readonly (string Key, string Title, string Stack)[] All =
    {
        ("Modern", "Современный", "Segoe UI Variable Display, Segoe UI"),
        ("Segoe", "Обычный", "Segoe UI"),
        ("Arial", "Arial", "Arial"),
        ("Tahoma", "Tahoma", "Tahoma"),
        ("Verdana", "Verdana", "Verdana"),
        ("Bahnschrift", "Bahnschrift", "Bahnschrift, Segoe UI"),
        ("Consolas", "Моноширинный", "Consolas"),
    };

    public static bool IsValid(string key) => All.Any(f => f.Key == key);
    public static FontFamily Family(string key) => new((All.FirstOrDefault(f => f.Key == key).Stack ?? All[0].Stack));
    public static FontWeight Weight(string key) => key switch { "Regular" => FontWeights.Normal, "Bold" => FontWeights.Bold, _ => FontWeights.SemiBold };
}
