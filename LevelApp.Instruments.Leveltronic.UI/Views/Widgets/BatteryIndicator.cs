using LevelApp.Instruments.Leveltronic.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace LevelApp.Instruments.Leveltronic.UI.Views.Widgets;

/// <summary>
/// A small drawn battery (port of the phone app's BatteryIndicator): the fill is the state of
/// charge, its colour the urgency (green / amber / red), an up-arrow sits on the cell while
/// charging. <see cref="Percent"/> below 0 draws an empty outline (no data). Built in code, no XAML.
/// </summary>
public sealed class BatteryIndicator : UserControl
{
    public static readonly DependencyProperty PercentProperty = Reg(nameof(Percent), typeof(int), -1);
    public static readonly DependencyProperty ChargingProperty = Reg(nameof(Charging), typeof(bool), false);
    public static readonly DependencyProperty ChargeStateProperty = Reg(nameof(ChargeState), typeof(BatteryState), BatteryState.Normal);
    public static readonly DependencyProperty GlyphHeightProperty = Reg(nameof(GlyphHeight), typeof(double), 18.0);
    public static readonly DependencyProperty ShowLabelProperty = Reg(nameof(ShowLabel), typeof(bool), true);

    private static DependencyProperty Reg(string name, Type type, object def) =>
        DependencyProperty.Register(name, type, typeof(BatteryIndicator),
            new PropertyMetadata(def, (d, _) => ((BatteryIndicator)d).Redraw()));

    private readonly StackPanel _root = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Canvas _canvas = new();
    private readonly TextBlock _label = new() { Foreground = Solid(255, 0xFF, 0xFF, 0xFF), VerticalAlignment = VerticalAlignment.Center };

    public BatteryIndicator()
    {
        _root.Children.Add(_canvas);
        _root.Children.Add(_label);
        Content = _root;
        Redraw();
    }

    /// <summary>State of charge 0..100, or -1 when unknown.</summary>
    public int Percent { get => (int)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
    public BatteryState ChargeState { get => (BatteryState)GetValue(ChargeStateProperty); set => SetValue(ChargeStateProperty, value); }
    /// <summary>Height of the battery glyph in pixels.</summary>
    public double GlyphHeight { get => (double)GetValue(GlyphHeightProperty); set => SetValue(GlyphHeightProperty, value); }
    public bool ShowLabel { get => (bool)GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }

    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));

    private static SolidColorBrush FillBrush(BatteryLevel level) => level switch
    {
        BatteryLevel.Critical => Solid(255, 0xE5, 0x39, 0x35),
        BatteryLevel.Low => Solid(255, 0xFF, 0xA0, 0x00),
        BatteryLevel.Ok => Solid(255, 0x43, 0xA0, 0x47),
        _ => Solid(0x62, 0xFF, 0xFF, 0xFF),
    };

    private void Redraw()
    {
        double h = Math.Max(8, GlyphHeight);
        double w = h * 1.9;
        int? percent = Percent >= 0 ? Math.Min(Percent, 100) : null;
        bool hasData = percent is not null;
        double fraction = (percent ?? 0) / 100.0;
        var white70 = Solid(0xB3, 0xFF, 0xFF, 0xFF);

        _canvas.Children.Clear();
        _canvas.Width = w;
        _canvas.Height = h;

        double capW = w * 0.07;
        double bodyW = w - capW - 1;
        double stroke = h * 0.1;

        // Outline.
        _canvas.Children.Add(Placed(new Rectangle
        {
            Width = bodyW,
            Height = h,
            RadiusX = h * 0.22,
            RadiusY = h * 0.22,
            Stroke = white70,
            StrokeThickness = stroke,
        }, 0, 0));

        // Terminal cap.
        _canvas.Children.Add(Placed(new Rectangle
        {
            Width = capW,
            Height = h * 0.4,
            RadiusX = h * 0.1,
            RadiusY = h * 0.1,
            Fill = white70,
        }, bodyW + 1, h * 0.3));

        // Fill, inset from the outline.
        if (hasData && fraction > 0)
        {
            double inset = h * 0.16;
            double innerW = bodyW - 2 * inset;
            double innerH = h - 2 * inset;
            double fillW = Math.Clamp(innerW * fraction, h * 0.1, innerW);
            _canvas.Children.Add(Placed(new Rectangle
            {
                Width = fillW,
                Height = innerH,
                RadiusX = h * 0.1,
                RadiusY = h * 0.1,
                Fill = FillBrush(LiveFormat.BatteryLevelFor(percent, ChargeState)),
            }, inset, inset));
        }

        // Charging: an up-arrow over the cell, outlined so it reads on any fill.
        if (Charging)
        {
            double cx = bodyW / 2, cy = h / 2;
            double ah = h * 0.62, aw = h * 0.46;
            var arrow = new Polygon
            {
                Fill = Solid(255, 0xFF, 0xFF, 0xFF),
                Stroke = Solid(255, 0x12, 0x12, 0x12),
                StrokeThickness = h * 0.12,
                StrokeLineJoin = PenLineJoin.Round,
            };
            double top = cy - ah / 2;
            arrow.Points.Add(new Point(cx, top));
            arrow.Points.Add(new Point(cx + aw / 2, top + ah * 0.45));
            arrow.Points.Add(new Point(cx + aw * 0.17, top + ah * 0.45));
            arrow.Points.Add(new Point(cx + aw * 0.17, cy + ah / 2));
            arrow.Points.Add(new Point(cx - aw * 0.17, cy + ah / 2));
            arrow.Points.Add(new Point(cx - aw * 0.17, top + ah * 0.45));
            arrow.Points.Add(new Point(cx - aw / 2, top + ah * 0.45));
            _canvas.Children.Add(arrow);
        }

        string text = LiveFormat.Percent(percent);
        _label.Text = text;
        _label.FontSize = h * 0.62 < 8 ? 8 : h * 0.62;
        _label.Visibility = ShowLabel ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(this, Charging ? $"Battery {text}, charging" : $"Battery {text}");
    }

    private static T Placed<T>(T element, double left, double top) where T : UIElement
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return element;
    }
}
