using System.IO;
using System.Text.Json;
using ThodMonitor.Models;

namespace ThodMonitor.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static string Dir => Environment.GetEnvironmentVariable("THODHUB_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThodMonitor");
    private static string FilePath => Path.Combine(Dir, "settings.json");
    private Timer? _timer;

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Файл настроек повреждён, используются значения по умолчанию", ex);
            try { File.Move(FilePath, FilePath + ".broken", true); } catch { }
            Settings = new AppSettings();
        }
        Settings.Sanitize();
        if (!Settings.PresetApplied)
        {
            PresetService.Apply(Settings, "Circles");   // красивый вид «из коробки»
            Settings.PresetApplied = true;
        }
        Settings.Hook();
        Settings.Changed += QueueSave;
        QueueSave();
    }

    private void QueueSave()
    {
        _timer ??= new Timer(_ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(Flush)));
        _timer.Change(400, Timeout.Infinite); // дебаунс
    }

    public void Flush()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, Options));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex) { Log.Error("Не удалось сохранить настройки", ex); }
    }
}
