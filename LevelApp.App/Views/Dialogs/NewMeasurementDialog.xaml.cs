using LevelApp.App.Services;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class NewMeasurementDialog : ContentDialog
{
    public string Label        => LabelBox.Text;
    public string OperatorName => OperatorBox.Text;
    public string Notes        => NotesBox.Text;

    private readonly IReadOnlyList<InstrumentChoice> _choices;

    /// <summary>The instrument picked in the dialog (null only if none is offered).</summary>
    public InstrumentChoice? SelectedInstrument =>
        InstrumentCombo.SelectedIndex >= 0 && InstrumentCombo.SelectedIndex < _choices.Count
            ? _choices[InstrumentCombo.SelectedIndex]
            : null;

    public NewMeasurementDialog(string summary, string defaultOperator,
                                IReadOnlyList<InstrumentChoice> instruments, int defaultInstrumentIndex)
    {
        InitializeComponent();

        _choices = instruments;
        foreach (var choice in instruments)
            InstrumentCombo.Items.Add(choice.Label);
        InstrumentCombo.SelectedIndex = instruments.Count == 0
            ? -1
            : Math.Clamp(defaultInstrumentIndex, 0, instruments.Count - 1);
        NoInstrumentHint.Visibility = instruments.Any(c => c.Device is not null)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        GeometrySummaryText.Text = summary;
        OperatorBox.Text         = defaultOperator;
    }

    private static string BuildGeometrySummary(ObjectDefinition def)
    {
        string geom = def.GeometryModuleId;

        if (def.Parameters.TryGetValue("widthMm",  out var w) &&
            def.Parameters.TryGetValue("heightMm", out var h))
        {
            int wMm = Convert.ToInt32(w);
            int hMm = Convert.ToInt32(h);
            return $"{geom}  {wMm} \u00d7 {hMm} mm";
        }
        return geom;
    }
}
