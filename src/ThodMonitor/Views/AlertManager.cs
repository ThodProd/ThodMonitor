using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ThodMonitor.Models;
using ThodMonitor.Services;
using ThodMonitor.ViewModels;
using ThodMonitor.Visuals;

namespace ThodMonitor.Views;

/// <summary>Показывает карточки уведомлений стопкой в выбранном углу, добавляет сведения о нагрузке и «тяжёлых» процессах.</summary>
public sealed class AlertManager
{
    private readonly AppSettings _s;
    private readonly MainViewModel _vm;
    private readonly List<AlertWindow> _active = new();   // старые → новые

    public AlertManager(AppSettings s, MainViewModel vm) { _s = s; _vm = vm; }

    public void OnFired(AlertRule rule, double value, TimeSpan held) => Show(rule, value, held, false);
    public void Test(AlertRule rule)
    {
        var card = _vm.Find(rule.Metric);
        double v = card is { IsAvailable: true } ? card.NumericValue : rule.Threshold;
        Show(rule, v, TimeSpan.FromSeconds(rule.DurationSec), true);
    }

    private void Show(AlertRule rule, double value, TimeSpan held, bool test)
    {
        try { ShowCore(rule, value, held, test); }
        catch (Exception ex) { Log.Error("Ошибка показа уведомления", ex); }
    }

    private void ShowCore(AlertRule rule, double value, TimeSpan held, bool test)
    {
        var def = MetricCatalog.All.First(m => m.Id == rule.Metric);
        var card = _vm.Find(rule.Metric);
        string unit = def.Unit == "%" ? "%" : def.Unit == "°C" ? " °C" : " " + def.Unit;
        var avm = new AlertViewModel
        {
            Title = rule.Title,
            IconName = rule.Icon,
            RingValue = Math.Clamp(value, 0, 100),
            RingText = def.Unit == "%" ? $"{value:0}%" : def.Unit == "°C" ? $"{value:0}°" : $"{value:0}",
            Warn = card?.Warn ?? 60,
            Crit = card?.Crit ?? 85,
            Compact = _s.Alerts.Style == "Minimal",
            Subtitle = (test ? "ТЕСТ · " : "") + $"{value:0}{unit} · порог {rule.Threshold:0}{unit} · держится {held.TotalSeconds:0} с",
        };
        AddContextLines(rule, avm);

        var col = ColorScale.HighIsBad(value, card?.Warn ?? 60, card?.Crit ?? 85);
        var win = new AlertWindow(avm, col);
        win.SizeSettled += _ => Reflow(null);
        _active.Add(win);

        int max = (int)_s.Alerts.MaxCards;
        while (_active.Count > max) Dismiss(_active[0]);

        Reflow(win);
        win.Show();
        win.PlayIn(_s.Alerts.Style, SystemParameters.WorkArea, _s.Alerts.Corner);
        if (_s.Alerts.Sound) { try { SystemSounds.Exclamation.Play(); } catch { } }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_s.Alerts.DurationSec) };
        timer.Tick += (_, _) => { timer.Stop(); Dismiss(win); };
        timer.Start();

        _ = LoadProcessesAsync(rule, avm);
    }

    private void AddContextLines(AlertRule rule, AlertViewModel avm)
    {
        void Add(string label, MetricCardViewModel? c, string fmt = "")
        {
            if (c is { IsAvailable: true }) avm.Lines.Add($"{label}: {c.ValueText} {c.UnitText}".TrimEnd());
        }
        if (rule.Metric.StartsWith("cpu."))
        {
            if (rule.Metric != "cpu.temp") Add("Температура", _vm.CpuTemp);
            if (rule.Metric != "cpu.load") Add("Загрузка", _vm.CpuLoad);
        }
        else if (rule.Metric.StartsWith("gpu."))
        {
            if (rule.Metric != "gpu.temp") Add("Температура", _vm.GpuTemp);
            if (rule.Metric != "gpu.load") Add("Загрузка", _vm.GpuLoad);
            if (rule.Metric != "gpu.vram" && _vm.GpuVram.IsAvailable) avm.Lines.Add($"VRAM: {_vm.GpuVram.Note}");
            else if (rule.Metric == "gpu.vram" && !string.IsNullOrEmpty(_vm.GpuVram.Note)) avm.Lines.Add($"Занято: {_vm.GpuVram.Note}");
        }
        else if (rule.Metric == "ram.load" && !string.IsNullOrEmpty(_vm.RamLoad.Note)) avm.Lines.Add($"Используется: {_vm.RamLoad.Note}");
        else if (rule.Metric == "disk.used" && !string.IsNullOrEmpty(_vm.Find("disk.used")?.Note)) avm.Lines.Add(_vm.Find("disk.used")!.Note!);
    }

    private async Task LoadProcessesAsync(AlertRule rule, AlertViewModel avm)
    {
        try
        {
            List<string>? top = null; string head = "";
            if (rule.Metric is "cpu.load" or "cpu.temp") { top = await ProcessInfoService.TopCpuAsync(); head = "Больше всего грузят CPU:"; }
            else if (rule.Metric == "ram.load") { top = await ProcessInfoService.TopMemoryAsync(); head = "Больше всего памяти:"; }
            if (top is { Count: > 0 })
            {
                avm.Lines.Add(head);
                foreach (var t in top) avm.Lines.Add("• " + t);
            }
        }
        catch (Exception ex) { Log.Error("Не удалось получить список процессов", ex); }
    }

    private void Dismiss(AlertWindow w)
    {
        if (!_active.Remove(w)) return;
        w.CloseAnimated(() => { });
        Reflow(null);
    }

    /// <summary>Раскладывает карточки стопкой от выбранного угла; новая — ближе всего к углу.</summary>
    private void Reflow(AlertWindow? fresh)
    {
        var wa = SystemParameters.WorkArea;
        string c = _s.Alerts.Corner;
        bool right = c.EndsWith("Right"), bottom = c.StartsWith("Bottom");
        const double margin = 14, gap = 10;
        double pad = AlertWindow.StackPad;
        double y = bottom ? wa.Bottom - margin + pad : wa.Top + margin - pad;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var w = _active[i];
            var sz = w.CurrentSize();
            double top;
            if (bottom) { top = y - sz.Height; y = top + 2 * pad - gap; }
            else { top = y; y = top + sz.Height - 2 * pad + gap; }

            double left = right ? wa.Right - sz.Width + pad - margin : wa.Left - pad + margin;
            if (w == fresh || !w.IsVisible)
            {
                w.Left = left; w.Top = top;
            }
            else
            {
                w.BeginAnimation(Window.TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
    }

    public void CloseAll()
    {
        foreach (var w in _active.ToList()) { try { w.Close(); } catch { } }
        _active.Clear();
    }
}
