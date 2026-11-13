namespace LevelApp.App.ViewModels;

/// <summary>One line of the project tab's list of measurements.</summary>
public sealed record ProjectMeasurementRow(
    string Label, string Strategy, string Taken, string Instrument, string Outcome);
