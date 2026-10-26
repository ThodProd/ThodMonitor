using System.Windows;
using System.Windows.Controls;

namespace ThodMonitor.Views;

public partial class EdgeContent : UserControl
{
    public EdgeContent() => InitializeComponent();

    /// <summary>Вертикально для левого/правого края, горизонтально для верхнего/нижнего.</summary>
    public void SetOrientation(Orientation o)
    {
        var f = new FrameworkElementFactory(typeof(StackPanel));
        f.SetValue(StackPanel.OrientationProperty, o);
        Items.ItemsPanel = new ItemsPanelTemplate(f);
    }
}
