using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThodMonitor.Models;
using ThodMonitor.Services;

namespace ThodMonitor.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SensorService _sensors;
    private readonly FpsService _fps;
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private IReadOnlyList<MetricSample>? _latest;
    private FpsSnapshot? _lastFps;
    private bool _fpsLive;          // были ли данные на прошлом обновлении (чтобы не гасить карточки каждый тик)
    private int _scheduled, _tick;
    private bool _syncing, _suppressAutostart;
    private readonly Dictionary<string, MetricCardViewModel> _c = new();

    public AppSettings Settings { get; }
    public ObservableCollection<MetricCardViewModel> Cards { get; } = new();
    public ObservableCollection<MetricCardViewModel> ClassicCards { get; } = new();
    public ObservableCollection<string> DiagLines { get; } = new();
    public ObservableCollection<EdgeBlockViewModel> EdgeBlocks { get; } = new();
    public EdgeEditorViewModel EdgeEditor { get; }
    public bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public string VersionText { get; } = "Thod Monitor " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?");
    public string LogPath => Log.FilePath;

    // быстрый доступ к показателям для привязок виджетов
    public MetricCardViewModel CpuLoad => _c["cpu.load"];
    public MetricCardViewModel CpuTemp => _c["cpu.temp"];
    public MetricCardViewModel CpuPower => _c["cpu.power"];
    public MetricCardViewModel CpuClock => _c["cpu.clock"];
    public MetricCardViewModel GpuLoad => _c["gpu.load"];
    public MetricCardViewModel GpuTemp => _c["gpu.temp"];
    public MetricCardViewModel GpuVram => _c["gpu.vram"];
    public MetricCardViewModel GpuPower => _c["gpu.power"];
    public MetricCardViewModel RamLoad => _c["ram.load"];
    public MetricCardViewModel FpsNow => _c["fps.now"];
    public MetricCardViewModel FpsAvg => _c["fps.avg"];
    public MetricCardViewModel FpsLow => _c["fps.low"];
    public MetricCardViewModel FrameTime => _c["fps.ft"];

    public WidgetConfig CfgSystem => Cfg("system");
    public WidgetConfig CfgCpu => Cfg("cpu");
    public WidgetConfig CfgGpu => Cfg("gpu");
    public WidgetConfig CfgRam => Cfg("ram");
    public WidgetConfig CfgFps => Cfg("fps");
    public WidgetConfig CfgClassic => Cfg("classic");
    private WidgetConfig Cfg(string id) => Settings.Widgets.First(w => w.Id == id);

    [ObservableProperty] private string page = "Home";
    [ObservableProperty] private bool monitoringPaused;
    [ObservableProperty] private bool autostartEnabled;
    public bool AutostartSupported => AutostartService.IsSupported;
    [ObservableProperty] private bool editMode;
    [ObservableProperty] private string fpsStatus = "";
    [ObservableProperty] private Brush fpsBrush = Visuals.ColorScale.Brush(Visuals.ColorScale.Neutral);
    [ObservableProperty] private MetricCardViewModel fpsMain = null!;      // основной показатель FPS-панели
    [ObservableProperty] private MetricCardViewModel fpsSecond = null!;    // второй (мелко)
    [ObservableProperty] private string fpsMainLabel = "FPS";
    [ObservableProperty] private string fpsSecondLabel = "мс";
    public Dictionary<string, ExtraRowsViewModel> WidgetExtras { get; } = new();

    public event Action? StateChanged;
    public event Action? ExitRequested;
    public event Action? SamplesApplied;
    public event Action<AlertRule>? TestAlertRequested;

    public MetricCardViewModel? Find(string id) => _c.GetValueOrDefault(id);

    public MainViewModel(SettingsService settingsSvc, SensorService sensors, FpsService fps)
    {
        Settings = settingsSvc.Settings;
        _sensors = sensors; _fps = fps;
        FpsStatus = fps.Status;

        foreach (var def in MetricCatalog.All)
        {
            var card = new MetricCardViewModel(def, Settings) { IsInOsd = Settings.OsdMetrics.Contains(def.Id) };
            card.OsdToggled += OnCardOsdToggled;
            Cards.Add(card);
            _c[def.Id] = card;
        }
        RebuildClassic();
        foreach (var c in Cards.Where(c => c.Id.StartsWith("fps."))) c.Update(null, FpsStatus);

        foreach (var w in Settings.Widgets)
            WidgetExtras[w.Id] = new ExtraRowsViewModel(w.Extras, this, w.Id is "cpu" or "gpu" or "ram" or "fps" ? w.Id : "");
        EdgeEditor = new EdgeEditorViewModel(this, Settings);
        foreach (var b in Settings.Edge.Blocks) EdgeBlocks.Add(new EdgeBlockViewModel(b, this));
        Settings.Edge.Blocks.CollectionChanged += OnBlocksChanged;

        _suppressAutostart = true;
        AutostartEnabled = AutostartService.IsEnabled();
        _suppressAutostart = false;

        Settings.PropertyChanged += OnSettingsChanged;
        Settings.Changed += () => { RecalcFpsBrush(); StateChanged?.Invoke(); };
        FpsNow.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MetricCardViewModel.StateBrush)) RecalcFpsBrush(); };
        FrameTime.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MetricCardViewModel.StateBrush)) RecalcFpsBrush(); };
        _fps.WindowMs = (int)Settings.FpsWindowMs;
        RecalcFpsMain();
        _fps.Updated += s => { _lastFps = s; Schedule(); };
        _fps.StatusChanged += st => _ui.BeginInvoke(new Action(() =>
        {
            FpsStatus = st;
            if (_lastFps is not { HasData: true }) ApplyFps(new FpsSnapshot(false, "", 0, 0, null, 0), force: true);
        }));
    }

    // ---------- блоки выдвижной панели ----------

    private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                int at = e.NewStartingIndex;
                foreach (EdgeBlock b in e.NewItems!) EdgeBlocks.Insert(at++, new EdgeBlockViewModel(b, this));
                break;
            case NotifyCollectionChangedAction.Remove:
                for (int i = 0; i < e.OldItems!.Count; i++)
                {
                    var vm = EdgeBlocks[e.OldStartingIndex];
                    if (EdgeEditor.Selected == vm) EdgeEditor.Select(null);
                    EdgeBlocks.RemoveAt(e.OldStartingIndex);
                    vm.Detach();
                }
                break;
            case NotifyCollectionChangedAction.Move:
                EdgeBlocks.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;
            default:
                EdgeEditor.Select(null);
                foreach (var old in EdgeBlocks) old.Detach();
                EdgeBlocks.Clear();
                foreach (var b in Settings.Edge.Blocks) EdgeBlocks.Add(new EdgeBlockViewModel(b, this));
                break;
        }
    }

    // ---------- данные ----------

    public void Push(IReadOnlyList<MetricSample> samples) { _latest = samples; Schedule(); }

    private void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
            _ui.BeginInvoke(new Action(Drain), DispatcherPriority.Background);
    }

    private void Drain()
    {
        Interlocked.Exchange(ref _scheduled, 0);
        var s = _latest;
        if (s != null)
        {
            var map = s.ToDictionary(x => x.Id);
            foreach (var c in Cards.Where(c => !c.Id.StartsWith("fps.")))
            {
                if (map.TryGetValue(c.Id, out var sample)) c.Update(sample.Value, sample.Note);
                else c.Update(null, "Ошибка чтения датчика (см. журнал)");
            }
        }
        if (_lastFps != null) ApplyFps(_lastFps);
        SamplesApplied?.Invoke();
        if (Page == "Diagnostics" && ++_tick % 5 == 0) RefreshDiagnostics();
    }

    private void ApplyFps(FpsSnapshot f, bool force = false)
    {
        if (f.HasData)
        {
            _fpsLive = true;
            _c["fps.now"].Update(f.Now, f.Process + " · по времени кадра");
            _c["fps.avg"].Update(f.Avg, "за 10 секунд");
            _c["fps.low"].Update(f.Low1, f.Low1.HasValue ? "99-й перцентиль кадра, 30 с" : "нужно больше кадров");
            _c["fps.ft"].Update(f.FrameMs, $"среднее за {Settings.FpsWindowMs:0} мс");
        }
        // «Н/Д» ставим один раз при реальном переходе, а не каждые полсекунды —
        // иначе карточка мигает и мини-граф затирается пустыми точками
        else if (_fpsLive || force)
        {
            _fpsLive = false;
            foreach (var c in Cards.Where(c => c.Id.StartsWith("fps."))) c.Update(null, FpsStatus);
        }
    }

    private void RebuildClassic()
    {
        ClassicCards.Clear();
        foreach (var c in Cards.Where(c => c.IsInOsd)) ClassicCards.Add(c);
    }

    // ---------- реакции на изменения ----------

    private void OnCardOsdToggled(MetricCardViewModel c)
    {
        if (_syncing) return;
        var list = Settings.OsdMetrics;
        if (c.IsInOsd) { if (!list.Contains(c.Id)) list.Add(c.Id); }
        else list.Remove(c.Id);
        RebuildClassic();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.PollIntervalMs): _sensors.IntervalMs = Settings.PollIntervalMs; break;
            case nameof(AppSettings.LoadWarn): case nameof(AppSettings.LoadCrit):
            case nameof(AppSettings.TempWarn): case nameof(AppSettings.TempCrit):
            case nameof(AppSettings.FpsGood): case nameof(AppSettings.FpsBad):
                foreach (var c in Cards) c.RefreshThresholds();
                break;
            case nameof(AppSettings.FpsMetric): RecalcFpsMain(); break;
            case nameof(AppSettings.FpsWindowMs): _fps.WindowMs = (int)Settings.FpsWindowMs; break;
            case nameof(AppSettings.FpsEnabled):
                if (Settings.FpsEnabled) _fps.Start(Settings.PresentMonPath, Settings.FpsArgs); else _fps.Stop();
                break;
        }
    }

    partial void OnMonitoringPausedChanged(bool value) { _sensors.Paused = value; StateChanged?.Invoke(); }
    partial void OnEditModeChanged(bool value) => StateChanged?.Invoke();
    partial void OnPageChanged(string value) { if (value == "Diagnostics") RefreshDiagnostics(); }

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_suppressAutostart) return;
        if (!AutostartService.Set(value))
        {
            _suppressAutostart = true;
            AutostartEnabled = AutostartService.IsEnabled();
            _suppressAutostart = false;
        }
    }

    // ---------- команды ----------

    [RelayCommand]
    private void ApplyPreset(string name) => PresetService.Apply(Settings, name);

    [RelayCommand]
    private void TestAlert(AlertRule rule) => TestAlertRequested?.Invoke(rule);

    [RelayCommand]
    private void RestartFps()
    {
        if (!Settings.FpsEnabled) Settings.FpsEnabled = true;
        else _fps.Start(Settings.PresentMonPath, Settings.FpsArgs);
    }

    [RelayCommand]
    private void OpenPresentMonFolder()
    {
        try
        {
            Directory.CreateDirectory(FpsService.DefaultFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{FpsService.DefaultFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("Не удалось открыть папку PresentMon", ex); }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        var answer = MessageBox.Show("Сбросить все настройки Thod Monitor к значениям по умолчанию?",
            "Сброс настроек", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        _syncing = true;
        try
        {
            Settings.ResetToDefaults();
            PresetService.Apply(Settings, "Circles");
            foreach (var c in Cards) c.IsInOsd = Settings.OsdMetrics.Contains(c.Id);
            foreach (var c in Cards) c.RefreshThresholds();
        }
        finally { _syncing = false; }
        RebuildClassic();
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(LogPath)}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("Не удалось открыть папку журнала", ex); }
    }

    [RelayCommand]
    private async Task RecheckSensorsAsync()
    {
        await Task.Run(() => _sensors.Reopen());
        RefreshDiagnostics();
    }

    [RelayCommand]
    private void TuneFps()
    {
        // подбирает компактные размеры под выбранный вид панели FPS
        var c = CfgFps;
        (double pad, double bg, double scale) = c.Variant switch
        {
            "Number" => (2.0, 0.35, 0.8),
            "NumberLabel" => (4.0, 0.45, 0.8),
            "Compact" => (8.0, 0.6, 0.9),
            "Line" => (6.0, 0.5, 0.9),
            _ => (14.0, 0.8, 1.0),
        };
        c.Padding = pad; c.BgOpacity = bg; c.Scale = scale;
    }

    private void RecalcFpsBrush()
    {
        var cfg = CfgFps;
        if (cfg.ColorMode == "Accent")
        {
            try { FpsBrush = Visuals.ColorScale.Brush((Color)ColorConverter.ConvertFromString(cfg.Accent)); return; }
            catch { /* неверный HEX — используем «светофор» */ }
        }
        FpsBrush = FpsMain.StateBrush;
    }

    /// <summary>Основной показатель: FPS (считается по времени кадра, подпись «FPS») или само время кадра в мс.</summary>
    private void RecalcFpsMain()
    {
        bool ft = Settings.FpsMetric == "FrameTime";
        FpsMain = ft ? FrameTime : FpsNow;
        FpsSecond = ft ? FpsNow : FrameTime;
        FpsMainLabel = ft ? "мс" : "FPS";
        FpsSecondLabel = ft ? "FPS" : "мс";
        RecalcFpsBrush();
    }

    /// <summary>Перечитывает состояние автозапуска (после фоновой сверки задачи планировщика).</summary>
    public void RefreshAutostart()
    {
        _suppressAutostart = true;
        AutostartEnabled = AutostartService.IsEnabled();
        _suppressAutostart = false;
    }

    private void RefreshDiagnostics()
    {
        DiagLines.Clear();
        DiagLines.Add(VersionText);
        DiagLines.Add($"Права администратора: {(IsElevated ? "да" : "нет (запустите ThodMonitor.exe — он запросит права сам)")}");
        DiagLines.Add($"Среда: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}");
        DiagLines.Add($"Показателей доступно: {Cards.Count(c => c.IsAvailable)} из {Cards.Count}");
        DiagLines.Add($"FPS: {_fps.Status}");
        DiagLines.Add($"PresentMon: {_fps.ResolvedExe ?? "не найден"}");
        DiagLines.Add("Горячая клавиша: Ctrl+Alt+O — показать/скрыть оверлеи");
        DiagLines.Add($"Журнал: {LogPath}");
        foreach (var p in _sensors.GetProblems()) DiagLines.Add("⚠ " + p);
        foreach (var c in Cards.Where(c => !c.IsAvailable && !c.Id.StartsWith("fps.")))
            DiagLines.Add($"• {c.Title}: Н/Д — {c.Note}");
    }
}
