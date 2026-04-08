using System.ComponentModel;
using LevelApp.App.Navigation;
using LevelApp.App.ViewModels;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;
using Orientation = LevelApp.Core.Models.Orientation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
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
            DrawGridMap();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    // ── Canvas refresh ────────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Redraw whenever the active step changes
        if (e.PropertyName is
            nameof(MeasurementViewModel.CurrentStepIndex) or
            nameof(MeasurementViewModel.CurrentStep))
        {
            DispatcherQueue.TryEnqueue(DrawGridMap);
        }
    }

    /// <summary>
    /// Redraws the grid map canvas with nodes and edges.
    /// Edge colour states: pending = grey | current step = accent blue | completed = green.
    /// Node colour: grey for all nodes, except the two endpoints of the current step
    /// which are drawn in accent blue.  Nodes never permanently turn green — only edges do.
    /// Pixel spacing is proportional to the plate's physical aspect ratio.
    /// </summary>
    private void DrawGridMap()
    {
        GridCanvas.Children.Clear();

        var currentStep = ViewModel.CurrentStep;
        if (currentStep is null) return;

        int    cols      = ViewModel.GridColumns;
        int    rows      = ViewModel.GridRows;
        int    curIdx    = ViewModel.CurrentStepIndex;
        var    steps     = ViewModel.Steps;
        double widthMm   = ViewModel.WidthMm;
        double heightMm  = ViewModel.HeightMm;

        // Union Jack (and other non-grid strategies) don't use GridColumns/GridRows
        if (cols <= 0 || rows <= 0)
        {
            DrawUnionJackMap(currentStep, curIdx, steps, widthMm, heightMm);
            return;
        }

        // Pixel spacing proportional to physical dimensions, capped to TargetMaxPx
        double scale    = TargetMaxPx / Math.Max(widthMm, heightMm);
        double xSpacing = Math.Max(MinSpacingPx, widthMm  / (cols - 1) * scale);
        double ySpacing = Math.Max(MinSpacingPx, heightMm / (rows - 1) * scale);

        // Node centres include padding so the outermost highlighted nodes are not clipped
        (double x, double y) NodePos(int c, int r) =>
            (c * xSpacing + CanvasPad + HighlightR,
             r * ySpacing + CanvasPad + HighlightR);

        // Accent colour from the Fluent system palette
        Color accentColor;
        try   { accentColor = (Color)Application.Current.Resources["SystemAccentColor"]; }
        catch { accentColor = Color.FromArgb(255, 0, 120, 212); }

        var greenColor   = Color.FromArgb(255, 0, 160, 80);
        var greyColor    = Color.FromArgb(255, 140, 140, 140);
        var pendingColor = Color.FromArgb(180, 140, 140, 140);

        // Endpoints of the active step
        (int toCol, int toRow) = currentStep.Orientation switch
        {
            Orientation.East  => (currentStep.GridCol + 1, currentStep.GridRow),
            Orientation.West  => (currentStep.GridCol - 1, currentStep.GridRow),
            Orientation.South => (currentStep.GridCol,     currentStep.GridRow + 1),
            Orientation.North => (currentStep.GridCol,     currentStep.GridRow - 1),
            _                 => (-1, -1)
        };

        // ── Draw edges (painter order: drawn before nodes) ────────────────────
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

            Color  edgeColor;
            double thickness;
            if (i == curIdx)
            {
                edgeColor = accentColor;
                thickness = 3.0;
            }
            else if (i < curIdx)
            {
                edgeColor = greenColor;
                thickness = 2.5;
            }
            else
            {
                edgeColor = pendingColor;
                thickness = 1.5;
            }

            GridCanvas.Children.Add(new Line
            {
                X1 = fx, Y1 = fy, X2 = tx, Y2 = ty,
                Stroke          = new Microsoft.UI.Xaml.Media.SolidColorBrush(edgeColor),
                StrokeThickness = thickness
            });
        }

        // ── Draw nodes (on top of edges) ──────────────────────────────────────
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                bool isEndpoint = (c == currentStep.GridCol && r == currentStep.GridRow)
                               || (c == toCol              && r == toRow);

                Color  color  = isEndpoint ? accentColor : greyColor;
                double radius = isEndpoint ? HighlightR  : NodeRadius;

                var (cx, cy) = NodePos(c, r);
                var ellipse = new Ellipse
                {
                    Width  = radius * 2,
                    Height = radius * 2,
                    Fill   = new Microsoft.UI.Xaml.Media.SolidColorBrush(color)
                };
                Canvas.SetLeft(ellipse, cx - radius);
                Canvas.SetTop(ellipse,  cy - radius);
                GridCanvas.Children.Add(ellipse);
            }
        }

        // Canvas size: last node centre + padding to avoid clipping
        GridCanvas.Width  = (cols - 1) * xSpacing + (CanvasPad + HighlightR) * 2;
        GridCanvas.Height = (rows - 1) * ySpacing + (CanvasPad + HighlightR) * 2;
    }

    /// <summary>
    /// Draws the Union Jack step map using physical node positions from
    /// <see cref="UnionJackStrategy.NodePositionById"/>.
    /// Same colour states as the Full Grid: pending grey → current accent → completed green.
    /// </summary>
    private void DrawUnionJackMap(
        MeasurementStep               currentStep,
        int                           curIdx,
        IReadOnlyList<MeasurementStep> steps,
        double                        widthMm,
        double                        heightMm)
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

        // Accent colour from the Fluent system palette
        Color accentColor;
        try   { accentColor = (Color)Application.Current.Resources["SystemAccentColor"]; }
        catch { accentColor = Color.FromArgb(255, 0, 120, 212); }

        var greenColor   = Color.FromArgb(255, 0, 160, 80);
        var greyColor    = Color.FromArgb(255, 140, 140, 140);
        var pendingColor = Color.FromArgb(180, 140, 140, 140);

        // ── Draw edges (painter order: drawn before nodes) ────────────────────
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var (fx, fy) = NodePos(s.NodeId);
            var (tx, ty) = NodePos(s.ToNodeId);

            Color  edgeColor;
            double thickness;
            if (i == curIdx)        { edgeColor = accentColor;  thickness = 3.0; }
            else if (i < curIdx)    { edgeColor = greenColor;   thickness = 2.5; }
            else                    { edgeColor = pendingColor;  thickness = 1.5; }

            GridCanvas.Children.Add(new Line
            {
                X1 = fx, Y1 = fy, X2 = tx, Y2 = ty,
                Stroke          = new Microsoft.UI.Xaml.Media.SolidColorBrush(edgeColor),
                StrokeThickness = thickness
            });
        }

        // ── Draw nodes (on top of edges) ──────────────────────────────────────
        var nodeIds = steps
            .SelectMany(s => new[] { s.NodeId, s.ToNodeId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct();

        foreach (var nodeId in nodeIds)
        {
            bool isEndpoint = nodeId == currentStep.NodeId || nodeId == currentStep.ToNodeId;
            Color  color  = isEndpoint ? accentColor : greyColor;
            double radius = isEndpoint ? HighlightR  : NodeRadius;

            var (cx, cy) = NodePos(nodeId);
            var ellipse = new Ellipse
            {
                Width  = radius * 2,
                Height = radius * 2,
                Fill   = new Microsoft.UI.Xaml.Media.SolidColorBrush(color)
            };
            Canvas.SetLeft(ellipse, cx - radius);
            Canvas.SetTop(ellipse,  cy - radius);
            GridCanvas.Children.Add(ellipse);
        }

        // Canvas size: plate bounding box + padding
        GridCanvas.Width  = widthMm  * scale + (CanvasPad + HighlightR) * 2;
        GridCanvas.Height = heightMm * scale + (CanvasPad + HighlightR) * 2;
    }
}
