using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ThodMonitor.Services;

public sealed record FpsSnapshot(bool HasData, string Process, double Now, double Avg, double? Low1, double FrameMs);

/// <summary>
/// Реальный FPS/frametime через внешний PresentMon (ETW). Мы НЕ внедряемся в игры.
/// PresentMon не входит в комплект и не скачивается автоматически: положите PresentMon*.exe в папку «PresentMon» рядом с программой.
/// Обычно нужны права администратора. Заголовки CSV определяются автоматически (MsBetweenPresents или FrameTime).
/// </summary>
public sealed class FpsService : IDisposable
{
    /// <summary>Как долго держим последнее значение, если кадры на пару секунд пропали (смена сцены, пауза).</summary>
    private const int HoldMs = 4000;

    private readonly record struct Frame(long Ticks, double Ms);
    private readonly Dictionary<int, (string Name, Queue<Frame> Frames)> _data = new();
    private readonly object _gate = new();
    private Process? _proc;
    private CancellationTokenSource? _cts;
    private volatile string _lastErr = "";
    private int _pick;                  // выбранный процесс — держимся за него, чтобы FPS не прыгал между окнами
    private FpsSnapshot? _last;         // последний удачный снимок
    private long _lastGoodTicks;

    public int WindowMs { get; set; } = 500;   // окно усреднения времени кадра
    public string Status { get; private set; } = "FPS не запущен";
    public event Action<FpsSnapshot>? Updated;
    public event Action<string>? StatusChanged;

    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "PresentMon");
    public string? ResolvedExe { get; private set; }

    private void SetStatus(string s) { Status = s; Log.Info("FPS: " + s); StatusChanged?.Invoke(s); }

    public void Start(string customPath, string args)
    {
        Stop();
        var exe = Resolve(customPath);
        ResolvedExe = exe;
        if (exe == null)
        {
            SetStatus("PresentMon не найден. Положите PresentMon*.exe в папку «PresentMon» рядом с программой.");
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            _proc = Process.Start(psi);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            SetStatus("Не удалось запустить PresentMon: " + ex.Message);
            return;
        }
        if (_proc == null) { SetStatus("Не удалось запустить PresentMon."); return; }

        _lastErr = "";
        _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _lastErr = e.Data; };
        _proc.BeginErrorReadLine();
        SetStatus("PresentMon запущен, ждём кадры игры…");
        _cts = new CancellationTokenSource();
        var ct = _cts.Token; var p = _proc;
        Task.Run(() => ReadLoop(p, ct));
        Task.Run(() => AnalyseLoop(ct));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
        try { _proc?.Dispose(); } catch { }
        _proc = null; _cts = null;
        lock (_gate) { _data.Clear(); _pick = 0; _last = null; _lastGoodTicks = 0; }
    }

    public void Dispose() => Stop();

    private static string? Resolve(string custom)
    {
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;
        try
        {
            if (Directory.Exists(DefaultFolder))
                return Directory.EnumerateFiles(DefaultFolder, "PresentMon*.exe").OrderByDescending(f => f).FirstOrDefault();
        }
        catch { }
        return null;
    }

    private async Task ReadLoop(Process p, CancellationToken ct)
    {
        int iApp = -1, iPid = -1, iMs = -1;
        try
        {
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
            {
                if (iMs < 0)
                {
                    var cols = line.Split(',');
                    iApp = Array.FindIndex(cols, c => c is "Application" or "ProcessName");
                    iPid = Array.IndexOf(cols, "ProcessID");
                    iMs = Array.FindIndex(cols, c => c is "MsBetweenPresents" or "FrameTime");
                    if (iApp < 0 || iPid < 0 || iMs < 0) { iMs = -1; }   // это не заголовок
                    continue;
                }
                var f = line.Split(',');
                if (f.Length <= Math.Max(iMs, Math.Max(iApp, iPid))) continue;
                if (!int.TryParse(f[iPid], out var pid)) continue;
                if (!double.TryParse(f[iMs], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms <= 0) continue;
                Add(pid, f[iApp], ms);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Log.Error("Ошибка чтения вывода PresentMon", ex); }

        if (!ct.IsCancellationRequested)
        {
            int code = -1;
            try { p.WaitForExit(1000); code = p.ExitCode; } catch { }
            SetStatus($"PresentMon завершился (код {code}). {_lastErr} Обычно помогает запуск от администратора.");
        }
    }

    private void Add(int pid, string name, double ms)
    {
        long now = DateTime.UtcNow.Ticks;
        lock (_gate)
        {
            if (!_data.TryGetValue(pid, out var e)) { e = (name, new Queue<Frame>()); _data[pid] = e; }
            e.Frames.Enqueue(new Frame(now, ms));
            while (e.Frames.Count > 0 && (now - e.Frames.Peek().Ticks > 30 * TimeSpan.TicksPerSecond || e.Frames.Count > 30000))
                e.Frames.Dequeue();
        }
    }

    private async Task AnalyseLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Updated?.Invoke(Compute()); } catch (Exception ex) { Log.Error("Ошибка расчёта FPS", ex); }
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private FpsSnapshot Compute()
    {
        long now = DateTime.UtcNow.Ticks;
        long s1 = TimeSpan.TicksPerSecond;
        string name = ""; Frame[] frames; int pick;
        lock (_gate)
        {
            int fg = Native.ForegroundPid();
            int best = 0; pick = 0;
            foreach (var (pid, e) in _data)
            {
                // мы никогда не измеряем сами себя: PresentMon пишет все процессы, а наше окно
                // почти всегда в фокусе и кадры даёт рывками — из-за этого FPS «мигал»
                if (pid == Environment.ProcessId || e.Name.Equals("ThodMonitor.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (e.Name.Equals("dwm.exe", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                int recent = e.Frames.Count(f => now - f.Ticks <= 2 * s1);
                if (recent < 3) continue;
                if (pid == _pick) { pick = pid; best = int.MaxValue; break; }   // держимся за то же окно
                if (pid == fg) { pick = pid; best = int.MaxValue - 1; continue; }  // иначе — активное окно
                if (recent > best) { best = recent; pick = pid; }
            }
            if (pick != 0) _pick = pick;
            if (pick == 0)
            {
                // короткий пропуск кадров не должен гасить показание — держим прежнее значение
                if (_last is { HasData: true } && now - _lastGoodTicks <= HoldMs * TimeSpan.TicksPerMillisecond) return _last;
                return new FpsSnapshot(false, "", 0, 0, null, 0);
            }
            name = _data[pick].Name;
            frames = _data[pick].Frames.ToArray();
        }

        var last10 = frames.Where(f => now - f.Ticks <= 10 * s1).ToArray();
        long win = TimeSpan.TicksPerMillisecond * Math.Max(100, WindowMs);
        var lastW = frames.Where(f => now - f.Ticks <= win).ToArray();
        // основной показатель: среднее время кадра в окне -> FPS = 1000 / время кадра (как в игровых оверлеях: проседает при нагрузке)
        double ft = lastW.Length > 0 ? lastW.Average(f => f.Ms) : 0;
        double fpsNow = ft > 0 ? 1000.0 / ft : 0;
        double avg = last10.Length > 0 ? 1000.0 / last10.Average(f => f.Ms) : 0;
        double? low = null;
        if (frames.Length >= 100)
        {
            var sorted = frames.Select(f => f.Ms).OrderBy(x => x).ToArray();
            int idx = Math.Clamp((int)Math.Ceiling(sorted.Length * 0.99) - 1, 0, sorted.Length - 1);
            low = 1000.0 / sorted[idx];   // 1% Low = 1000 / 99-й перцентиль времени кадра за последние 30 с
        }
        var snap = new FpsSnapshot(true, name, fpsNow, avg, low, ft);
        lock (_gate) { _last = snap; _lastGoodTicks = now; }
        return snap;
    }
}
