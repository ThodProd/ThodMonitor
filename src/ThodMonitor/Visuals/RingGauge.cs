using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ThodMonitor.Visuals;

/// <summary>
/// Круговой индикатор: дорожка, дуга с закруглёнными концами, мягкое свечение, иконка и значение в центре.
/// Значение и цвет меняются плавно; цвет считается по анимируемому значению (зелёный → жёлтый → красный).
/// </summary>
public sealed class RingGauge : FrameworkElement
{
    private static FrameworkPropertyMetadata R(object def, PropertyChangedCallback? cb = null) =>
        new(def, FrameworkPropertyMetadataOptions.AffectsRender, cb);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, OnValueChanged));
    public static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(nameof(DisplayValue), typeof(double), typeof(RingGauge), R(0.0));
    public static readonly DependencyProperty WarnProperty = DependencyProperty.Register(nameof(Warn), typeof(double), typeof(RingGauge), R(60.0));
    public static readonly DependencyProperty CritProperty = DependencyProperty.Register(nameof(Crit), typeof(double), typeof(RingGauge), R(85.0));
    public static readonly DependencyProperty IsAvailableProperty = DependencyProperty.Register(nameof(IsAvailable), typeof(bool), typeof(RingGauge), R(true));
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(string), typeof(RingGauge), R("Traffic"));
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(RingGauge), R(Color.FromRgb(0x4F, 0x8C, 0xFF)));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(RingGauge), R(""));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(RingGauge), R(""));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(RingGauge), R(8.0));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(RingGauge),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(RingGauge), R(FontWeights.SemiBold));
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double DisplayValue { get => (double)GetValue(DisplayValueProperty); set => SetValue(DisplayValueProperty, value); }
    public double Warn { get => (double)GetValue(WarnProperty); set => SetValue(WarnProperty, value); }
    public double Crit { get => (double)GetValue(CritProperty); set => SetValue(CritProperty, value); }
    public bool IsAvailable { get => (bool)GetValue(IsAvailableProperty); set => SetValue(IsAvailableProperty, value); }
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (RingGauge)d;
        var to = Math.Clamp((double)e.NewValue, 0, 100);
        g.BeginAnimation(DisplayValueProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(380))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private Color StateColor()
    {
        if (!IsAvailable) return ColorScale.Dim;
        return Mode == "Accent" ? AccentColor : ColorScale.HighIsBad(DisplayValue, Warn, Crit);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double d = Math.Min(w, h), t = Thickness, r = d / 2 - t / 2 - 3;
        if (r <= 2) return;
        var c = new Point(w / 2, h / 2);
        var col = StateColor();

        // дорожка
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), t), c, r, r);

        if (IsAvailable && DisplayValue > 0.3)
        {
            double sweep = Math.Min(DisplayValue, 100) / 100.0 * 359.9;
            var geo = Arc(c, r, -90, sweep);
            var glow = new Pen(new SolidColorBrush(Color.FromArgb(60, col.R, col.G, col.B)), t + 7)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var pen = new Pen(new SolidColorBrush(col), t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, glow, geo);
            dc.DrawGeometry(null, pen, geo);
        }

        bool hasIcon = !string.IsNullOrEmpty(Icon) && d >= 60;
        var textBrush = IsAvailable ? new SolidColorBrush(ColorScale.Neutral) : new SolidColorBrush(ColorScale.Dim);
        if (hasIcon)
        {
            double s = d * 0.27;
            Icons.Draw(dc, Icon, new Rect(c.X - s / 2, c.Y - d * 0.29, s, s), new SolidColorBrush(col), 1.8);
        }
        if (!string.IsNullOrEmpty(Text))
        {
            double fs = hasIcon ? d * 0.22 : d * 0.28;
            var ft = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
                fs, textBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double cy = hasIcon ? c.Y + d * 0.13 : c.Y;
            dc.DrawText(ft, new Point(c.X - ft.Width / 2, cy - ft.Height / 2));
        }
    }

    private static StreamGeometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var p0 = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
        var p1 = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
