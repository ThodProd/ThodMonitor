using System.Windows.Controls;

namespace ThodMonitor.Views;

public partial class AlertCard : UserControl
{
    public AlertCard() => InitializeComponent();
    public Border Root => RootBorder;
    public StackPanel TextArea => TextPart;
}
