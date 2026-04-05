using LevelApp.Core.Models;

namespace LevelApp.App.Navigation;

/// <summary>Navigation parameter passed from ResultsView to CorrectionView.</summary>
public sealed record CorrectionArgs(Project Project, MeasurementSession Session);
