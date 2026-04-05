using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// Renders a completed surface result into a UI element.
/// Returning <see cref="object"/> keeps Core free of UI framework dependencies.
/// WinUI 3 implementations cast the return value to UIElement.
/// </summary>
public interface IResultDisplay
{
    string DisplayId { get; }
    string DisplayName { get; }
    object Render(SurfaceResult result);
}
