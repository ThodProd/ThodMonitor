using System.Windows;
using System.Windows.Controls;
using ThodMonitor.Models;
using ThodMonitor.Services;
using ThodMonitor.ViewModels;
using ThodMonitor.Views.Widgets;

namespace ThodMonitor.Views;

/// <summary>Создаёт, показывает и скрывает окна виджетов в соответствии с настройками.</summary>
public sealed class WidgetManager : IDisposable
{
    private readonly AppSettings _s;
    private readonly MainViewModel _vm;
    private readonly Dictionary<string, WidgetWindow> _win = new();

    public WidgetManager(AppSettings s, MainViewModel vm) { _s = s; _vm = vm; }

    private static FrameworkElement Create(string id) => id switch
    {
        "system" => new SystemWidget(),
        "cpu" => new CpuWidget(),
        "gpu" => new GpuWidget(),
        "ram" => new RamWidget(),
        "fps" => new FpsWidget(),
        _ => new ClassicWidget(),
    };

    /// <summary>Виджет + мелкие строки дополнительных показателей под ним (кроме текстового оверлея).</summary>
    private FrameworkElement Build(string id)
    {
        var content = Create(id);
        if (id == "classic" || !_vm.WidgetExtras.TryGetValue(id, out var extras)) return content;
        var host = new StackPanel();
        host.Children.Add(content);
        host.Children.Add(new ItemsControl
        {
            ItemsSource = extras.Rows,
            ItemTemplate = (DataTemplate)Application.Current.FindResource("ExtraRowTemplate"),
            Style = (Style)Application.Current.FindResource("ExtrasList")
        });
        return host;
    }

    /// <summary>Приводит набор открытых окон в соответствие с настройками. Вызывается часто, работает быстро.</summary>
    public void Sync()
    {
        foreach (var cfg in _s.Widgets)
        {
            bool show = cfg.Enabled && _s.OverlaysVisible;
            _win.TryGetValue(cfg.Id, out var w);
            if (show)
            {
                if (w == null)
                {
                    w = new WidgetWindow(cfg, _vm, Build(cfg.Id));
                    _win[cfg.Id] = w;
                }
                if (!w.IsVisible)
                {
                    try { w.Show(); } catch (Exception ex) { Log.Error("Не удалось показать виджет " + cfg.Id, ex); continue; }
                    if (cfg.Layer == "Desktop") Native.SendToBottom(w);
                }
                w.SetEditMode(_vm.EditMode);
            }
            else if (w is { IsVisible: true }) w.Hide();
        }
    }

    public void Dispose()
    {
        foreach (var w in _win.Values) { try { w.Close(); } catch { } }
        _win.Clear();
    }
}
