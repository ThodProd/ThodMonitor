using System.Diagnostics;
using System.IO;
using LibreHardwareMonitor.Hardware;
using ThodMonitor.Models;

namespace ThodMonitor.Services;

/// <summary>
/// Единственный цикл опроса датчиков (фоновая задача). Ошибка одной секции или датчика
/// не прерывает опрос: такой показатель просто отдаётся как «Н/Д».
/// </summary>
public sealed class SensorService : IDisposable
{
    private Computer? _computer;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _paused;
    private readonly object _problemsGate = new();
    private readonly List<string> _problems = new();

    public bool Paused { get => _paused; set => _paused = value; }
    public int IntervalMs { get; set; } = 1000;
    public event Action<IReadOnlyList<MetricSample>>? Updated;

    public string[] GetProblems() { lock (_problemsGate) return _problems.ToArray(); }

    public void Start() => Launch(openFirst: true);

    public void Reopen() { Stop(); OpenComputer(); Launch(openFirst: false); }

    private void Launch(bool openFirst)
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(async () =>
        {
            if (openFirst) OpenComputer();
            await LoopAsync(ct);
        });
    }

    private void Stop()
    {
        try { _cts?.Cancel(); _loop?.Wait(3000); } catch { }
        _cts?.Dispose(); _cts = null; _loop = null;
        try { _computer?.Close(); } catch (Exception ex) { Log.Error("Ошибка закрытия датчиков", ex); }
        _computer = null;
    }

    public void Dispose() => Stop();

    private void OpenComputer()
    {
        lock (_problemsGate) _problems.Clear();
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
                IsStorageEnabled = true, IsNetworkEnabled = true
            };
            _computer.Open();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось инициализировать библиотеку датчиков", ex);
            lock (_problemsGate) _problems.Add("Библиотека датчиков не запустилась: " + ex.Message);
            _computer = null;
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            if (!_paused)
            {
                try { Updated?.Invoke(Collect()); }
                catch (Exception ex) { Log.Error("Ошибка опроса датчиков", ex); }
            }
            var delay = Math.Max(100, IntervalMs - (int)sw.ElapsedMilliseconds);
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // ---------- сбор ----------

    private List<MetricSample> Collect()
    {
        var r = new List<MetricSample>(16);
        var hw = new List<IHardware>();
        if (_computer != null)
        {
            foreach (var h in _computer.Hardware)
            {
                try { h.Update(); } catch (Exception ex) { Log.Error($"Ошибка обновления «{h.Name}»", ex); }
            }
            hw = Flatten(_computer.Hardware).ToList();
        }

        void Add(string id, double? v, string naText, string? note = null) =>
            r.Add(new MetricSample(id, v, v.HasValue ? note : naText));

        const string adminHint = "Датчик недоступен (на многих ПК для него нужны права администратора)";
        const string na = "Датчик недоступен";

        Section("CPU", () =>
        {
            var cpuHw = hw.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            var s = SensorsOf(cpuHw);
            Add("cpu.load", Pick(s, SensorType.Load, "CPU Total"), na, cpuHw?.Name);
            Add("cpu.temp", Pick(s, SensorType.Temperature, "Package", "Tctl", "Tdie", "Core Max", "Average"), adminHint);
            Add("cpu.power", Pick(s, SensorType.Power, "Package"), adminHint);
            var clocks = s.Where(x => x.SensorType == SensorType.Clock && x.Name.StartsWith("Core #") && x.Value.HasValue)
                          .Select(x => (double)x.Value!.Value).ToList();
            Add("cpu.clock", clocks.Count > 0 ? clocks.Average() : null, adminHint, "среднее по ядрам");
        });

        Section("GPU", () =>
        {
            var gpu = hw.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
                      ?? hw.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
            var s = SensorsOf(gpu);
            const string noGpu = "Видеокарта или её датчики не обнаружены";
            Add("gpu.load", Pick(s, SensorType.Load, "GPU Core", "D3D 3D"), noGpu, gpu?.Name);
            Add("gpu.temp", Pick(s, SensorType.Temperature, "GPU Core"), noGpu);
            var used = Exact(s, SensorType.SmallData, "GPU Memory Used");
            var total = Exact(s, SensorType.SmallData, "GPU Memory Total");
            Add("gpu.vram", used.HasValue && total is > 0 ? used / total * 100 : null, noGpu,
                used.HasValue && total.HasValue ? $"{used / 1024:F1} / {total / 1024:F1} ГБ" : null);
            Add("gpu.power", Pick(s, SensorType.Power, "GPU Package", "GPU Power"), noGpu);
            Add("gpu.clock", Pick(s, SensorType.Clock, "GPU Core"), noGpu);
        });

        Section("RAM", () =>
        {
            var s = SensorsOf(hw.FirstOrDefault(h => h.HardwareType == HardwareType.Memory));
            var used = Exact(s, SensorType.Data, "Memory Used");
            var avail = Exact(s, SensorType.Data, "Memory Available");
            Add("ram.load", Exact(s, SensorType.Load, "Memory"), na,
                used.HasValue && avail.HasValue ? $"{used:F1} / {used + avail:F1} ГБ" : null);
        });

        Section("Диски", () =>
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory)!;
            var di = new DriveInfo(root);
            const double gb = 1024.0 * 1024 * 1024;
            double total = di.TotalSize / gb, free = di.AvailableFreeSpace / gb;
            Add("disk.used", (1 - free / total) * 100, "Нет данных о диске",
                $"{free:F0} ГБ свободно из {total:F0} ГБ ({root})");

            double? act = null, io = null;
            foreach (var d in hw.Where(h => h.HardwareType == HardwareType.Storage))
            {
                var a = Exact(d.Sensors, SensorType.Load, "Total Activity");
                if (a.HasValue) act = Math.Max(act ?? 0, a.Value);
                var rd = Exact(d.Sensors, SensorType.Throughput, "Read Rate");
                var wr = Exact(d.Sensors, SensorType.Throughput, "Write Rate");
                if (rd.HasValue || wr.HasValue) io = (io ?? 0) + (rd ?? 0) + (wr ?? 0);
            }
            Add("disk.activity", act, na, "максимум по накопителям");
            Add("disk.io", io / 1_000_000, na, "сумма по накопителям");
        });

        Section("Сеть", () =>
        {
            double? down = null, up = null;
            foreach (var n in hw.Where(h => h.HardwareType == HardwareType.Network))
            {
                var d = Exact(n.Sensors, SensorType.Throughput, "Download Speed");
                var u = Exact(n.Sensors, SensorType.Throughput, "Upload Speed");
                if (d.HasValue) down = (down ?? 0) + d.Value;
                if (u.HasValue) up = (up ?? 0) + u.Value;
            }
            const string noNet = "Сетевые адаптеры не найдены";
            Add("net.down", down / 1_000_000, noNet, "сумма по всем адаптерам");
            Add("net.up", up / 1_000_000, noNet, "сумма по всем адаптерам");
        });

        return r;
    }

    private static void Section(string name, Action body)
    {
        try { body(); } catch (Exception ex) { Log.Error($"Ошибка чтения секции «{name}»", ex); }
    }

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> items)
    {
        foreach (var h in items)
        {
            yield return h;
            foreach (var sub in Flatten(h.SubHardware)) yield return sub;
        }
    }

    private static List<ISensor> SensorsOf(IHardware? h)
    {
        var list = new List<ISensor>();
        if (h == null) return list;
        list.AddRange(h.Sensors);
        foreach (var sub in h.SubHardware) list.AddRange(sub.Sensors);
        return list;
    }

    /// <summary>Первый датчик типа <paramref name="type"/>, имя которого содержит одну из подстрок (по порядку предпочтения).</summary>
    private static double? Pick(IEnumerable<ISensor> sensors, SensorType type, params string[] names)
    {
        var list = sensors as IList<ISensor> ?? sensors.ToList();
        foreach (var n in names)
        {
            var s = list.FirstOrDefault(x => x.SensorType == type && x.Value.HasValue &&
                                             x.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (s != null) return s.Value!.Value;
        }
        return null;
    }

    private static double? Exact(IEnumerable<ISensor> sensors, SensorType type, string name)
    {
        var s = sensors.FirstOrDefault(x => x.SensorType == type && x.Value.HasValue &&
                                            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return s?.Value;
    }
}
