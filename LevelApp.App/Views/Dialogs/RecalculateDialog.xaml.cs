using LevelApp.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

/// <summary>
/// Dialog for choosing calculation method, sigma threshold, and manual step exclusions.
///
/// Primary   = "Recalculate"          — updates in-memory result only
/// Secondary = "Recalculate &amp; Save" — updates result AND saves CalculationParameters
/// Close     = "Cancel"               — no action
/// </summary>
public sealed partial class RecalculateDialog : ContentDialog
{
    private readonly List<FlaggedStepEntry> _flaggedEntries;

    public RecalculateDialog(CalculationParameters currentParams, SurfaceResult? currentResult)
    {
        InitializeComponent();

        var loc = App.Services.GetRequiredService<LevelApp.App.Services.ILocalisationService>();
        Title               = loc.Get("Recalculate_Title.Text");
        PrimaryButtonText   = loc.Get("Recalculate_OkButton.Content");
        SecondaryButtonText = loc.Get("Recalculate_SaveResult_Label.Text");
        CloseButtonText     = loc.Get("Recalculate_CancelButton.Content");

        MethodCombo.SelectedIndex = currentParams.MethodId == "SequentialIntegration" ? 1 : 0;
        AutoExcludeToggle.IsOn    = currentParams.AutoExcludeOutliers;
        SigmaBox.Value            = currentParams.SigmaThreshold;
        SigmaBox.IsEnabled        = currentParams.AutoExcludeOutliers;

        _flaggedEntries = [];
        if (currentResult is not null)
        {
            var manualExclSet = new HashSet<int>(currentParams.ManuallyExcludedStepIndices);
            foreach (int stepIdx in currentResult.FlaggedStepIndices)
            {
                _flaggedEntries.Add(new FlaggedStepEntry
                {
                    StepIndex  = stepIdx,
                    Label      = $"Step {stepIdx + 1}",
                    IsIncluded = !manualExclSet.Contains(stepIdx)
                });
            }
        }

        FlaggedList.ItemsSource  = _flaggedEntries;
        FlaggedPanel.Visibility  = _flaggedEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Public result accessor ────────────────────────────────────────────────

    /// <summary>Returns CalculationParameters built from the current dialog state.</summary>
    public CalculationParameters BuildParameters() => new()
    {
        MethodId = MethodCombo.SelectedIndex == 1 ? "SequentialIntegration" : "LeastSquares",
        SigmaThreshold       = double.IsNaN(SigmaBox.Value) ? 2.5 : SigmaBox.Value,
        AutoExcludeOutliers  = AutoExcludeToggle.IsOn,
        ManuallyExcludedStepIndices = _flaggedEntries
            .Where(e => !e.IsIncluded)
            .Select(e => e.StepIndex)
            .ToList()
    };

    // ── Event handler ─────────────────────────────────────────────────────────

    private void OnAutoExcludeToggled(object sender, RoutedEventArgs e)
    {
        // Guard against partial initialisation: WinUI 3 fires this event during
        // InitializeComponent() when IsOn="True" is applied to AutoExcludeToggle,
        // before SigmaBox (defined after it in XAML) has been instantiated.
        // Use sender (always non-null) instead of the AutoExcludeToggle field reference.
        if (SigmaBox is not null && sender is ToggleSwitch toggle)
            SigmaBox.IsEnabled = toggle.IsOn;
    }
}

internal sealed class FlaggedStepEntry
{
    public int    StepIndex  { get; init; }
    public string Label      { get; init; } = string.Empty;
    public bool   IsIncluded { get; set; }
}
