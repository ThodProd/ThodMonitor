using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using ThodMonitor.Visuals;

namespace ThodMonitor.Models;

public static class WidgetCatalog
{
    public static readonly (string Id, string Title)[] All =
    {
        ("system",  "Системная панель (круги CPU · GPU · RAM)"),
        ("cpu",     "Процессор"),
        ("gpu",     "Видеокарта"),
        ("ram",     "Оперативная память"),
        ("fps",     "FPS"),
        ("classic", "Текстовый оверлей (классика)"),
    };
    public static string TitleOf(string id) => All.FirstOrDefault(x => x.Id == id).Title ?? id;
}

/// <summary>Настройки одного оверлея-виджета.</summary>
public sealed partial class WidgetConfig : ObservableObject
{
    public string Id { get; set; } = "";
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private double? x;
    [ObservableProperty] private double? y;
    [ObservableProperty] private double scale = 1;
    [ObservableProperty] private double opacity = 1;
    [ObservableProperty] private double bgOpacity = 0.8;
    [ObservableProperty] private string layer = "Top";           // Top | Desktop
    [ObservableProperty] private string colorMode = "Traffic";   // Traffic | Accent
    [ObservableProperty] private string accent = "#4F8CFF";
    [ObservableProperty] private double padding = 14;            // внутренний отступ, px
    [ObservableProperty] private string variant = "Full";        // только для FPS: Number | NumberLabel | Compact | Line | Full
    [ObservableProperty] private string font = "Modern";         // см. WidgetFonts
    [ObservableProperty] private string weight = "SemiBold";     // Regular | SemiBold | Bold
    [ObservableProperty] [property: JsonIgnore] private bool extrasOpen;   // только для интерфейса

    /// <summary>Дополнительные показатели мелкими строками под основным.</summary>
    public ObservableCollection<string> Extras { get; set; } = new();

    private ObservableCollection<ExtraOption>? _choices;
    private bool _building;

    [JsonIgnore]
    public ObservableCollection<ExtraOption> ExtraChoices => _choices ??= BuildChoices();

    private ObservableCollection<ExtraOption> BuildChoices()
    {
        var list = new ObservableCollection<ExtraOption>();
        foreach (var m in MetricCatalog.All)
            list.Add(new ExtraOption(m.Id, MetricLabels.Caption(m.Id), Extras.Contains(m.Id), o =>
            {
                if (_building) return;
                if (o.IsChecked) { if (!Extras.Contains(o.Id)) Extras.Add(o.Id); }
                else Extras.Remove(o.Id);
            }));
        Extras.CollectionChanged += (_, _) =>
        {
            _building = true;
            foreach (var o in list) o.IsChecked = Extras.Contains(o.Id);
            _building = false;
        };
        return list;
    }

    [JsonIgnore] public string Title => WidgetCatalog.TitleOf(Id);

    public void ResetStyle()
    {
        Scale = 1; Opacity = 1; BgOpacity = 0.8; Layer = "Top"; ColorMode = "Traffic"; Accent = "#4F8CFF";
        Padding = 14; Variant = "Full";
        Font = Id == "classic" ? "Consolas" : "Modern"; Weight = "SemiBold";
        Extras.Clear();
    }
}

/// <summary>Один блок выдвижной панели: основной показатель, вид и дополнительные показатели.</summary>
public sealed partial class EdgeBlock : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    [ObservableProperty] private string primary = "cpu.load";
    [ObservableProperty] private string style = "Ring";     // Ring | Number | Bar | Line
    [ObservableProperty] private string title = "";         // пусто = стандартная русская подпись
    [ObservableProperty] private bool showTitle = true;
    [ObservableProperty] private string size = "M";         // S | M | L
    public ObservableCollection<string> Extras { get; set; } = new();

    public static EdgeBlock Make(string primary, string style, params string[] extras)
    {
        var b = new EdgeBlock { Primary = primary, Style = style };
        foreach (var e in extras) b.Extras.Add(e);
        return b;
    }

    public static List<EdgeBlock> Defaults() => new()
    {
        Make("gpu.load", "Ring", "gpu.temp", "gpu.vram"),   // видеопамять — строкой под загрузкой
        Make("cpu.load", "Ring", "cpu.temp", "cpu.power"),
        Make("ram.load", "Ring"),
        Make("fps.now", "Number", "fps.low", "fps.ft"),
    };
}

