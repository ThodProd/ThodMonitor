using ThodMonitor.Models;
using ThodMonitor.ViewModels;

namespace ThodMonitor.Services;

/// <summary>
/// Правила уведомлений: порог + длительность превышения + гистерезис + пауза между повторами.
/// Кратковременные скачки игнорируются. Работает пока идут измерения (в т.ч. при закрытом главном окне).
/// </summary>
public sealed class AlertEngine
{
    private sealed class State { public DateTime? Since; public bool Active; public DateTime LastFired = DateTime.MinValue; }
    private const double Hysteresis = 3;           // условие восстановления: порог − 3
    private const double MinPauseSeconds = 60;     // не чаще раза в минуту для одного правила

    private readonly AppSettings _s;
    private readonly MainViewModel _vm;
    private readonly Dictionary<string, State> _state = new();

    public event Action<AlertRule, double, TimeSpan>? Fired;

    public AlertEngine(AppSettings s, MainViewModel vm)
    {
        _s = s; _vm = vm;
        vm.SamplesApplied += Evaluate;
    }

    private void Evaluate()
    {
        if (!_s.Alerts.Enabled) return;
        var now = DateTime.UtcNow;
        foreach (var r in _s.Alerts.Rules)
        {
            if (!_state.TryGetValue(r.Id, out var st)) _state[r.Id] = st = new State();
            var card = _vm.Find(r.Metric);
            if (!r.Enabled || card == null || !card.IsAvailable) { st.Since = null; st.Active = false; continue; }

            double v = card.NumericValue;
            if (v >= r.Threshold)
            {
                st.Since ??= now;
                var held = now - st.Since.Value;
                if (held.TotalSeconds < r.DurationSec) continue;
                bool first = !st.Active;
                bool repeat = st.Active && (now - st.LastFired).TotalMinutes >= r.RepeatMin;
                if ((first || repeat) && (now - st.LastFired).TotalSeconds >= MinPauseSeconds)
                {
                    st.Active = true; st.LastFired = now;
                    Fired?.Invoke(r, v, held);
                }
            }
            else if (v < r.Threshold - Hysteresis)
            {
                st.Since = null; st.Active = false;
            }
        }
    }
}
