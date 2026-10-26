using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ThodMonitor.Models;
using ThodMonitor.Visuals;

namespace ThodMonitor.ViewModels;

public sealed partial class MetricCardViewModel : ObservableObject
{
    private const int MaxPoints = 60;          // ограниченный буфер для мини-графика
    private const double W = 170, H = 32;
    private readonly List<double?> _hist = new();
    private readonly AppSettings _s;

    public MetricCardViewModel(MetricDef def, AppSettings s) { Def = def; _s = s; RefreshThresholds(); }

    public MetricDef Def { get; }
    public string Id => Def.Id;
    public string Title => Def.Title;
    public string Unit => Def.Unit;

    [ObservableProperty] private string valueText = "…";
    [ObservableProperty] private string unitText = "";
    [ObservableProperty] private string ringText = "…";
    [ObservableProperty] private string? note;
    [ObservableProperty] private double numericValue;
    [ObservableProperty] private bool isAvailable;
    [ObservableProperty] private double warn = 60;
    [ObservableProperty] private double crit = 85;
    [ObservableProperty] private Brush stateBrush = ColorScale.Brush(ColorScale.Dim);
    [ObservableProperty] private PointCollection points = new();
    [ObservableProperty] private bool isInOsd;

    public event Action<MetricCardViewModel>? OsdToggled;
    partial void OnIsInOsdChanged(bool value) => OsdToggled?.Invoke(this);

    /// <summary>Пересчитывает пороги цветов из настроек (вызывается при их изменении).</summary>
    public void RefreshThresholds()
    {
        if (Def.Unit == "%") { Warn = _s.LoadWarn; Crit = _s.LoadCrit; }
        else if (Def.Unit == "°C") { Warn = _s.TempWarn; Crit = _s.TempCrit; }
        Recolor();
    }

    private void Recolor() =>
        StateBrush = ColorScale.Brush(IsAvailable ? ColorScale.For(Def, NumericValue, _s) : ColorScale.Dim);

    public void Update(double? v, string? noteText)
    {
        IsAvailable = v.HasValue;
        NumericValue = v ?? 0;
        Note = noteText;
        ValueText = v.HasValue ? v.Value.ToString(Def.Unit == "МБ/с" ? "0.00" : Def.Unit == "мс" ? "0.0" : "0") : "Н/Д";
        UnitText = v.HasValue ? Def.Unit : "";
        RingText = !v.HasValue ? "Н/Д" : Def.Unit == "%" ? $"{v:0}%" : Def.Unit == "°C" ? $"{v:0}°" : ValueText;
        Recolor();

        _hist.Add(v);
        if (_hist.Count > MaxPoints) _hist.RemoveAt(0);
        RebuildPoints();
    }

    private void RebuildPoints()
    {
        double max = Def.FixedMax ??
                     Math.Max(1, _hist.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(1).Max());
        var pc = new PointCollection();
        double step = W / (MaxPoints - 1);
        int offset = MaxPoints - _hist.Count;
        for (int i = 0; i < _hist.Count; i++)
        {
            if (_hist[i] is not double d) continue;
            pc.Add(new Point((offset + i) * step, H - 1 - Math.Min(d, max) / max * (H - 2)));
        }
        Points = pc;
    }
}
