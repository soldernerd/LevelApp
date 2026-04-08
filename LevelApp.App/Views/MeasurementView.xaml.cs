using System.ComponentModel;
using LevelApp.App.Navigation;
using LevelApp.App.ViewModels;
using LevelApp.Core.Models;
using Orientation = LevelApp.Core.Models.Orientation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace LevelApp.App.Views;

public sealed partial class MeasurementView : Page
{
    // ── Constants for canvas rendering ────────────────────────────────────────
    private const double NodeRadius   = 6.0;
    private const double NodeSpacing  = 30.0;

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
    /// Redraws the grid map canvas.
    /// • Orange  — "from" node of the current step
    /// • Blue    — "to" node of the current step
    /// • Green   — nodes already measured (from-node of completed steps)
    /// • Grey    — untouched nodes
    /// </summary>
    private void DrawGridMap()
    {
        GridCanvas.Children.Clear();

        var step = ViewModel.CurrentStep;
        if (step is null) return;

        int cols = ViewModel.GridColumns;
        int rows = ViewModel.GridRows;

        // Union Jack (and other non-grid strategies) don't use GridColumns/GridRows
        if (cols <= 0 || rows <= 0) return;

        // Determine the "to" node
        (int toCol, int toRow) = step.Orientation switch
        {
            Orientation.East  => (step.GridCol + 1, step.GridRow),
            Orientation.West  => (step.GridCol - 1, step.GridRow),
            Orientation.South => (step.GridCol,     step.GridRow + 1),
            Orientation.North => (step.GridCol,     step.GridRow - 1),
            _                 => (-1, -1)
        };

        // Collect from-positions of completed steps for the "measured" colour
        var measuredFroms = ViewModel.Steps
            .Take(ViewModel.CurrentStepIndex)
            .Where(s => s.Reading.HasValue)
            .Select(s => (s.GridCol, s.GridRow))
            .ToHashSet();

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double cx = c * NodeSpacing;
                double cy = r * NodeSpacing;

                Color colour;
                double radius = NodeRadius;

                if (c == step.GridCol && r == step.GridRow)
                {
                    colour = Colors.Orange;           // current from-node
                    radius = NodeRadius + 3;
                }
                else if (c == toCol && r == toRow)
                {
                    colour = Colors.CornflowerBlue;   // current to-node
                    radius = NodeRadius + 3;
                }
                else if (measuredFroms.Contains((c, r)))
                {
                    colour = Color.FromArgb(255, 80, 170, 80); // measured
                }
                else
                {
                    colour = Color.FromArgb(255, 150, 150, 150); // unvisited
                }

                var ellipse = new Ellipse
                {
                    Width  = radius * 2,
                    Height = radius * 2,
                    Fill   = new Microsoft.UI.Xaml.Media.SolidColorBrush(colour)
                };

                Canvas.SetLeft(ellipse, cx - radius);
                Canvas.SetTop(ellipse,  cy - radius);
                GridCanvas.Children.Add(ellipse);
            }
        }

        GridCanvas.Width  = (cols - 1) * NodeSpacing + NodeRadius * 2;
        GridCanvas.Height = (rows - 1) * NodeSpacing + NodeRadius * 2;
    }
}
