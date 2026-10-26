using System.Collections.ObjectModel;
using ThodMonitor.Models;

namespace ThodMonitor.ViewModels;

/// <summary>Строки дополнительных показателей под оверлеем; перестраиваются при изменении списка в настройках.</summary>
public sealed class ExtraRowsViewModel
{
    public ObservableCollection<ExtraRowViewModel> Rows { get; } = new();

    public ExtraRowsViewModel(ObservableCollection<string> ids, MainViewModel vm, string group)
    {
        void Rebuild()
        {
            foreach (var r in Rows) r.Detach();
            Rows.Clear();
            foreach (var id in ids)
            {
                var card = vm.Find(id);
                if (card == null) continue;
                string label = group != "" && MetricLabels.Group(id) == group ? MetricLabels.Row(id) : MetricLabels.Caption(id);
                Rows.Add(new ExtraRowViewModel(card, label));
            }
        }
        ids.CollectionChanged += (_, _) => Rebuild();
        Rebuild();
    }
}
