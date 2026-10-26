using System.Windows;
using ThodMonitor.Models;

namespace ThodMonitor.Services;

/// <summary>Готовые раскладки оверлеев. Координаты считаются от рабочей области основного монитора.</summary>
public static class PresetService
{
    public static readonly (string Key, string Title)[] All =
    {
        ("Circles", "Круги"), ("Game", "Игровой"), ("Minimal", "Минимал"), ("Classic", "Классика"), ("TwoCards", "CPU и GPU"),
    };

    public static void Apply(AppSettings s, string name)
    {
        var wa = SystemParameters.WorkArea;
        const double m = 24;
        void Set(string id, bool on, double x, double y)
        {
            var w = s.Widgets.First(i => i.Id == id);
            w.Enabled = on; w.X = x; w.Y = y;
        }

        // базовые позиции для всех (чтобы включённый позже виджет не оказался в (0,0))
        Set("system", false, wa.Left + m, wa.Top + m);
        Set("cpu", false, wa.Left + m, wa.Top + 150);
        Set("gpu", false, wa.Left + m, wa.Top + 300);
        Set("ram", false, wa.Left + m, wa.Top + 450);
        Set("fps", false, wa.Right - 250, wa.Top + m);
        Set("classic", false, wa.Left + m, wa.Top + 600);

        switch (name)
        {
            case "Circles":
                Set("system", true, wa.Left + m, wa.Top + m);
                Set("fps", true, wa.Right - 250, wa.Top + m);
                break;
            case "Game":
                Set("fps", true, wa.Left + m, wa.Top + m);
                Set("cpu", true, wa.Left + m, wa.Top + 100);
                Set("gpu", true, wa.Left + m, wa.Top + 250);
                break;
            case "Minimal":
                Set("fps", true, wa.Right - 250, wa.Top + m);
                break;
            case "Classic":
                Set("classic", true, wa.Left + m, wa.Top + m);
                break;
            case "TwoCards":
                Set("cpu", true, wa.Left + m, wa.Top + m);
                Set("gpu", true, wa.Right - 320, wa.Top + m);
                break;
        }
    }
}
