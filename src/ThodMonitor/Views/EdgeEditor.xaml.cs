using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ThodMonitor.Models;
using ThodMonitor.ViewModels;
using ThodMonitor.Visuals;

namespace ThodMonitor.Views;

/// <summary>Визуальный редактор выдвижной панели: выбор блока кликом, перестановка перетаскиванием.</summary>
public partial class EdgeEditor : UserControl
{
    private MainViewModel? _vm;
    private Point _start;
    private EdgeBlockViewModel? _dragItem;

    public EdgeEditor()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.Settings.Edge.PropertyChanged -= OnEdgeChanged;
        _vm = e.NewValue as MainViewModel;
        if (_vm != null) { _vm.Settings.Edge.PropertyChanged += OnEdgeChanged; ApplyOrientation(); ApplyFont(); }
    }

    private void OnEdgeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EdgeConfig.Edge)) ApplyOrientation();
        if (e.PropertyName is nameof(EdgeConfig.Font) or nameof(EdgeConfig.Weight)) ApplyFont();
    }

    private void ApplyOrientation()
    {
        bool horizontal = _vm!.Settings.Edge.Edge is "Top" or "Bottom";
        var f = new FrameworkElementFactory(typeof(StackPanel));
        f.SetValue(StackPanel.OrientationProperty, horizontal ? Orientation.Horizontal : Orientation.Vertical);
        Preview.ItemsPanel = new ItemsPanelTemplate(f);
    }

    private void ApplyFont()
    {
        Preview.Resources["ValueFont"] = WidgetFonts.Family(_vm!.Settings.Edge.Font);
        Preview.Resources["ValueWeight"] = WidgetFonts.Weight(_vm.Settings.Edge.Weight);
    }

    private EdgeBlockViewModel? BlockAt(object source)
    {
        if (source is not DependencyObject d) return null;
        return (ItemsControl.ContainerFromElement(Preview, d) as FrameworkElement)?.DataContext as EdgeBlockViewModel;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(null);
        _dragItem = BlockAt(e.OriginalSource);
        if (_dragItem != null && _vm != null) _vm.EdgeEditor.Select(_dragItem);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
        var d = e.GetPosition(null) - _start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _dragItem; _dragItem = null;
        DragDrop.DoDragDrop(Preview, new DataObject("thodblock", item), DragDropEffects.Move);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("thodblock") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm == null || e.Data.GetData("thodblock") is not EdgeBlockViewModel src) return;
        var target = BlockAt(e.OriginalSource);
        if (target != null && target != src) _vm.EdgeEditor.MoveTo(src, target);
        e.Handled = true;
    }
}
