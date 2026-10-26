using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ThodMonitor.Models;
using ThodMonitor.Services;
using ThodMonitor.ViewModels;
using ThodMonitor.Visuals;

namespace ThodMonitor.Views;

/// <summary>
/// Скрытая панель у края основного экрана. Курсор отслеживается по таймеру (без окна-«ловушки»),
/// поэтому панель не перехватывает клики и не крадёт фокус. Появляется выездом из-за края с плавным изменением прозрачности.
/// Состояние хранится явно, поэтому анимация не перезапускается при движении курсора вдоль края.
/// </summary>
public sealed class EdgePanelWindow : OverlayWindowBase
{
    private readonly EdgeConfig _cfg;
    private readonly EdgeContent _content = new();
    private readonly Border _card;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DispatcherTimer _timer;
    private bool _shown;
    private DateTime? _enterAt, _leaveAt;

    public EdgePanelWindow(EdgeConfig cfg, MainViewModel vm)
    {
        _cfg = cfg;
        DataContext = vm;
        _content.DataContext = vm;
        _card = new Border
        {
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x16, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = _content,
            LayoutTransform = _scale
        };
        Content = _card;
        Title = "Thod Monitor · панель у края";
        ApplyFont();
        cfg.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(EdgeConfig.Font) or nameof(EdgeConfig.Weight)) ApplyFont(); };
        Topmost = true;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => Tick();
    }

    private void ApplyFont()
    {
        Resources["ValueFont"] = WidgetFonts.Family(_cfg.Font);
        Resources["ValueWeight"] = WidgetFonts.Weight(_cfg.Weight);
    }

    public void Start() => _timer.Start();
    public void Stop() { _timer.Stop(); if (_shown) HideNow(); }

    private bool Horizontal => _cfg.Edge is "Top" or "Bottom";

    private void Tick()
    {
        try { TickCore(); } catch (Exception ex) { Log.Error("Ошибка выдвижной панели", ex); }
    }

    private void TickCore()
    {
        if (!_cfg.Enabled) { if (_shown) HideNow(); return; }
        if (_cfg.IgnoreFullscreen && Native.IsForegroundFullscreen()) { if (_shown) HideNow(); return; }

        _scale.ScaleX = _scale.ScaleY = _cfg.Scale;
        Native.GetCursorPos(out var p);
        double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double cx = p.X / k, cy = p.Y / k;
        double W = SystemParameters.PrimaryScreenWidth, H = SystemParameters.PrimaryScreenHeight, z = _cfg.ZonePx;

        bool onScreen = cx >= 0 && cx <= W && cy >= 0 && cy <= H;
        bool inZone = onScreen && _cfg.Edge switch
        {
            "Left" => cx <= z,
            "Right" => cx >= W - z - 1,
            "Top" => cy <= z,
            _ => cy >= H - z - 1,
        };
        bool overPanel = _shown && cx >= Left - 12 && cx <= Left + ActualWidth + 12 && cy >= Top - 12 && cy <= Top + ActualHeight + 12;

        var now = DateTime.UtcNow;
        if (inZone || overPanel)
        {
            _leaveAt = null;
            if (!_shown)
            {
                _enterAt ??= now;
                if ((now - _enterAt.Value).TotalMilliseconds >= _cfg.ShowDelayMs) ShowNow();
            }
        }
        else
        {
            _enterAt = null;
            if (_shown)
            {
                _leaveAt ??= now;
                if ((now - _leaveAt.Value).TotalMilliseconds >= _cfg.HideDelayMs) HideNow();
            }
        }
    }

    private (double shownL, double shownT, double hidL, double hidT) Layout()
    {
        _content.SetOrientation(Horizontal ? Orientation.Horizontal : Orientation.Vertical);
        _card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = _card.DesiredSize;
        var wa = SystemParameters.WorkArea;
        double W = SystemParameters.PrimaryScreenWidth, H = SystemParameters.PrimaryScreenHeight;
        const double m = 12;
        double cxL = Math.Max(wa.Left + m, wa.Left + (wa.Width - sz.Width) / 2);
        double cyT = Math.Max(wa.Top + m, wa.Top + (wa.Height - sz.Height) / 2);
        return _cfg.Edge switch
        {
            "Left" => (wa.Left + m, cyT, -sz.Width - 40, cyT),
            "Top" => (cxL, wa.Top + m, cxL, -sz.Height - 40),
            "Bottom" => (cxL, wa.Bottom - sz.Height - m, cxL, H + 40),
            _ => (wa.Right - sz.Width - m, cyT, W + 40, cyT),
        };
    }

    private void ShowNow()
    {
        _shown = true;
        var (sl, st, hl, ht) = Layout();
        var dur = TimeSpan.FromMilliseconds(_cfg.AnimMs);
        Left = Horizontal ? sl : hl; Top = Horizontal ? ht : st;   // стартовая позиция за краем
        Opacity = 0;
        if (!IsVisible) Show();
        ClickThrough = true;

        var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
        if (Horizontal) BeginAnimation(TopProperty, new DoubleAnimation(ht, st, dur) { EasingFunction = ease });
        else BeginAnimation(LeftProperty, new DoubleAnimation(hl, sl, dur) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, _cfg.Opacity, TimeSpan.FromMilliseconds(_cfg.AnimMs * 0.8)));
    }

    private void HideNow()
    {
        _shown = false; _enterAt = null; _leaveAt = null;
        var (_, _, hl, ht) = Layout();
        var dur = TimeSpan.FromMilliseconds(_cfg.AnimMs * 0.8);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var move = Horizontal ? new DoubleAnimation(ht, dur) { EasingFunction = ease } : new DoubleAnimation(hl, dur) { EasingFunction = ease };
        var fade = new DoubleAnimation(0, dur);
        fade.Completed += (_, _) => { if (!_shown) Hide(); };
        BeginAnimation(Horizontal ? TopProperty : LeftProperty, move);
        BeginAnimation(OpacityProperty, fade);
    }
}
