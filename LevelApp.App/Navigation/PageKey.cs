namespace LevelApp.App.Navigation;

/// <summary>
/// Identifies each navigable page. ViewModels use this enum so they have no
/// compile-time dependency on concrete View types.
/// </summary>
public enum PageKey
{
    ProjectSetup,
    Measurement,
    Results,
    Correction
}
