namespace LevelApp.Core.Interfaces;

public interface ICalibrationWorkflow
{
    string DisplayName { get; }

    /// <summary>
    /// Returns a UIElement the app embeds in the instrument management tab.
    /// Declared as object to keep Core free of WinUI dependencies;
    /// cast to UIElement in LevelApp.App.
    /// </summary>
    object CreateView();
}
