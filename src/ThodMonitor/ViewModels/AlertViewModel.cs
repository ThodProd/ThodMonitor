using System.Collections.ObjectModel;
using System.Windows;

namespace ThodMonitor.ViewModels;

public sealed class AlertViewModel
{
    public string Title { get; init; } = "";
    public string Subtitle { get; set; } = "";
    public string IconName { get; init; } = "warn";
    public string RingText { get; init; } = "";
    public double RingValue { get; init; }
    public double Warn { get; init; } = 60;
    public double Crit { get; init; } = 85;
    public bool Compact { get; init; }
    public ObservableCollection<string> Lines { get; } = new();
    public Visibility LinesVisibility => Compact ? Visibility.Collapsed : Visibility.Visible;
}
