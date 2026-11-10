using System.ComponentModel;
using LevelApp.Instruments.Leveltronic.Diagnostics;
using LevelApp.Instruments.Leveltronic.Protocol;
using LevelApp.Instruments.Leveltronic.UI.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace LevelApp.Instruments.Leveltronic.UI.Views;

/// <summary>
/// Oscilloscope-style picture of the four channels (A, B on the upper screen, S1, S2 on the
/// lower one), a port of the phone app's scope screen. Pure drawing: the numbers come from
/// <see cref="ScopeViewModel"/> and the trace maths from <see cref="ScopeMath"/>.
/// </summary>
public sealed partial class ScopeView : UserControl
{
    private readonly record struct TraceSpec(string Name, double PeakMv, double PhaseDeg);

    private static readonly Color ColorA = Rgb(0xFF, 0xC1, 0x07);
    private static readonly Color ColorB = Rgb(0x29, 0xB6, 0xF6);
    private static readonly Color ColorS1 = Rgb(0x66, 0xBB, 0x6A);
    private static readonly Color ColorS2 = Rgb(0xEC, 0x40, 0x7A);

    public ScopeViewModel ViewModel { get; }

    public ScopeView(ScopeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ScaleBox.ItemsSource = ViewModel.ScaleItems;
        ScaleBox.SelectedIndex = ViewModel.ScaleIndex;
        ScaleBox.SelectionChanged += OnScaleChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
        ViewModel.PropertyChanged += OnViewModelChanged;
        if (ScaleBox.SelectedIndex != ViewModel.ScaleIndex) ScaleBox.SelectedIndex = ViewModel.ScaleIndex;
        Redraw();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        ViewModel.PropertyChanged -= OnViewModelChanged;

    private void OnScaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScaleBox.SelectedIndex >= 0) ViewModel.ScaleIndex = ScaleBox.SelectedIndex;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(ScopeViewModel.Latest)
            || e.PropertyName == nameof(ScopeViewModel.ScaleIndex)
            || e.PropertyName == nameof(ScopeViewModel.SensorFullScaleMv))
            Redraw();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    // ── Drawing ──────────────────────────────────────────────────────────────

    private void Redraw()
    {
        var excitation = new List<TraceSpec>();
        var sensors = new List<TraceSpec>();
        if (ViewModel.Latest is { } d)
        {
            excitation.Add(Spec(d, "A"));
            excitation.Add(Spec(d, "B"));
            sensors.Add(Spec(d, "S1"));
            sensors.Add(Spec(d, "S2"));
        }

        DrawPanel(ExcitationCanvas, ExcitationLegend, excitation, ScopeMath.FullScaleMv, shiftS2: false);
        DrawPanel(SensorCanvas, SensorLegend, sensors, ViewModel.SensorFullScaleMv, shiftS2: true);
    }

    private static TraceSpec Spec(SignalDiag d, string name)
    {
        int i = Array.IndexOf(SignalDiag.ChannelNames, name);
        return new TraceSpec(name, d.P2pMv[i] / 2, d.PhaseDeg[i]);
    }

    private static Color TraceColor(string name) => name switch
    {
        "A" => ColorA,
        "B" => ColorB,
        "S1" => ColorS1,
        _ => ColorS2,
    };

    private static void DrawPanel(Canvas canvas, StackPanel legend, IReadOnlyList<TraceSpec> traces,
        double fullScaleMv, bool shiftS2)
    {
        BuildLegend(legend, traces, fullScaleMv);

        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 2 || h < 2) return;
        canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, w, h) };

        var grid = new SolidColorBrush(Rgb(0x1F, 0x3A, 0x26));
        for (int i = 0; i <= ScopeMath.GridCols; i++)
        {
            double x = w * i / ScopeMath.GridCols;
            AddLine(canvas, x, 0, x, h, grid, 1);
        }
        for (int j = 0; j <= ScopeMath.GridRows; j++)
        {
            double y = h * j / ScopeMath.GridRows;
            AddLine(canvas, 0, y, w, y, grid, 1);
        }

        double mid = h / 2;
        double amp = h / 2;
        AddLine(canvas, 0, mid, w, mid, new SolidColorBrush(Rgb(0x2F, 0x5A, 0x3A)), 1.5);

        // Voltage labels on the horizontal grid lines. With two sensors, S1 has the left
        // scale and S2 the right one, which is shifted together with its trace.
        if (shiftS2)
        {
            AddLabels(canvas, w, h, fullScaleMv, right: false, WithAlpha(ColorS1, 0xCC), 0);
            AddLabels(canvas, w, h, fullScaleMv, right: true, WithAlpha(ColorS2, 0xCC), ScopeMath.S2Shift * amp);
        }
        else
        {
            AddLabels(canvas, w, h, fullScaleMv, right: false, Rgb(0x4F, 0x8A, 0x5C), 0);
        }

        foreach (var t in traces)
        {
            double dy = shiftS2 && t.Name == "S2" ? ScopeMath.S2Shift * amp : 0;
            double[] v = ScopeMath.Trace(t.PeakMv, t.PhaseDeg, fullScaleMv);
            var points = new PointCollection();
            for (int k = 0; k < v.Length; k++)
                points.Add(new Point(w * k / ScopeMath.Steps, mid - v[k] * amp + dy));
            canvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush(TraceColor(t.Name)),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
            });
        }
    }

    private static void BuildLegend(StackPanel legend, IReadOnlyList<TraceSpec> traces, double fullScaleMv)
    {
        legend.Children.Clear();
        foreach (var t in traces)
        {
            var color = TraceColor(t.Name);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            item.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(color),
            });
            item.Children.Add(new TextBlock
            {
                Text = ScopeMath.LegendText(t.Name, t.PeakMv),
                Foreground = new SolidColorBrush(color),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
            });
            legend.Children.Add(item);
        }
        legend.Children.Add(new TextBlock
        {
            Text = ScopeMath.FullScaleText(fullScaleMv),
            Foreground = new SolidColorBrush(Rgb(0x9E, 0x9E, 0x9E)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private static void AddLabels(Canvas canvas, double w, double h, double fullScaleMv,
        bool right, Color color, double dy)
    {
        var brush = new SolidColorBrush(color);
        for (int j = 1; j < ScopeMath.GridRows; j++)
        {
            var tb = new TextBlock
            {
                Text = ScopeMath.GridLabel(ScopeMath.GridLineMv(fullScaleMv, j)),
                FontSize = 10,
                Foreground = brush,
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = right ? w - tb.DesiredSize.Width - 3 : 3;
            double y = h * j / ScopeMath.GridRows - tb.DesiredSize.Height - 1 + dy;
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2,
        Brush stroke, double thickness) =>
        canvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness,
        });

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
