using LevelApp.Core.Models;

namespace LevelApp.App.Services;

public interface IProjectFileService
{
    void Initialize(nint hwnd);
    Task SaveToPathAsync(Project project, string path);
    Task<string?> SaveAsAsync(Project project, string suggestedFileName);
    Task<(Project? project, string? path)> OpenAsync();

    /// <summary>
    /// Writes <paramref name="project"/> to the recovery file: the safety copy of a project that has no
    /// file yet, kept up to date while measuring so an interruption loses nothing.
    /// </summary>
    Task SaveRecoveryAsync(Project project);

    /// <summary>Reads the recovery file, or null when there is none (or it cannot be read).</summary>
    Task<Project?> TryLoadRecoveryAsync();

    /// <summary>Removes the recovery file (the project was saved, or the operator discarded it).</summary>
    void DeleteRecovery();

    /// <summary>Reads the project at <paramref name="path"/> without showing a dialog.</summary>
    Task<Project> OpenFromPathAsync(string path);
    string SuggestFileName(Project project);
}
