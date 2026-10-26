using System.IO;

namespace ThodMonitor.Services;

/// <summary>Простой файловый журнал с ротацией (до ~1 МБ + одна архивная копия) и подавлением повторов.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> LastSeen = new();
    private static string Dir => Path.Combine(Environment.GetEnvironmentVariable("THODHUB_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThodMonitor"), "logs");
    public static string FilePath => Path.Combine(Dir, "thod.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                // одинаковые сообщения не чаще раза в минуту
                var key = level + message;
                if (LastSeen.TryGetValue(key, out var t) && (DateTime.UtcNow - t).TotalSeconds < 60) return;
                LastSeen[key] = DateTime.UtcNow;
                if (LastSeen.Count > 200) LastSeen.Clear();

                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000)
                    File.Move(FilePath, Path.Combine(Dir, "thod.old.log"), true);

                var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}" +
                           (ex != null ? Environment.NewLine + ex : "") + Environment.NewLine;
                File.AppendAllText(FilePath, text);
            }
        }
        catch { /* журнал не должен ронять приложение */ }
    }
}
