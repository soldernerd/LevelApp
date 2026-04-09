using System.ComponentModel;
using LevelApp.App.Helpers;
using LevelApp.App.Navigation;
using LevelApp.App.ViewModels;
using LevelApp.Core.Geometry.ParallelWays.Strategies;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;
using Orientation = LevelApp.Core.Models.Orientation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace LevelApp.App.Views;

public sealed partial class MeasurementView : Page
{
    // ── Constants for canvas rendering ────────────────────────────────────────
    private const double NodeRadius     = 5.0;
    private const double HighlightR     = 7.0;
    private const double TargetMaxPx    = 360.0;
    private const double MinSpacingPx   = 24.0;
    private const double CanvasPad      = 10.0;
    private const double ArrowLen       = 10.0;
    private const double ArrowHalfW     =  4.5;

    public MeasurementViewModel ViewModel { get; }

    public MeasurementView()
    {
        ViewModel = App.Services.GetRequiredService<MeasurementViewModel>();
        this.InitializeComponent();
    }

    // ── Navigation lifecycle ──────────────────────────────────────────────────

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is MeasurementArgs args)
        {
            ViewModel.Initialize(args);
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            this.ActualThemeChanged   += OnActualThemeChanged;
            DrawGridMap();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        this.ActualThemeChanged   -= OnActualThemeChanged;
    }

    // ── Canvas refresh ────────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(MeasurementViewModel.CurrentStepIndex) or
            nameof(MeasurementViewModel.CurrentStep))
        {
            DispatcherQueue.TryEnqueue(DrawGridMap);
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
        => DrawGridMap();

    /// <summary>
    /// Redraws the grid map canvas with nodes and edges.
    /// Edge colour states: pending = grey | current step = accent | completed = green.
    /// Colours are resolved from the ThemeColors resource dictionary at draw time.
    /// </summary>
    private void DrawGridMap()
    {
        GridCanvas.Children.Clear();

        var currentStep = ViewModel.CurrentStep;
        if (currentStep is null) return;

        // Resolve theme colours at draw time
        Color activeColor    = ThemeHelper.GetColor(GridCanvas, "GridCurrentStepBrush");
        Color completedColor = ThemeHelper.GetColor(GridCanvas, "GridCompletedStepBrush");
        Color pendingColor   = ThemeHelper.GetColor(GridCanvas, "GridPendingStepBrush");

        int    cols      = ViewModel.GridColumns;
        int    rows      = ViewModel.GridRows;
        int    curIdx    = ViewModel.CurrentStepIndex;
        var    steps     = ViewModel.Steps;
        double widthMm   = ViewModel.WidthMm;
        double heightMm  = ViewModel.HeightMm;

        if (ViewModel.IsParallelWays)
        {
            DrawParallelWaysMap(currentStep, curIdx, steps, activeColor, completedColor, pendingColor);
            return;
        }

        if (cols <= 0 || rows <= 0)
        {
            DrawUnionJackMap(currentStep, curIdx, steps, widthMm, heightMm,
                             activeColor, completedColor, pendingColor);
            return;
        }

        double scale    = TargetMaxPx / Math.Max(widthMm, heightMm);
        double xSpacing = Math.Max(MinSpacingPx, widthMm  / (cols - 1) * scale);
        double ySpacing = Math.Max(MinSpacingPx, heightMm / (rows - 1) * scale);

        (double x, double y) NodePos(int c, int r) =>
            (c * xSpacing + CanvasPad + HighlightR,
             r * ySpacing + CanvasPad + HighlightR);

        (int toCol, int toRow) = currentStep.Orientation switch
        {
            Orientation.East  => (currentStep.GridCol + 1, currentStep.GridRow),
            Orientation.West  => (currentStep.GridCol - 1, currentStep.GridRow),
            Orientation.South => (currentStep.GridCol,     currentStep.GridRow + 1),
            Orientation.North => (currentStep.GridCol,     currentStep.GridRow - 1),
            _                 => (-1, -1)
        };

        // ── Draw edges ────────────────────────────────────────────────────────
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var (fx, fy) = NodePos(s.GridCol, s.GridRow);
            var (tx, ty) = s.Orientation switch
            {
                Orientation.East  => NodePos(s.GridCol + 1, s.GridRow),
                Orientation.West  => NodePos(s.GridCol - 1, s.GridRow),
                Orientation.South => NodePos(s.GridCol,     s.GridRow + 1),
                Orientation.North => NodePos(s.GridCol,     s.GridRow - 1),
                _                 => NodePos(s.GridCol, s.GridRow)
            };

            if (i == curIdx)
            {
                AddArrow(fx, fy, tx, ty, activeColor, 3.0);
            }
            else
            {
                Color  edgeColor = i < curIdx ? completedColor : pendingColor;
                double thickness = i < curIdx ? 2.5 : 1.5;
                GridCanvas.Children.Add(new Line
                {
                    X1 = fx, Y1 = fy, X2 = tx, Y2 = ty,
                    Stroke          = new SolidColorBrush(edgeColor),
                    StrokeThickness = thickness
                });
            }
        }

        // ── Draw nodes ────────────────────────────────────────────────────────
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                bool isEndpoint = (c == currentStep.GridCol && r == currentStep.GridRow)
                               || (c == toCol              && r == toRow);

                Color  color  = isEndpoint ? activeColor  : pendingColor;
                double radius = isEndpoint ? HighlightR   : NodeRadius;

                var (cx, cy) = NodePos(c, r);
                var ellipse = new Ellipse
                {
                    Width  = radius * 2,
                    Height = radius * 2,
                    Fill   = new SolidColorBrush(color)
                };
                Canvas.SetLeft(ellipse, cx - radius);
                Canvas.SetTop(ellipse,  cy - radius);
                GridCanvas.Children.Add(ellipse);
            }
        }

        GridCanvas.Width  = (cols - 1) * xSpacing + (CanvasPad + HighlightR) * 2;
        GridCanvas.Height = (rows - 1) * ySpacing + (CanvasPad + HighlightR) * 2;
    }

    private void DrawParallelWaysMap(
        MeasurementStep               currentStep,
        int                           curIdx,
        IReadOnlyList<MeasurementStep> steps,
        Color                         activeColor,
        Color                         completedColor,
        Color                         pendingColor)
    {
        const double railSpacingPx = 60.0;
        const double pad           = 20.0;

        var allNodeIds = steps
            .SelectMany(s => new[] { s.NodeId, s.ToNodeId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        int maxRail = 0, maxSta = 0;
        foreach (var id in allNodeIds)
        {
            var (r, s) = ParallelWaysStrategy.ParseNodeId(id);
            if (r > maxRail) maxRail = r;
            if (s > maxSta)  maxSta  = s;
        }

        int numRails = maxRail + 1;
        int numSta   = maxSta  + 1;

        double stationSpacingPx = numSta > 1
            ? Math.Max(MinSpacingPx, (TargetMaxPx - pad * 2) / (numSta - 1))
            : TargetMaxPx / 2;

        (double x, double y) NodePos(string nodeId)
        {
            var (r, s) = ParallelWaysStrategy.ParseNodeId(nodeId);
            return (pad + s * stationSpacingPx, pad + r * railSpacingPx);
        }

        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (string.IsNullOrEmpty(s.NodeId) || string.IsNullOrEmpty(s.ToNodeId)) continue;

            var (fx, fy) = NodePos(s.NodeId);
            var (tx, ty) = NodePos(s.ToNodeId);

            if (i == curIdx)
            {
                AddArrow(fx, fy, tx, ty, activeColor, 2.5);
            }
            else
            {
                Color  edgeColor = i < curIdx ? completedColor : pendingColor;
                double thickness = i < curIdx ? 2.0 : 1.0;
                GridCanvas.Children.Add(new Line
                {
                    X1 = fx, Y1 = fy, X2 = tx, Y2 = ty,
                    Stroke          = new SolidColorBrush(edgeColor),
                    StrokeThickness = thickness
                });
            }
        }

        foreach (var nodeId in allNodeIds)
        {
            bool isEndpoint = nodeId == currentStep.NodeId || nodeId == currentStep.ToNodeId;
            Color  color  = isEndpoint ? activeColor  : pendingColor;
            double radius = isEndpoint ? HighlightR   : NodeRadius;

            var (cx, cy) = NodePos(nodeId);
            var ellipse = new Ellipse
            {
                Width  = radius * 2,
                Height = radius * 2,
                Fill   = new SolidColorBrush(color)
            };
            Canvas.SetLeft(ellipse, cx - radius);
            Canvas.SetTop(ellipse,  cy - radius);
            GridCanvas.Children.Add(ellipse);
        }

        GridCanvas.Width  = pad * 2 + (numSta - 1) * stationSpacingPx;
        GridCanvas.Height = pad * 2 + (numRails - 1) * railSpacingPx;
    }

    private void DrawUnionJackMap(
        MeasurementStep               currentStep,
        int                           curIdx,
        IReadOnlyList<MeasurementStep> steps,
        double                        widthMm,
        double                        heightMm,
        Color                         activeColor,
        Color                         completedColor,
        Color                         pendingColor)
    {
        var definition = ViewModel.Definition;
        if (definition is null || widthMm <= 0 || heightMm <= 0) return;

        double scale = TargetMaxPx / Math.Max(widthMm, heightMm);

        (double x, double y) NodePos(string nodeId)
        {
            var (mmX, mmY) = UnionJackStrategy.NodePositionById(nodeId, definition);
            return (mmX * scale + CanvasPad + HighlightR,
                    mmY * scale + CanvasPad + HighlightR);
        }

        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var (fx, fy) = NodePos(s.NodeId);
            var (tx, ty) = NodePos(s.ToNodeId);

            if (i == curIdx)
            {
                AddArrow(fx, fy, tx, ty, activeColor, 3.0);
            }
            else
            {
                Color  edgeColor = i < curIdx ? completedColor : pendingColor;
                double thickness = i < curIdx ? 2.5 : 1.5;
                GridCanvas.Children.Add(new Line
                {
                    X1 = fx, Y1 = fy, X2 = tx, Y2 = ty,
                    Stroke          = new SolidColorBrush(edgeColor),
                    StrokeThickness = thickness
                });
            }
        }

        var nodeIds = steps
            .SelectMany(s => new[] { s.NodeId, s.ToNodeId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct();

        foreach (var nodeId in nodeIds)
        {
            bool isEndpoint = nodeId == currentStep.NodeId || nodeId == currentStep.ToNodeId;
            Color  color  = isEndpoint ? activeColor  : pendingColor;
            double radius = isEndpoint ? HighlightR   : NodeRadius;

            var (cx, cy) = NodePos(nodeId);
            var ellipse = new Ellipse
            {
                Width  = radius * 2,
                Height = radius * 2,
                Fill   = new SolidColorBrush(color)
            };
            Canvas.SetLeft(ellipse, cx - radius);
            Canvas.SetTop(ellipse,  cy - radius);
            GridCanvas.Children.Add(ellipse);
        }

        GridCanvas.Width  = widthMm  * scale + (CanvasPad + HighlightR) * 2;
        GridCanvas.Height = heightMm * scale + (CanvasPad + HighlightR) * 2;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private void AddArrow(double x1, double y1, double x2, double y2, Color color, double thickness)
    {
        double dx  = x2 - x1;
        double dy  = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;

        double nx = dx / len;
        double ny = dy / len;
        double px = -ny;
        double py =  nx;

        double al = Math.Min(ArrowLen,   len * 0.40);
        double hw = Math.Min(ArrowHalfW, al  * 0.50);

        double bx = x2 - nx * al;
        double by = y2 - ny * al;

        var brush = new SolidColorBrush(color);

        GridCanvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = bx, Y2 = by,
            Stroke = brush, StrokeThickness = thickness
        });

        GridCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(x2,          y2),
                new Point(bx + px * hw, by + py * hw),
                new Point(bx - px * hw, by - py * hw)
            },
            Fill = brush
        });
    }

}