/// <summary>Выдвижная панель у края экрана.</summary>
public sealed partial class EdgeConfig : ObservableObject
{
    [ObservableProperty] private bool blocksSeeded;
    [ObservableProperty] private string font = "Modern";
    [ObservableProperty] private string weight = "SemiBold";
    public ObservableCollection<EdgeBlock> Blocks { get; set; } = new();

    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string edge = "Right";      // Left | Right | Top | Bottom
    [ObservableProperty] private double zonePx = 6;
    [ObservableProperty] private double showDelayMs = 150;
    [ObservableProperty] private double hideDelayMs = 600;
    [ObservableProperty] private double animMs = 300;
    [ObservableProperty] private double opacity = 0.97;
    [ObservableProperty] private double scale = 1;
    [ObservableProperty] private bool ignoreFullscreen;

    public void ResetToDefaults()
    {
        var d = new EdgeConfig();
        Enabled = d.Enabled; Edge = d.Edge; ZonePx = d.ZonePx; ShowDelayMs = d.ShowDelayMs; HideDelayMs = d.HideDelayMs;
        AnimMs = d.AnimMs; Opacity = d.Opacity; Scale = d.Scale; IgnoreFullscreen = d.IgnoreFullscreen;
        Font = d.Font; Weight = d.Weight;
        Blocks.Clear();
        foreach (var b in EdgeBlock.Defaults()) Blocks.Add(b);
        BlocksSeeded = true;
    }
}

public sealed partial class AlertRule : ObservableObject
{
    public string Id { get; set; } = "";
    public string Metric { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "warn";
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private double threshold = 90;
    [ObservableProperty] private double durationSec = 15;
    [ObservableProperty] private double repeatMin = 5;

    [JsonIgnore] public string Unit => MetricCatalog.All.FirstOrDefault(m => m.Id == Metric)?.Unit ?? "";
}

public sealed partial class AlertSettings : ObservableObject
{
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string style = "Slide";        // Slide | Fade | Scale | Expand | Glow | Pulse | Minimal
    [ObservableProperty] private string corner = "BottomRight"; // TopLeft | TopRight | BottomLeft | BottomRight
    [ObservableProperty] private double durationSec = 8;
    [ObservableProperty] private double maxCards = 3;
    [ObservableProperty] private bool sound;

    public ObservableCollection<AlertRule> Rules { get; set; } = new();

    public static List<AlertRule> DefaultRules() => new()
    {
        new() { Id = "cpu-load", Metric = "cpu.load", Title = "Процессор перегружен", Icon = "cpu", Threshold = 90, DurationSec = 15, RepeatMin = 5 },
        new() { Id = "cpu-temp", Metric = "cpu.temp", Title = "Перегрев процессора", Icon = "thermo", Threshold = 85, DurationSec = 10, RepeatMin = 5 },
        new() { Id = "gpu-temp", Metric = "gpu.temp", Title = "Перегрев видеокарты", Icon = "thermo", Threshold = 83, DurationSec = 10, RepeatMin = 5 },
        new() { Id = "gpu-vram", Metric = "gpu.vram", Title = "Видеопамять заполнена", Icon = "gpu", Threshold = 95, DurationSec = 10, RepeatMin = 5 },
        new() { Id = "gpu-load", Metric = "gpu.load", Title = "Видеокарта перегружена", Icon = "gpu", Threshold = 99, DurationSec = 60, RepeatMin = 10, Enabled = false },
        new() { Id = "ram-load", Metric = "ram.load", Title = "Заканчивается оперативная память", Icon = "ram", Threshold = 90, DurationSec = 10, RepeatMin = 5 },
        new() { Id = "disk-used", Metric = "disk.used", Title = "Мало места на системном диске", Icon = "disk", Threshold = 95, DurationSec = 5, RepeatMin = 60 },
    };

