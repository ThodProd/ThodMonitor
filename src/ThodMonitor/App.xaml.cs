using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using ThodMonitor.Services;
using ThodMonitor.ViewModels;
using ThodMonitor.Views;
using Forms = System.Windows.Forms;

namespace ThodMonitor;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _wait;
    private SettingsService? _settings;
    private SensorService? _sensors;
    private FpsService? _fps;
    private MainViewModel? _vm;
    private MainWindow? _main;
    private WidgetManager? _widgets;
    private EdgePanelWindow? _edge;
    private AlertManager? _alerts;
    private AlertEngine? _engine;
    private HotkeyService? _hotkey;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _overlaysItem, _editItem, _alertsItem, _pauseItem;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        var dataArg=Array.IndexOf(e.Args,"--thodhub-data");
        if(dataArg>=0 && dataArg+1<e.Args.Length)
            Environment.SetEnvironmentVariable("THODHUB_DATA_DIR",System.IO.Path.GetFullPath(e.Args[dataArg+1]));
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Манифест требует администратора, но если процесс всё же запущен без прав (например, обходным путём),
        // перезапускаемся с запросом UAC и выходим.
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Join(" ", e.Args)) { UseShellExecute = true, Verb = "runas" });
            }
            catch (Exception ex) { Log.Error("Не удалось получить права администратора", ex); }
            Shutdown();
            return;
        }

        // единственный экземпляр; при перезапуске от администратора ждём освобождения
        bool restart = e.Args.Contains("--restart");
        _mutex = new Mutex(false, @"Local\ThodMonitor.Single");
        try { _ownsMutex = _mutex.WaitOne(restart ? 8000 : 0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ThodMonitor.Show");
        if (!_ownsMutex) { _showEvent.Set(); Shutdown(); return; }

        DispatcherUnhandledException += (_, a) => { Log.Error("Необработанное исключение UI", a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("Фатальное исключение", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Необработанное исключение задачи", a.Exception); a.SetObserved(); };
        Log.Info("Запуск Thod Monitor");

        _settings = new SettingsService();
        _settings.Load();
        var s = _settings.Settings;

        _sensors = new SensorService { IntervalMs = s.PollIntervalMs };
        _fps = new FpsService();
        _vm = new MainViewModel(_settings, _sensors, _fps);
        _widgets = new WidgetManager(s, _vm);
        _edge = new EdgePanelWindow(s.Edge, _vm);
        _alerts = new AlertManager(s, _vm);
        _engine = new AlertEngine(s, _vm);
        _engine.Fired += _alerts.OnFired;
        _vm.TestAlertRequested += _alerts.Test;
        _vm.StateChanged += ApplyState;
        _vm.ExitRequested += ExitApp;

        _main = new MainWindow(_vm);
        _main.Closing += OnMainClosing;

        CreateTray();
        _hotkey = new HotkeyService();
        _hotkey.Pressed += () => s.OverlaysVisible = !s.OverlaysVisible;
        _hotkey.Start();

        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowMain)), null, -1, false);

        _sensors.Updated += _vm.Push;
        _sensors.Start();
        if (s.FpsEnabled) _fps.Start(s.PresentMonPath, s.FpsArgs);
        _edge.Start();
        ApplyState();

        // автозапуск: старая запись HKCU\Run -> задача планировщика с правами администратора (в фоне)
        Task.Run(() => AutostartService.Reconcile()).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => _vm?.RefreshAutostart())));

        if (!(e.Args.Contains("--minimized") || s.StartMinimized)) ShowMain();
    }

    private void ShowMain()
    {
        if (_main == null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void OnMainClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        if (_settings!.Settings.CloseToTray) _main!.Hide();
        else Dispatcher.BeginInvoke(new Action(ExitApp)); // Close() внутри Closing запрещён
    }

    /// <summary>Синхронизирует окна оверлеев и пункты меню трея с текущим состоянием.</summary>
    private void ApplyState()
    {
        if (_vm == null || _widgets == null) return;
        _widgets.Sync();
        var s = _vm.Settings;
        if (_overlaysItem != null) _overlaysItem.Checked = s.OverlaysVisible;
        if (_editItem != null) _editItem.Checked = _vm.EditMode;
        if (_alertsItem != null) _alertsItem.Checked = s.Alerts.Enabled;
        if (_pauseItem != null) _pauseItem.Checked = _vm.MonitoringPaused;
    }

    private void CreateTray()
    {
        System.Drawing.Icon icon;
        try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application; }
        catch { icon = System.Drawing.SystemIcons.Application; }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть Thod Monitor", null, (_, _) => ShowMain());
        _overlaysItem = new Forms.ToolStripMenuItem("Показывать оверлеи (Ctrl+Alt+O)");
        _overlaysItem.Click += (_, _) => _vm!.Settings.OverlaysVisible = !_vm.Settings.OverlaysVisible;
        menu.Items.Add(_overlaysItem);
        _editItem = new Forms.ToolStripMenuItem("Режим правки оверлеев");
        _editItem.Click += (_, _) => _vm!.EditMode = !_vm.EditMode;
        menu.Items.Add(_editItem);
        _alertsItem = new Forms.ToolStripMenuItem("Уведомления");
        _alertsItem.Click += (_, _) => _vm!.Settings.Alerts.Enabled = !_vm.Settings.Alerts.Enabled;
        menu.Items.Add(_alertsItem);
        _pauseItem = new Forms.ToolStripMenuItem("Приостановить мониторинг");
        _pauseItem.Click += (_, _) => _vm!.MonitoringPaused = !_vm.MonitoringPaused;
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon { Text = "Thod Monitor", Icon = icon, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("Завершение работы");
        try { _wait?.Unregister(null); } catch { }
        try { _hotkey?.Dispose(); } catch { }
        try { _edge?.Stop(); _edge?.Close(); } catch { }
        try { _alerts?.CloseAll(); } catch { }
        try { _widgets?.Dispose(); } catch { }
        try { _fps?.Dispose(); } catch (Exception ex) { Log.Error("Ошибка остановки FPS", ex); }
        try { _sensors?.Dispose(); } catch (Exception ex) { Log.Error("Ошибка остановки датчиков", ex); }
        try { _settings?.Flush(); } catch { }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _main?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { if (_ownsMutex) _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _showEvent?.Dispose();
        base.OnExit(e);
    }
}
