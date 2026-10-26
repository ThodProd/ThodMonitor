using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ThodMonitor.ViewModels;
using ThodMonitor.Visuals;

namespace ThodMonitor.Views;

/// <summary>Одна карточка уведомления. Варианты появления: Slide, Fade, Scale, Expand, Glow, Pulse, Minimal.</summary>
public sealed class AlertWindow : OverlayWindowBase
{
    private const double Pad = 26;   // запас под свечение
    private readonly AlertCard _card;
    private readonly Grid _host;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DropShadowEffect _glow;

    public AlertWindow(AlertViewModel vm, Color accent)
    {
        _card = new AlertCard { DataContext = vm, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _scale };
        _glow = new DropShadowEffect { Color = accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.5 };
        _card.Effect = _glow;
        _host = new Grid { Margin = new Thickness(Pad) };
        _host.Children.Add(_card);
        Content = _host;
        Title = "Thod Monitor · уведомление";
        Opacity = 0;
        SizeChanged += (_, _) => SizeSettled?.Invoke(this);
    }

    public event Action<AlertWindow>? SizeSettled;

    public Size MeasureSize()
    {
        _host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _host.DesiredSize;
    }

    public Size CurrentSize() => IsLoaded ? new Size(ActualWidth, ActualHeight) : MeasureSize();

    private void Fade(double ms, double to = 1) =>
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(ms)));

    public void PlayIn(string style, Rect workArea, string corner)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var pop = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        switch (style)
        {
            case "Fade":
                Fade(650);
                break;
            case "Scale":
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = pop });
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = pop });
                Fade(260);
                break;
            case "Expand":
                // сначала появляется кольцо с иконкой, затем карточка раскрывается вправо
                Fade(160);
                _card.TextArea.Opacity = 0;
                _card.Root.BeginAnimation(WidthProperty, new DoubleAnimation(124, 340, TimeSpan.FromMilliseconds(460)) { BeginTime = TimeSpan.FromMilliseconds(140), EasingFunction = ease });
                _card.TextArea.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(380) });
                break;
            case "Glow":
                Fade(320);
                _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(10, 38, TimeSpan.FromMilliseconds(650))
                { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = new SineEase() });
                _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.35, 0.95, TimeSpan.FromMilliseconds(650))
                { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                break;
            case "Pulse":
                Fade(300);
                DoubleAnimation Pulse() => new(1, 1.05, TimeSpan.FromMilliseconds(320))
                { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new SineEase(), BeginTime = TimeSpan.FromMilliseconds(250) };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Pulse());
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse());
                break;
            default: // Slide, Minimal
            {
                double to = Left;
                bool right = corner.EndsWith("Right");
                double from = right ? workArea.Right + 10 : workArea.Left - CurrentSize().Width - 10;
                BeginAnimation(LeftProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
                Fade(240);
                break;
            }
        }
    }

    public void CloseAnimated(Action onClosed)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280));
        fade.Completed += (_, _) => { try { Close(); } catch { } onClosed(); };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(280)));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(280)));
        BeginAnimation(OpacityProperty, fade);
    }

    public static double StackPad => Pad;
}
