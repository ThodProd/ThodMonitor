namespace ThodMonitor.Models;

public sealed record MetricDef(string Id, string Title, string Unit, double? FixedMax = null);

/// <summary>Результат чтения. Value == null означает «Н/Д», Note объясняет причину или даёт пояснение.</summary>
public sealed record MetricSample(string Id, double? Value, string? Note);

public static class MetricCatalog
{
    public static readonly MetricDef[] All =
    {
        new("cpu.load",      "CPU · загрузка",          "%", 100),
        new("cpu.temp",      "CPU · температура",       "°C"),
        new("cpu.power",     "CPU · мощность",          "Вт"),
        new("cpu.clock",     "CPU · частота",           "МГц"),
        new("gpu.load",      "GPU · загрузка",          "%", 100),
        new("gpu.temp",      "GPU · температура",       "°C"),
        new("gpu.vram",      "VRAM · занято",           "%", 100),
        new("gpu.power",     "GPU · мощность",          "Вт"),
        new("gpu.clock",     "GPU · частота",           "МГц"),
        new("ram.load",      "RAM · занято",            "%", 100),
        new("disk.used",     "Системный диск · занято", "%", 100),
        new("disk.activity", "Диски · активность",      "%", 100),
        new("disk.io",       "Диски · чтение+запись",   "МБ/с"),
        new("net.down",      "Сеть · приём",            "МБ/с"),
        new("net.up",        "Сеть · отдача",           "МБ/с"),
        new("fps.now",       "FPS",                     "FPS"),
        new("fps.avg",       "FPS · средний (10 с)",    "FPS"),
        new("fps.low",       "FPS · 1% Low (30 с)",     "FPS"),
        new("fps.ft",        "Время кадра",             "мс"),
    };
}
