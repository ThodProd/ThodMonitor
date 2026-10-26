namespace ThodMonitor.Models;

/// <summary>Русские подписи и иконки показателей.</summary>
public static class MetricLabels
{
    // Id → (короткая подпись строки, полная подпись блока, иконка)
    private static readonly Dictionary<string, (string Row, string Caption, string Icon)> M = new()
    {
        ["cpu.load"] = ("Загрузка", "Процессор", "cpu"),
        ["cpu.temp"] = ("Температура", "Температура процессора", "thermo"),
        ["cpu.power"] = ("Мощность", "Мощность процессора", "bolt"),
        ["cpu.clock"] = ("Частота", "Частота процессора", "clock"),
        ["gpu.load"] = ("Загрузка", "Видеокарта", "gpu"),
        ["gpu.temp"] = ("Температура", "Температура видеокарты", "thermo"),
        ["gpu.vram"] = ("Видеопамять", "Видеопамять", "vram"),
        ["gpu.power"] = ("Мощность", "Мощность видеокарты", "bolt"),
        ["gpu.clock"] = ("Частота", "Частота видеокарты", "clock"),
        ["ram.load"] = ("Память", "Оперативная память", "ram"),
        ["disk.used"] = ("Занято", "Системный диск", "disk"),
        ["disk.activity"] = ("Активность", "Активность дисков", "disk"),
        ["disk.io"] = ("Скорость", "Скорость дисков", "disk"),
        ["net.down"] = ("Приём", "Сеть: приём", "net"),
        ["net.up"] = ("Отдача", "Сеть: отдача", "net"),
        ["fps.now"] = ("FPS", "FPS", "fps"),
        ["fps.avg"] = ("Средний", "FPS средний", "fps"),
        ["fps.low"] = ("1% Low", "FPS 1% Low", "fps"),
        ["fps.ft"] = ("Кадр", "Время кадра", "fps"),
    };

    public static string Row(string id) => M.TryGetValue(id, out var v) ? v.Row : id;
    public static string Caption(string id) => M.TryGetValue(id, out var v) ? v.Caption : id;
    public static string Icon(string id) => M.TryGetValue(id, out var v) ? v.Icon : "pulse";
    public static string Group(string id) => id.Split('.')[0];
}
