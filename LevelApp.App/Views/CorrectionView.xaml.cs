using System.ComponentModel;
using LevelApp.App.Navigation;
using LevelApp.App.ViewModels;
using LevelApp.Core.Models;
using Orientation = LevelApp.Core.Models.Orientation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace LevelApp.App.Views;

public sealed partial class CorrectionView : Page
{
    private const double NodeRadius  = 6.0;
    private const double NodeSpacing = 30.0;

    public CorrectionViewModel ViewModel { get; }

    public CorrectionView()
    {
        ViewModel = App.Services.GetRequiredService<CorrectionViewModel>();
        this.InitializeComponent();
    }

    // ── Navigation lifecycle ──────────────────────────────────────────────────

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is CorrectionArgs args)
        {
            ViewModel.Initialize(args);
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            this.ActualThemeChanged   += OnActualThemeChanged;
            DrawCorrectionMap();
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
            nameof(CorrectionViewModel.CurrentStepIndex) or
            nameof(CorrectionViewModel.CurrentStep))
        {
            DispatcherQueue.TryEnqueue(DrawCorrectionMap);
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
        => DrawCorrectionMap();

    /// <summary>
    /// Redraws the grid map for the correction session.
    /// • GridCurrentStepBrush  — current step from-node and to-node
    /// • GridCompletedStepBrush — already-corrected flagged steps
    /// • GridFlaggedStepBrush   — pending (not-yet-corrected) flagged steps
    /// • GridPendingStepBrush   — non-flagged nodes
    /// All colours are resolved from the ThemeColors resource dictionary at draw time.
    /// </summary>
    private void DrawCorrectionMap()
    {
        GridCanvas.Children.Clear();

        var step = ViewModel.CurrentStep;
        if (step is null) return;

        // Resolve theme colours at draw time
        Color activeColor    = GetThemeColor("GridCurrentStepBrush");
        Color completedColor = GetThemeColor("GridCompletedStepBrush");
        Color flaggedColor   = GetThemeColor("GridFlaggedStepBrush");
        Color pendingColor   = GetThemeColor("GridPendingStepBrush");

        int cols = ViewModel.GridColumns;
        int rows = ViewModel.GridRows;

        (int toCol, int toRow) = step.Orientation switch
        {
            Orientation.East      => (step.GridCol + 1, step.GridRow),
            Orientation.West      => (step.GridCol - 1, step.GridRow),
            Orientation.South     => (step.GridCol,     step.GridRow + 1),
            Orientation.North     => (step.GridCol,     step.GridRow - 1),
            Orientation.SouthEast => (step.GridCol + 1, step.GridRow + 1),
            Orientation.SouthWest => (step.GridCol - 1, step.GridRow + 1),
            Orientation.NorthEast => (step.GridCol + 1, step.GridRow - 1),
            Orientation.NorthWest => (step.GridCol - 1, step.GridRow - 1),
            _                     => (-1, -1)
        };

        var alreadyCorrected = ViewModel.FlaggedSteps
            .Take(ViewModel.CurrentStepIndex)
            .Select(s => (s.GridCol, s.GridRow))
            .ToHashSet();

        var pendingFlagged = ViewModel.FlaggedSteps
            .Skip(ViewModel.CurrentStepIndex + 1)
            .Select(s => (s.GridCol, s.GridRow))
            .ToHashSet();

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double cx     = c * NodeSpacing;
                double cy     = r * NodeSpacing;
                Color  colour;
                double radius = NodeRadius;

                if (c == step.GridCol && r == step.GridRow)
                {
                    colour = activeColor;
                    radius = NodeRadius + 3;
                }
                else if (c == toCol && r == toRow)
                {
                    colour = activeColor;
                    radius = NodeRadius + 3;
                }
                else if (alreadyCorrected.Contains((c, r)))
                {
                    colour = completedColor;
                }
                else if (pendingFlagged.Contains((c, r)))
                {
                    colour = flaggedColor;
                }
                else
                {
                    colour = pendingColor;
                }

                var ellipse = new Ellipse
                {
                    Width  = radius * 2,
                    Height = radius * 2,
                    Fill   = new SolidColorBrush(colour)
                };
                Canvas.SetLeft(ellipse, cx - radius);
                Canvas.SetTop(ellipse,  cy - radius);
                GridCanvas.Children.Add(ellipse);
            }
        }

        GridCanvas.Width  = (cols - 1) * NodeSpacing + NodeRadius * 2;
        GridCanvas.Height = (rows - 1) * NodeSpacing + NodeRadius * 2;
    }

    // ── Theme helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a named theme colour using GridCanvas (in the visual tree) first,
    /// then falls back to application resources.
    /// </summary>
    private Color GetThemeColor(string resourceKey)
    {
        if (GridCanvas.Resources.TryGetValue(resourceKey, out var res)
            || Application.Current.Resources.TryGetValue(resourceKey, out res))
        {
            return res is SolidColorBrush brush ? brush.Color : Colors.Gray;
        }
        return Colors.Gray;
    }
}
