using LevelApp.Core.Models;

namespace LevelApp.App.ViewModels;

/// <summary>
/// One line of the project tab's list of measurements. The name can be edited there: it is written to
/// the measurement (<see cref="MeasurementSession.Label"/>) and <paramref name="renamed"/> lets the
/// owner save the project and refresh the measurement pull-down.
/// </summary>
public sealed class ProjectMeasurementRow(
    MeasurementSession session, string strategy, string taken, string instrument, string outcome,
    Action<MeasurementSession> renamed)
{
    public string Label
    {
        get => session.Label;
        set
        {
            string name = value.Trim();
            if (name.Length == 0 || name == session.Label) return;
            session.Label = name;
            renamed(session);
        }
    }

    public string Strategy   { get; } = strategy;
    public string Taken      { get; } = taken;
    public string Instrument { get; } = instrument;
    public string Outcome    { get; } = outcome;
}
