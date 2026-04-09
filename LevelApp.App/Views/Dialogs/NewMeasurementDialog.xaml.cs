using LevelApp.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class NewMeasurementDialog : ContentDialog
{
    public string Label        => LabelBox.Text;
    public string OperatorName => OperatorBox.Text;
    public string Notes        => NotesBox.Text;

    public string StrategyId => StrategyCombo.SelectedIndex switch
    {
        1    => "UnionJack",
        _    => "FullGrid"
    };

    public NewMeasurementDialog(ObjectDefinition definition, string defaultOperator)
    {
        InitializeComponent();

        var loc = App.Services.GetRequiredService<LevelApp.App.Services.ILocalisationService>();
        Title             = loc.Get("NewMeasurement_Title.Text");
        PrimaryButtonText = loc.Get("NewMeasurement_OkButton.Content");
        CloseButtonText   = loc.Get("NewMeasurement_CancelButton.Content");

        GeometrySummaryText.Text = BuildGeometrySummary(definition);
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
