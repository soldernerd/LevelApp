using LevelApp.Core.Models;

namespace LevelApp.App.Navigation;

/// <summary>Navigation parameter passed from MeasurementView to ResultsView.</summary>
public sealed record ResultsArgs(Project Project, MeasurementSession Session);
