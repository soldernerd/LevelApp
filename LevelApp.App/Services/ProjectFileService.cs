using LevelApp.Core.Models;
using LevelApp.Core.Serialization;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LevelApp.App.Services;

/// <summary>
/// Presents native file-picker dialogs and delegates to
/// <see cref="ProjectSerializer"/> for JSON I/O.
///
/// Call <see cref="Initialize"/> once from <c>MainWindow</c> after the window
/// handle is available (required for unpackaged WinUI 3 apps).
/// </summary>
public sealed class ProjectFileService
{
    private nint _hwnd;

    /// <summary>Stores the owner window handle used to parent file-picker dialogs.</summary>
    public void Initialize(nint hwnd) => _hwnd = hwnd;

    /// <summary>
    /// Opens a Save dialog and writes the project as a <c>.levelproj</c> file.
    /// Returns <c>true</c> if the file was saved; <c>false</c> if the user cancelled.
    /// </summary>
    public async Task<bool> SaveAsync(Project project)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName      = project.Name.Length > 0 ? project.Name : "project";
        picker.FileTypeChoices.Add("Level Project", [".levelproj"]);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;

        string json = ProjectSerializer.Serialize(project);
        await FileIO.WriteTextAsync(file, json);
        return true;
    }

    /// <summary>
    /// Opens an Open dialog and deserialises the selected <c>.levelproj</c> file.
    /// Returns the loaded <see cref="Project"/>, or <c>null</c> if the user cancelled.
    /// </summary>
    public async Task<Project?> OpenAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".levelproj");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return null;

        string json = await FileIO.ReadTextAsync(file);
        return ProjectSerializer.Deserialize(json);
    }
}
