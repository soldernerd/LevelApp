using LevelApp.Core.Models;

namespace LevelApp.App.Services;

public interface IProjectFileService
{
    void Initialize(nint hwnd);
    Task SaveToPathAsync(Project project, string path);
    Task<string?> SaveAsAsync(Project project, string suggestedFileName);
    Task<(Project? project, string? path)> OpenAsync();
    string SuggestFileName(Project project);
}
