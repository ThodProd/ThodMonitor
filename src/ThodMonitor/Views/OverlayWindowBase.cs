using System.Windows;
using System.Windows.Media;
using ThodMonitor.Services;

namespace ThodMonitor.Views;

/// <summary>Прозрачное окно оверлея: без рамки, без кнопки на панели задач, без захвата фокуса.</summary>
public abstract class OverlayWindowBase : Window
{
    private bool _clickThrough = true;

    protected OverlayWindowBase()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
    }

    /// <summary>true — клики проходят сквозь окно (обычный режим); false — окно можно перетаскивать.</summary>
    public bool ClickThrough
    {
        get => _clickThrough;
        set { _clickThrough = value; Native.SetClickThrough(this, value); }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.SetClickThrough(this, _clickThrough);
    }
}
