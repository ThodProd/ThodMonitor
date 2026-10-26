using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ThodMonitor.Models;

namespace ThodMonitor.ViewModels;

/// <summary>Строка дополнительного показателя внутри блока панели.</summary>
public sealed class ExtraRowViewModel : ObservableObject
{
    private readonly PropertyChangedEventHandler _h;
    public MetricCardViewModel Card { get; }
    public string Label { get; }
    public string Icon { get; }

    public ExtraRowViewModel(MetricCardViewModel card, string label)
    {
        Card = card; Label = label; Icon = MetricLabels.Icon(card.Id);
        _h = (_, e) =>
        {
            if (e.PropertyName is nameof(MetricCardViewModel.ValueText) or nameof(MetricCardViewModel.Note)
                or nameof(MetricCardViewModel.IsAvailable) or nameof(MetricCardViewModel.StateBrush) or nameof(MetricCardViewModel.NumericValue))
            {
                OnPropertyChanged(nameof(Text)); OnPropertyChanged(nameof(Brush)); OnPropertyChanged(nameof(HasBar));
            }
        };
        card.PropertyChanged += _h;
    }

    // для памяти и диска показываем «занято / всего, ГБ», для остальных — значение с единицей
    public string Text => !Card.IsAvailable ? "Н/Д"
        : Card.Id is "gpu.vram" or "ram.load" or "disk.used" && !string.IsNullOrEmpty(Card.Note) ? Card.Note!
        : $"{Card.ValueText} {Card.UnitText}".Trim();
    public Brush Brush => Card.StateBrush;
    public bool HasBar => Card.Unit == "%" && Card.IsAvailable;
    public void Detach() => Card.PropertyChanged -= _h;
}

/// <summary>Живая модель блока панели (настройки блока + текущие данные). Используется и в панели, и в редакторе.</summary>
public sealed partial class EdgeBlockViewModel : ObservableObject
{
    private readonly MainViewModel _vm;
    private readonly PropertyChangedEventHandler _cfgH, _setH;
    private readonly NotifyCollectionChangedEventHandler _extrasH;

    public EdgeBlock Config { get; }
    public ObservableCollection<ExtraRowViewModel> Extras { get; } = new();
    [ObservableProperty] private MetricCardViewModel? primary;
    [ObservableProperty] private bool isSelected;

    public EdgeBlockViewModel(EdgeBlock cfg, MainViewModel vm)
    {
        Config = cfg; _vm = vm;
        _cfgH = (_, _) => { Refresh(); RefreshExtras(); };
        _extrasH = (_, _) => RefreshExtras();
        _setH = (_, e) => { if (e.PropertyName == nameof(AppSettings.ShowLabels)) OnPropertyChanged(nameof(CaptionVisible)); };
        cfg.PropertyChanged += _cfgH;
        cfg.Extras.CollectionChanged += _extrasH;
        vm.Settings.PropertyChanged += _setH;
        Refresh(); RefreshExtras();
    }

    public string Caption => !string.IsNullOrWhiteSpace(Config.Title) ? Config.Title : MetricLabels.Caption(Config.Primary);
    public bool CaptionVisible => Config.ShowTitle && _vm.Settings.ShowLabels;
    public string Icon => MetricLabels.Icon(Config.Primary);
    public bool ShowPrimaryNote => Config.Primary is "ram.load" or "gpu.vram" or "disk.used";
    public double RingSize => Config.Size switch { "S" => 64.0, "L" => 108.0, _ => 84.0 };
    public double RingThickness => Config.Size switch { "S" => 6.0, "L" => 10.0, _ => 8.0 };
    public double NumberFont => Config.Size switch { "S" => 22.0, "L" => 40.0, _ => 30.0 };

    /// <summary>Кольцо и полоса имеют смысл только для процентов и температуры; для остальных показываем число.</summary>
    public string EffectiveStyle
    {
        get
        {
            var u = Primary?.Unit;
            bool scalable = u is "%" or "°C";
            return Config.Style is "Ring" or "Bar" && !scalable ? "Number" : Config.Style;
        }
    }

    private void Refresh()
    {
        Primary = _vm.Find(Config.Primary);
        foreach (var n in new[] { nameof(Caption), nameof(CaptionVisible), nameof(Icon), nameof(ShowPrimaryNote),
                                  nameof(RingSize), nameof(RingThickness), nameof(NumberFont), nameof(EffectiveStyle) })
            OnPropertyChanged(n);
    }

    private void RefreshExtras()
    {
        foreach (var r in Extras) r.Detach();
        Extras.Clear();
        string group = MetricLabels.Group(Config.Primary);
        foreach (var id in Config.Extras)
        {
            var card = _vm.Find(id);
            if (card == null) continue;
            // внутри «своей» группы — короткая подпись, иначе — полная (например, «Температура процессора»)
            string label = MetricLabels.Group(id) == group ? MetricLabels.Row(id) : MetricLabels.Caption(id);
            Extras.Add(new ExtraRowViewModel(card, label));
        }
    }

    public void Detach()
    {
        Config.PropertyChanged -= _cfgH;
        Config.Extras.CollectionChanged -= _extrasH;
        _vm.Settings.PropertyChanged -= _setH;
        foreach (var r in Extras) r.Detach();
    }
}
