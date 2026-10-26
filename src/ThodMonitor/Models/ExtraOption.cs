using CommunityToolkit.Mvvm.ComponentModel;

namespace ThodMonitor.Models;

public sealed record MetricOption(string Id, string Label);

/// <summary>Пункт «чип» для включения дополнительного показателя.</summary>
public sealed partial class ExtraOption : ObservableObject
{
    private readonly Action<ExtraOption> _changed;
    public ExtraOption(string id, string label, bool isChecked, Action<ExtraOption> changed)
    { Id = id; Label = label; this.isChecked = isChecked; _changed = changed; }
    public string Id { get; }
    public string Label { get; }
    [ObservableProperty] private bool isChecked;
    partial void OnIsCheckedChanged(bool value) => _changed(this);
}
