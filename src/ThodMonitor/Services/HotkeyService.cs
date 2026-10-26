using System.Windows.Interop;

namespace ThodMonitor.Services;

/// <summary>Глобальная горячая клавиша Ctrl+Alt+O — показать/скрыть оверлеи.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312, Id = 0x7E01;
    private HwndSource? _src;
    public event Action? Pressed;

    public void Start()
    {
        try
        {
            _src = new HwndSource(new HwndSourceParameters("ThodMonitorHotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
            _src.AddHook(Hook);
            // MOD_ALT=1, MOD_CONTROL=2, MOD_NOREPEAT=0x4000, 'O'=0x4F
            if (!Native.RegisterHotKey(_src.Handle, Id, 0x4003, 0x4F))
                Log.Error("Горячая клавиша Ctrl+Alt+O занята другой программой");
        }
        catch (Exception ex) { Log.Error("Не удалось зарегистрировать горячую клавишу", ex); }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY && w.ToInt32() == Id) { Pressed?.Invoke(); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try { if (_src != null) { Native.UnregisterHotKey(_src.Handle, Id); _src.Dispose(); } } catch { }
    }
}
