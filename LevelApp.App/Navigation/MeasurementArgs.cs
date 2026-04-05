using LevelApp.Core.Models;

namespace LevelApp.App.Navigation;

/// <summary>Navigation parameter passed from ProjectSetupView to MeasurementView.</summary>
public sealed record MeasurementArgs(Project Project, MeasurementSession Session);
