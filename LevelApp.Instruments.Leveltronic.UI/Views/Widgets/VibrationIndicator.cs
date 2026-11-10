using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace LevelApp.Instruments.Leveltronic.UI.Views.Widgets;

/// <summary>
/// Shows whether the last 0.6 s window of a reading looked disturbed (port of the phone app's
/// VibrationIndicator): amber and labelled when disturbed, a quiet green when calm, dim when there
/// is no reading. The glyph is a vibrating handset drawn from shapes. Built in code, no XAML.
/// </summary>
public sealed class VibrationIndicator : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ReadingState), typeof(VibrationIndicator),
        new PropertyMetadata(ReadingState.None, (d, _) => ((VibrationIndicator)d).Redraw()));

    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(
        nameof(GlyphSize), typeof(double), typeof(VibrationIndicator),
        new PropertyMetadata(26.0, (d, _) => ((VibrationIndicator)d).Redraw()));

    private readonly StackPanel _root = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _canvas = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _label = new() { Text = "disturbed", HorizontalAlignment = HorizontalAlignment.Center };

    public VibrationIndicator()
    {
        _root.Children.Add(_canvas);
        _root.Children.Add(_label);
        Content = _root;
        Redraw();
    }

    public ReadingState State { get => (ReadingState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>Edge length of the glyph in pixels.</summary>
    public double GlyphSize { get => (double)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }

    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));

    private void Redraw()
    {
        double s = Math.Max(10, GlyphSize);
        SolidColorBrush color;
        string tip;
        switch (State)
        {
            case ReadingState.Doubtful:
                color = Solid(255, 0xFF, 0xA0, 0x00);
                tip = "Disturbed — do not trust this value";
                break;
            case ReadingState.Ok:
                color = Solid(255, 0x66, 0xBB, 0x6A);
                tip = "Calm";
                break;
            default:
                color = Solid(0x3D, 0xFF, 0xFF, 0xFF);
                tip = "No reading";
                break;
        }

        _canvas.Children.Clear();
        _canvas.Width = s;
        _canvas.Height = s;
        double stroke = Math.Max(1.5, s * 0.08);

        // The handset.
        double bw = s * 0.34, bh = s * 0.64;
        var body = new Rectangle
        {
            Width = bw,
            Height = bh,
            RadiusX = s * 0.07,
            RadiusY = s * 0.07,
            Stroke = color,
            StrokeThickness = stroke,
        };
        Canvas.SetLeft(body, (s - bw) / 2);
        Canvas.SetTop(body, (s - bh) / 2);
        _canvas.Children.Add(body);

        // Two vibration marks on each side.
        double cy = s / 2;
        foreach (double d in new[] { 0.0, 1.0 })
        {
            double dx = s * 0.14 * d;
            double half = s * (0.17 + 0.05 * d);
            double leftX = s * 0.27 - dx;
            double rightX = s * 0.73 + dx;
            _canvas.Children.Add(Chevron(leftX, cy, half, s * 0.07, color, stroke, toward: +1));
            _canvas.Children.Add(Chevron(rightX, cy, half, s * 0.07, color, stroke, toward: -1));
        }

        _label.Foreground = color;
        _label.FontSize = Math.Max(9, s * 0.4);
        _label.Visibility = State == ReadingState.Doubtful ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(this, tip);
    }

    /// <summary>A "(" or ")" bracket: its tip points away from the handset (<paramref name="toward"/> = +1 opens to the right).</summary>
    private static Polyline Chevron(double x, double cy, double half, double depth, Brush brush, double stroke, int toward)
    {
        var line = new Polyline
        {
            Stroke = brush,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        line.Points.Add(new Point(x + toward * depth, cy - half));
        line.Points.Add(new Point(x, cy));
        line.Points.Add(new Point(x + toward * depth, cy + half));
        return line;
    }
}
