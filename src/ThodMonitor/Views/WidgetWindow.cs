using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ThodMonitor.Models;
using ThodMonitor.Services;
using ThodMonitor.ViewModels;
using ThodMonitor.Visuals;

namespace ThodMonitor.Views;

/// <summary>
/// Окно одного оверлея-виджета. В режиме правки перетаскивается мышью (собственная логика по позиции курсора:
/// без «залипания» у краёв), колесо меняет размер. Окно всегда остаётся целиком в пределах экрана,
/// но может стоять вплотную к любому краю, включая самый верх.
/// </summary>
public sealed class WidgetWindow : OverlayWindowBase
{
    private readonly WidgetConfig _cfg;
    private readonly Border _card;
    private readonly ScaleTransform _scale = new(1, 1);
    private string _font = "", _weight = "";
    private bool _edit, _dragging;
    private Native.POINT _startCursor;
    private double _startLeft, _startTop;

    public WidgetWindow(WidgetConfig cfg, MainViewModel vm, FrameworkElement content)
    {
        _cfg = cfg;
        DataContext = vm;
        content.DataContext = vm;
        _card = new Border { BorderThickness = new Thickness(1), Child = content, LayoutTransform = _scale };
        Content = _card;
        Title = "Thod Monitor · " + cfg.Title;
        ApplyConfig();
        cfg.PropertyChanged += (_, _) => ApplyConfig();
        Activated += (_, _) => { if (_cfg.Layer == "Desktop") Native.SendToBottom(this); };
        SizeChanged += (_, _) =>
        {
            // в режиме правки окно не двигаем само: размер контента меняется (например, появляется
            // подпись FPS) и окно «прыгало» бы туда-сюда под рукой пользователя
            if (!_dragging && !_edit) KeepOnScreen(true);
        };
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => EndDrag();
        MouseWheel += OnWheel;
    }

    private void ApplyConfig()
    {
        _scale.ScaleX = _scale.ScaleY = _cfg.Scale;
        Opacity = _cfg.Opacity;
        if (_font != _cfg.Font) { _font = _cfg.Font; Resources["ValueFont"] = WidgetFonts.Family(_font); }
        if (_weight != _cfg.Weight) { _weight = _cfg.Weight; Resources["ValueWeight"] = WidgetFonts.Weight(_weight); }
        _card.Padding = new Thickness(_cfg.Padding);
        _card.CornerRadius = new CornerRadius(Math.Min(18, _cfg.Padding + 4));
        Topmost = _cfg.Layer == "Top";
        if (_cfg.Layer == "Desktop" && IsLoaded) Native.SendToBottom(this);

        if (_cfg.X is double x && _cfg.Y is double y && !_dragging)
        {
            if (double.IsNaN(Left) || Math.Abs(Left - x) > 0.5) Left = x;
            if (double.IsNaN(Top) || Math.Abs(Top - y) > 0.5) Top = y;
        }
        UpdateFrame();
        KeepOnScreen(false);
    }

    public void SetEditMode(bool on)
    {
        _edit = on;
        ClickThrough = !on;
        Cursor = on ? Cursors.SizeAll : null;
        UpdateFrame();
    }

    private void UpdateFrame()
    {
        // в режиме правки фон не может быть полностью прозрачным — иначе окно нечем «схватить»
        double bg = Math.Clamp(_cfg.BgOpacity, 0, 1);
        if (_edit) bg = Math.Max(bg, 0.25);
        _card.Background = new SolidColorBrush(Color.FromArgb((byte)(bg * 255), 0x14, 0x16, 0x1B));
        _card.BorderBrush = _edit
            ? new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF))
            : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        _card.BorderThickness = new Thickness(_edit ? 2 : (_cfg.BgOpacity < 0.03 ? 0 : 1));
    }

    // ---------- границы экрана ----------

    private static Rect ScreenBoundsDip(System.Drawing.Point pxPoint, double k)
    {
        var s = System.Windows.Forms.Screen.FromPoint(pxPoint).Bounds;
        return new Rect(s.Left / k, s.Top / k, s.Width / k, s.Height / k);
    }

    private static Point Clamp(double left, double top, double w, double h, Rect b)
    {
        double l = Math.Min(Math.Max(left, b.Left), Math.Max(b.Left, b.Right - w));
        double t = Math.Min(Math.Max(top, b.Top), Math.Max(b.Top, b.Bottom - h));
        return new Point(l, t);
    }

    /// <summary>Возвращает окно целиком на экран (например, после смены размера или разрешения).</summary>
    private void KeepOnScreen(bool persist)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || double.IsNaN(Left) || double.IsNaN(Top)) return;
        double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var center = new System.Drawing.Point((int)((Left + w / 2) * k), (int)((Top + h / 2) * k));
        var p = Clamp(Left, Top, w, h, ScreenBoundsDip(center, k));
        if (Math.Abs(p.X - Left) > 0.5) Left = p.X;
        if (Math.Abs(p.Y - Top) > 0.5) Top = p.Y;
        if (persist && _cfg.X.HasValue && _cfg.Y.HasValue &&
            (Math.Abs(_cfg.X.Value - Left) > 0.5 || Math.Abs(_cfg.Y.Value - Top) > 0.5))
        {
            _cfg.X = Math.Round(Left); _cfg.Y = Math.Round(Top);
        }
    }

    // ---------- перетаскивание ----------

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_edit || e.ChangedButton != MouseButton.Left) return;
        Native.GetCursorPos(out _startCursor);
        _startLeft = Left; _startTop = Top;
        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
        Native.GetCursorPos(out var cur);
        double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double nl = _startLeft + (cur.X - _startCursor.X) / k;
        double nt = _startTop + (cur.Y - _startCursor.Y) / k;
        var bounds = ScreenBoundsDip(new System.Drawing.Point(cur.X, cur.Y), k);   // экран под курсором
        var p = Clamp(nl, nt, ActualWidth, ActualHeight, bounds);
        Left = p.X; Top = p.Y;
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        _cfg.X = Math.Round(Left);
        _cfg.Y = Math.Round(Top);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_edit) return;
        _cfg.Scale = Math.Clamp(Math.Round(_cfg.Scale + e.Delta / 1200.0, 2), 0.15, 2.5);
    }
}
