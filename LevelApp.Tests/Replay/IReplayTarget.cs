namespace LevelApp.Tests.Replay;

/// <summary>
/// Minimal abstraction consumed by <see cref="ActivityReplayRunner"/>.
///
/// TODO: Replace with <c>LevelApp.App.ViewModels.MainViewModel</c> once the test
/// project is updated to target <c>net8.0-windows10.0.19041.0</c> and references
/// <c>LevelApp.App</c>.  At that point <c>MainViewModel</c> should implement this
/// interface (or the runner should be updated to take <c>MainViewModel</c> directly).
/// </summary>
public interface IReplayTarget
{
    // TODO: Add members as ViewModel call sites are implemented in replay
}
