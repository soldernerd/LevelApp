using LevelApp.Instruments.Leveltronic.UI.Calibration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace LevelApp.Instruments.Leveltronic.UI.Views.Widgets;

/// <summary>
/// A schematic of the instrument on a plate, animated between poses (port of the phone app's
/// <c>InstrumentDiagram</c>): it shows what to do in a calibration step — hold still, turn the
/// instrument 180°, put a shim under one end. A pose change is eased over 700 ms: the instrument
/// turns (mirrors) and the plate tilts around its pivot. Drawn in code with shapes, no XAML.
/// </summary>
public sealed class InstrumentDiagram : UserControl
{
    private const double AnimationMs = 700;
    private const double ShimTilt = 0.09; // radians, as in the phone app

    private readonly Canvas _canvas = new() { Height = 150 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private DiagramPose _pose = DiagramPose.Level;
    private string? _label;

    // current (animated) values and the segment being animated
    private double _rotation, _tilt;
    private double _fromRotation, _fromTilt, _toRotation, _toTilt;
    private DateTime _animStart;

    public InstrumentDiagram()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = _canvas;
        _canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
        _canvas.SizeChanged += (_, _) => Redraw();
        _timer.Tick += OnTick;
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// Shows <paramref name="pose"/>. The first call sets the picture at once; later ones animate from
    /// what is shown now. <paramref name="label"/> is a short caption under the shim ("0.100 mm").
    /// </summary>
    public void Show(DiagramPose pose, string? label)
    {
        bool first = _animStart == default;

        // The wizard rebuilds its page on every instrument update: the same pose again changes nothing
        // (restarting the animation each time would make it crawl).
        if (!first && pose == _pose && label == _label) return;

        _label = label;
        _pose = pose;

        (_toRotation, _toTilt) = Target(pose);
        if (first)
        {
            (_rotation, _tilt) = (_toRotation, _toTilt);
            _animStart = DateTime.UtcNow;
            Redraw();
            return;
        }

        _fromRotation = _rotation;
        _fromTilt     = _tilt;
        _animStart    = DateTime.UtcNow;
        _timer.Start();
        Redraw();
    }

    private static (double Rotation, double Tilt) Target(DiagramPose pose) => pose switch
    {
        DiagramPose.Rotated   => (Math.PI, 0),
        DiagramPose.Shim      => (0, -ShimTilt),
        DiagramPose.ShimFront => (0, ShimTilt),
        _                     => (0, 0),
    };

    private void OnTick(object? sender, object e)
    {
        double t = Math.Clamp((DateTime.UtcNow - _animStart).TotalMilliseconds / AnimationMs, 0, 1);
        double eased = t * t * (3 - 2 * t); // ease in and out
        _rotation = _fromRotation + (_toRotation - _fromRotation) * eased;
        _tilt     = _fromTilt + (_toTilt - _fromTilt) * eased;
        if (t >= 1) _timer.Stop();
        Redraw();
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    private static SolidColorBrush Fill(Color c) => new(c);

    private void Redraw()
    {
        _canvas.Children.Clear();
        double w = _canvas.ActualWidth, h = _canvas.Height;
        if (w < 20) return;

        bool raiseFront = _pose == DiagramPose.ShimFront;
        bool showShim = _pose is DiagramPose.Shim or DiagramPose.ShimFront;

        double cx = w / 2;
        double baseY = h * 0.78;
        double plateW = Math.Min(w * 0.86, 360.0);

        // table line
        _canvas.Children.Add(new Line
        {
            X1 = cx - plateW / 2 - 12, Y1 = baseY + 22, X2 = cx + plateW / 2 + 12, Y2 = baseY + 22,
            Stroke = Fill(C(0x3D, 0xFF, 0xFF, 0xFF)), StrokeThickness = 2,
        });

        // shim block under one end of the plate
        if (showShim)
        {
            double shimX = raiseFront ? cx - plateW / 2 + 8 : cx + plateW / 2 - 34;
            var shim = new Rectangle
            {
                Width = 26, Height = 14, RadiusX = 2, RadiusY = 2, Fill = Fill(C(255, 0xFF, 0xB3, 0x00)),
            };
            Canvas.SetLeft(shim, shimX);
            Canvas.SetTop(shim, baseY + 8);
            _canvas.Children.Add(shim);

            if (!string.IsNullOrEmpty(_label))
            {
                var text = new TextBlock { Text = _label, FontSize = 12, Foreground = Fill(C(255, 0xFF, 0xB3, 0x00)) };
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(text, shimX + 13 - text.DesiredSize.Width / 2);
                Canvas.SetTop(text, baseY + 26);
                _canvas.Children.Add(text);
            }
        }

        // the plate and the instrument on it share one frame, pivoting on the plate's raised-end-opposite end
        var plateFrame = new Canvas
        {
            RenderTransform = new CompositeTransform
            {
                TranslateX = raiseFront ? cx + plateW / 2 : cx - plateW / 2,
                TranslateY = baseY + 8,
                Rotation = _tilt * 180.0 / Math.PI,
            },
        };

        double offset = raiseFront ? -plateW : 0;
        var plate = new Rectangle
        {
            Width = plateW, Height = 8, RadiusX = 2, RadiusY = 2, Fill = Fill(C(255, 0x54, 0x6E, 0x7A)),
        };
        Canvas.SetLeft(plate, offset);
        Canvas.SetTop(plate, -8);
        plateFrame.Children.Add(plate);

        // the instrument; a 180° turn seen from the side mirrors it left to right
        double bodyW = plateW * 0.62;
        const double bodyH = 34;
        var body = new Canvas
        {
            RenderTransform = new CompositeTransform
            {
                TranslateX = offset + plateW / 2,
                TranslateY = -8,
                ScaleX = Math.Cos(_rotation),
            },
        };

        var housing = new Rectangle
        {
            Width = bodyW, Height = bodyH, RadiusX = 6, RadiusY = 6,
            Fill = Fill(C(255, 0x26, 0x32, 0x38)),
            Stroke = Fill(C(255, 0x90, 0xCA, 0xF9)), StrokeThickness = 2,
        };
        Canvas.SetLeft(housing, -bodyW / 2);
        Canvas.SetTop(housing, -bodyH);
        body.Children.Add(housing);

        foreach (double dx in new[] { -bodyW * 0.3, bodyW * 0.3 })
        {
            var head = new Ellipse { Width = 14, Height = 14, Fill = Fill(C(255, 0x64, 0xB5, 0xF6)) };
            Canvas.SetLeft(head, dx - 7);
            Canvas.SetTop(head, -bodyH / 2 - 7);
            body.Children.Add(head);
        }

        // the front mark: a red triangle on the left end
        body.Children.Add(new Polygon
        {
            Points = [new Point(-bodyW / 2 - 10, -bodyH / 2), new Point(-bodyW / 2, -bodyH / 2 - 8), new Point(-bodyW / 2, -bodyH / 2 + 8)],
            Fill = Fill(C(255, 0xEF, 0x53, 0x50)),
        });

        plateFrame.Children.Add(body);
        _canvas.Children.Add(plateFrame);

        // the "turn 180°" arrow above
        if (_pose == DiagramPose.Rotated)
            AddTurnArrow(cx, h);
    }

    private void AddTurnArrow(double cx, double h)
    {
        double r = h * 0.22;
        double cy = h * 0.28;
        var color = Fill(C(255, 0x90, 0xCA, 0xF9));

        Point At(double a) => new(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        double start = Math.PI * 1.05, end = Math.PI * 1.95;

        var figure = new PathFigure { StartPoint = At(start), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = At(end), Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise, IsLargeArc = false,
        });
        _canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = new PathGeometry { Figures = { figure } },
            Stroke = color, StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });

        var tip = At(end);
        _canvas.Children.Add(new Polygon
        {
            Points = [tip, new Point(tip.X - 10, tip.Y - 4), new Point(tip.X - 3, tip.Y + 9)],
            Fill = color,
        });
    }
}