    public void ResetToDefaults()
    {
        var d = new AlertSettings();
        Enabled = d.Enabled; Style = d.Style; Corner = d.Corner; DurationSec = d.DurationSec; MaxCards = d.MaxCards; Sound = d.Sound;
        Rules.Clear();
        foreach (var r in DefaultRules()) Rules.Add(r);
    }
}

public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty] private int pollIntervalMs = 1000;
    [ObservableProperty] private bool closeToTray = true;
    [ObservableProperty] private bool startMinimized;
    [ObservableProperty] private bool overlaysVisible = true;
    [ObservableProperty] private bool presetApplied;
    [ObservableProperty] private bool showLabels = true;     // мелкие подписи CPU / GPU / VRAM / RAM
    [ObservableProperty] private double osdFontSize = 14;   // текстовый оверлей
    // пороги цвета колец (зелёный → жёлтый → красный)
    [ObservableProperty] private double loadWarn = 60;
    [ObservableProperty] private double loadCrit = 85;
    [ObservableProperty] private double tempWarn = 70;
    [ObservableProperty] private double tempCrit = 85;
    [ObservableProperty] private double fpsGood = 60;
    [ObservableProperty] private double fpsBad = 30;
    // FPS через внешний PresentMon
    [ObservableProperty] private bool fpsEnabled = true;
    [ObservableProperty] private string fpsMetric = "Fps";         // Fps (по времени кадра, подпись «FPS») | FrameTime (мс)
    [ObservableProperty] private double fpsWindowMs = 500;         // окно усреднения времени кадра
    [ObservableProperty] private string presentMonPath = "";
    [ObservableProperty] private string fpsArgs = "--output_stdout --stop_existing_session";

    public ObservableCollection<string> OsdMetrics { get; set; } =
        new() { "cpu.load", "cpu.temp", "gpu.load", "gpu.temp", "ram.load", "fps.now" };
    public ObservableCollection<WidgetConfig> Widgets { get; set; } = new();
    public EdgeConfig Edge { get; set; } = new();
    public AlertSettings Alerts { get; set; } = new();

    public event Action? Changed;   // события System.Text.Json и не сериализует — атрибут здесь недопустим
    private bool _hooked;
    private void Raise() => Changed?.Invoke();

    /// <summary>Подписывается на изменения всех вложенных объектов, чтобы сохранять настройки.</summary>
    public void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        PropertyChanged += (_, _) => Raise();
        OsdMetrics.CollectionChanged += (_, _) => Raise();
        foreach (var w in Widgets) { w.PropertyChanged += (_, _) => Raise(); w.Extras.CollectionChanged += (_, _) => Raise(); }
        Edge.PropertyChanged += (_, _) => Raise();
        void HookBlock(EdgeBlock b) { b.PropertyChanged += (_, _) => Raise(); b.Extras.CollectionChanged += (_, _) => Raise(); }
        foreach (var b in Edge.Blocks) HookBlock(b);
        Edge.Blocks.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (EdgeBlock b in e.NewItems) HookBlock(b);
            Raise();
        };
        Alerts.PropertyChanged += (_, _) => Raise();
        foreach (var r in Alerts.Rules) r.PropertyChanged += (_, _) => Raise();
        Alerts.Rules.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (AlertRule r in e.NewItems) r.PropertyChanged += (_, _) => Raise();
            Raise();
        };
    }

    public void Sanitize()
    {
        PollIntervalMs = Math.Clamp(PollIntervalMs, 250, 60000);
        OsdFontSize = Math.Clamp(OsdFontSize, 8, 40);
        LoadWarn = Math.Clamp(LoadWarn, 10, 98); LoadCrit = Math.Clamp(LoadCrit, LoadWarn + 1, 100);
        TempWarn = Math.Clamp(TempWarn, 30, 110); TempCrit = Math.Clamp(TempCrit, TempWarn + 1, 120);
        FpsWindowMs = Math.Clamp(FpsWindowMs, 250, 2000);
        if (FpsMetric is not ("Fps" or "FrameTime")) FpsMetric = "Fps";
        FpsGood = Math.Clamp(FpsGood, 20, 500); FpsBad = Math.Clamp(FpsBad, 5, FpsGood - 1);
        OsdMetrics ??= new(); Widgets ??= new(); Edge ??= new(); Alerts ??= new(); Alerts.Rules ??= new();

        var ordered = new List<WidgetConfig>();
        foreach (var def in WidgetCatalog.All)
            ordered.Add(Widgets.FirstOrDefault(w => w.Id == def.Id) ?? new WidgetConfig { Id = def.Id, Font = def.Id == "classic" ? "Consolas" : "Modern" });
        Widgets.Clear();
        foreach (var w in ordered)
        {
            w.Scale = Math.Clamp(w.Scale, 0.15, 2.5);
            w.Opacity = Math.Clamp(w.Opacity, 0.1, 1);
            w.Padding = Math.Clamp(w.Padding, 0, 40);
            if (w.Variant is not ("Number" or "NumberLabel" or "Compact" or "Line" or "Full")) w.Variant = "Full";
            w.BgOpacity = Math.Clamp(w.BgOpacity, 0, 1);
            if (w.Layer is not ("Top" or "Desktop")) w.Layer = "Top";
            if (w.ColorMode is not ("Traffic" or "Accent")) w.ColorMode = "Traffic";
            if (!WidgetFonts.IsValid(w.Font)) w.Font = "Modern";
            if (w.Weight is not ("Regular" or "SemiBold" or "Bold")) w.Weight = "SemiBold";
            w.Extras ??= new();
            var okIds = MetricCatalog.All.Select(m => m.Id).ToHashSet();
            for (int i = w.Extras.Count - 1; i >= 0; i--) if (!okIds.Contains(w.Extras[i])) w.Extras.RemoveAt(i);
            Widgets.Add(w);
        }

        if (Edge.Edge is not ("Left" or "Right" or "Top" or "Bottom")) Edge.Edge = "Right";
        Edge.ZonePx = Math.Clamp(Edge.ZonePx, 1, 40);
        Edge.ShowDelayMs = Math.Clamp(Edge.ShowDelayMs, 0, 3000);
        Edge.HideDelayMs = Math.Clamp(Edge.HideDelayMs, 0, 5000);
        Edge.AnimMs = Math.Clamp(Edge.AnimMs, 80, 1500);
        Edge.Opacity = Math.Clamp(Edge.Opacity, 0.3, 1);
        if (!WidgetFonts.IsValid(Edge.Font)) Edge.Font = "Modern";
        if (Edge.Weight is not ("Regular" or "SemiBold" or "Bold")) Edge.Weight = "SemiBold";
        Edge.Scale = Math.Clamp(Edge.Scale, 0.5, 2.5);

        Edge.Blocks ??= new();
        if (!Edge.BlocksSeeded)
        {
            Edge.Blocks.Clear();
            foreach (var b in EdgeBlock.Defaults()) Edge.Blocks.Add(b);
            Edge.BlocksSeeded = true;
        }
        var valid = MetricCatalog.All.Select(m => m.Id).ToHashSet();
        foreach (var b in Edge.Blocks.ToList())
        {
            if (!valid.Contains(b.Primary)) { Edge.Blocks.Remove(b); continue; }
            if (b.Style is not ("Ring" or "Number" or "Bar" or "Line")) b.Style = "Ring";
            if (b.Size is not ("S" or "M" or "L")) b.Size = "M";
            b.Extras ??= new();
            for (int i = b.Extras.Count - 1; i >= 0; i--)
                if (!valid.Contains(b.Extras[i]) || b.Extras[i] == b.Primary) b.Extras.RemoveAt(i);
        }

        if (Alerts.Rules.Count == 0) foreach (var r in AlertSettings.DefaultRules()) Alerts.Rules.Add(r);
        if (Alerts.Style is not ("Slide" or "Fade" or "Scale" or "Expand" or "Glow" or "Pulse" or "Minimal")) Alerts.Style = "Slide";
        if (Alerts.Corner is not ("TopLeft" or "TopRight" or "BottomLeft" or "BottomRight")) Alerts.Corner = "BottomRight";
        Alerts.DurationSec = Math.Clamp(Alerts.DurationSec, 3, 60);
        Alerts.MaxCards = Math.Clamp(Alerts.MaxCards, 1, 6);
        foreach (var r in Alerts.Rules)
        {
            r.Threshold = Math.Clamp(r.Threshold, 1, 120);
            r.DurationSec = Math.Clamp(r.DurationSec, 0, 600);
            r.RepeatMin = Math.Clamp(r.RepeatMin, 1, 600);
        }
    }

    public void ResetToDefaults()
    {
        var d = new AppSettings();
        PollIntervalMs = d.PollIntervalMs; CloseToTray = d.CloseToTray; StartMinimized = d.StartMinimized;
        OverlaysVisible = true; ShowLabels = d.ShowLabels; OsdFontSize = d.OsdFontSize;
        LoadWarn = d.LoadWarn; LoadCrit = d.LoadCrit; TempWarn = d.TempWarn; TempCrit = d.TempCrit;
        FpsGood = d.FpsGood; FpsBad = d.FpsBad;
        FpsEnabled = d.FpsEnabled; FpsMetric = d.FpsMetric; FpsWindowMs = d.FpsWindowMs; PresentMonPath = d.PresentMonPath; FpsArgs = d.FpsArgs;
        OsdMetrics.Clear();
        foreach (var m in d.OsdMetrics) OsdMetrics.Add(m);
        foreach (var w in Widgets) { w.ResetStyle(); w.Enabled = false; w.X = null; w.Y = null; }
        Edge.ResetToDefaults();
        Alerts.ResetToDefaults();
    }
}
