using System.Diagnostics;

namespace ThodMonitor.Services;

/// <summary>Самые «тяжёлые» процессы — для карточек уведомлений. Вызывается только в момент события.</summary>
public static class ProcessInfoService
{
    public static async Task<List<string>> TopCpuAsync(int count = 3)
    {
        var a = Snapshot();
        var t0 = Stopwatch.GetTimestamp();
        await Task.Delay(800);
        var b = Snapshot();
        double elapsed = Stopwatch.GetElapsedTime(t0).TotalSeconds * Environment.ProcessorCount;
        var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pid, (name, cpu)) in b)
        {
            if (!a.TryGetValue(pid, out var prev)) continue;
            double pct = (cpu - prev.cpu).TotalSeconds / elapsed * 100;
            if (pct <= 0.2) continue;
            byName[name] = byName.GetValueOrDefault(name) + pct;
        }
        return byName.OrderByDescending(k => k.Value).Take(count)
            .Select(k => $"{k.Key} — {k.Value:0}%").ToList();
    }

    public static Task<List<string>> TopMemoryAsync(int count = 3) => Task.Run(() =>
    {
        var byName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { byName[p.ProcessName] = byName.GetValueOrDefault(p.ProcessName) + p.WorkingSet64; }
            catch { }
            finally { p.Dispose(); }
        }
        return byName.OrderByDescending(k => k.Value).Take(count)
            .Select(k => $"{k.Key} — {k.Value / 1024.0 / 1024 / 1024:0.0} ГБ").ToList();
    });

    private static Dictionary<int, (string name, TimeSpan cpu)> Snapshot()
    {
        var d = new Dictionary<int, (string, TimeSpan)>();
        foreach (var p in Process.GetProcesses())
        {
            try { d[p.Id] = (p.ProcessName, p.TotalProcessorTime); }
            catch { /* нет доступа к системному процессу */ }
            finally { p.Dispose(); }
        }
        return d;
    }
}
