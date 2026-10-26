using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThodMonitor.Models;

namespace ThodMonitor.ViewModels;

/// <summary>Визуальный редактор выдвижной панели: выбор блока, добавление/удаление, порядок, показатели.</summary>
public sealed partial class EdgeEditorViewModel : ObservableObject
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _s;
    private bool _rebuilding;

    public ObservableCollection<MetricOption> PrimaryOptions { get; } = new();
    public ObservableCollection<ExtraOption> ExtraOptions { get; } = new();

    [ObservableProperty] private EdgeBlockViewModel? selected;
    [ObservableProperty] private bool hasSelection;
    [ObservableProperty] private bool noSelection = true;

    public EdgeEditorViewModel(MainViewModel vm, AppSettings s)
    {
        _vm = vm; _s = s;
        foreach (var m in MetricCatalog.All) PrimaryOptions.Add(new MetricOption(m.Id, MetricLabels.Caption(m.Id)));
    }

    partial void OnSelectedChanged(EdgeBlockViewModel? oldValue, EdgeBlockViewModel? newValue)
    {
        if (oldValue != null) { oldValue.IsSelected = false; oldValue.Config.PropertyChanged -= OnCfg; }
        if (newValue != null) { newValue.IsSelected = true; newValue.Config.PropertyChanged += OnCfg; }
        HasSelection = newValue != null;
        NoSelection = newValue == null;
        RebuildExtras();
    }

    private void OnCfg(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EdgeBlock.Primary) || Selected == null) return;
        Selected.Config.Extras.Remove(Selected.Config.Primary);   // основной не дублируем в дополнительных
        RebuildExtras();
    }

    private void RebuildExtras()
    {
        _rebuilding = true;
        ExtraOptions.Clear();
        if (Selected != null)
            foreach (var o in PrimaryOptions.Where(o => o.Id != Selected.Config.Primary))
                ExtraOptions.Add(new ExtraOption(o.Id, o.Label, Selected.Config.Extras.Contains(o.Id), OnExtraToggled));
        _rebuilding = false;
    }

    private void OnExtraToggled(ExtraOption o)
    {
        if (_rebuilding || Selected == null) return;
        var list = Selected.Config.Extras;
        if (o.IsChecked) { if (!list.Contains(o.Id)) list.Add(o.Id); }
        else list.Remove(o.Id);
    }

    public void Select(EdgeBlockViewModel? b) => Selected = b;

    private ObservableCollection<EdgeBlock> Blocks => _s.Edge.Blocks;

    [RelayCommand]
    private void AddBlock(string kind)
    {
        var b = kind switch
        {
            "cpu" => EdgeBlock.Make("cpu.load", "Ring", "cpu.temp", "cpu.power"),
            "gpu" => EdgeBlock.Make("gpu.load", "Ring", "gpu.temp", "gpu.vram"),
            "ram" => EdgeBlock.Make("ram.load", "Ring"),
            "disk" => EdgeBlock.Make("disk.used", "Ring", "disk.activity"),
            "net" => EdgeBlock.Make("net.down", "Number", "net.up"),
            "fps" => EdgeBlock.Make("fps.now", "Number", "fps.low", "fps.ft"),
            _ => EdgeBlock.Make("cpu.load", "Ring"),
        };
        Blocks.Add(b);
        Selected = _vm.EdgeBlocks.FirstOrDefault(x => x.Config == b);
    }

    [RelayCommand]
    private void RemoveBlock(EdgeBlockViewModel? b)
    {
        if (b == null) return;
        int i = Blocks.IndexOf(b.Config);
        if (i < 0) return;
        Selected = null;
        Blocks.RemoveAt(i);
        if (Blocks.Count > 0) Selected = _vm.EdgeBlocks.ElementAtOrDefault(Math.Min(i, Blocks.Count - 1));
    }

    [RelayCommand]
    private void MoveUp(EdgeBlockViewModel? b)
    {
        if (b == null) return;
        int i = Blocks.IndexOf(b.Config);
        if (i > 0) Blocks.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveDown(EdgeBlockViewModel? b)
    {
        if (b == null) return;
        int i = Blocks.IndexOf(b.Config);
        if (i >= 0 && i < Blocks.Count - 1) Blocks.Move(i, i + 1);
    }

    [RelayCommand]
    private void ResetBlocks()
    {
        Selected = null;
        Blocks.Clear();
        foreach (var b in EdgeBlock.Defaults()) Blocks.Add(b);
    }

    /// <summary>Перетаскивание: блок source встаёт на место блока target.</summary>
    public void MoveTo(EdgeBlockViewModel source, EdgeBlockViewModel target)
    {
        int a = Blocks.IndexOf(source.Config), b = Blocks.IndexOf(target.Config);
        if (a >= 0 && b >= 0 && a != b) Blocks.Move(a, b);
    }
}
