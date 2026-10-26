using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ThodMonitor.ViewModels;

namespace ThodMonitor.Views;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    private void UseDarkTitleBar()
    {
        try
        {
            int on = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 2004+ / 11)
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
        }
        catch { /* косметика: на старых Windows тёмный заголовок недоступен */ }
    }
}
