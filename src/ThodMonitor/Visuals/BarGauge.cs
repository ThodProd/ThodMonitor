using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ThodMonitor.Visuals;

/// <summary>Тонкая закруглённая полоса (например, VRAM) с тем же светофором цветов.</summary>
public sealed class BarGauge : FrameworkElement
{
    private static FrameworkPropertyMetadata R(object def, PropertyChangedCallback? cb = null) =>
        new(def, FrameworkPropertyMetadataOptions.AffectsRender, cb);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(BarGauge),
        new FrameworkPropertyMetadata(0.0, OnValueChanged));
    public static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(nameof(DisplayValue), typeof(double), typeof(BarGauge), R(0.0));
    public static readonly DependencyProperty WarnProperty = DependencyProperty.Register(nameof(Warn), typeof(double), typeof(BarGauge), R(60.0));
    public static readonly DependencyProperty CritProperty = DependencyProperty.Register(nameof(Crit), typeof(double), typeof(BarGauge), R(85.0));
    public static readonly DependencyProperty IsAvailableProperty = DependencyProperty.Register(nameof(IsAvailable), typeof(bool), typeof(BarGauge), R(true));
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(string), typeof(BarGauge), R("Traffic"));
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(BarGauge), R(Color.FromRgb(0x4F, 0x8C, 0xFF)));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double DisplayValue { get => (double)GetValue(DisplayValueProperty); set => SetValue(DisplayValueProperty, value); }
    public double Warn { get => (double)GetValue(WarnProperty); set => SetValue(WarnProperty, value); }
    public double Crit { get => (double)GetValue(CritProperty); set => SetValue(CritProperty, value); }
    public bool IsAvailable { get => (bool)GetValue(IsAvailableProperty); set => SetValue(IsAvailableProperty, value); }
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BarGauge)d).BeginAnimation(DisplayValueProperty, new DoubleAnimation(Math.Clamp((double)e.NewValue, 0, 100), TimeSpan.FromMilliseconds(380))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (!IsAvailable || DisplayValue <= 0.3) return;
        var col = Mode == "Accent" ? AccentColor : ColorScale.HighIsBad(DisplayValue, Warn, Crit);
        double fw = Math.Max(h, w * Math.Min(DisplayValue, 100) / 100.0);
        dc.DrawRoundedRectangle(new SolidColorBrush(col), null, new Rect(0, 0, fw, h), h / 2, h / 2);
    }
}
