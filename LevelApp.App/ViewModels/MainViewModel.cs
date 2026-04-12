using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Navigation;
using LevelApp.App.Services;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Geometry;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.ViewModels;

/// <summary>
/// Singleton ViewModel for the main window shell.
/// Owns the active project state, dirty flag, window title, and menu commands.
/// All page ViewModels receive this via DI injection and call <see cref="MarkDirty"/>
/// or <see cref="SetActiveProject"/> as needed.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly INavigationService  _navigation;
    private readonly IProjectFileService _fileService;
    private readonly IActivityLogger     _logger;

    // ── Global project state ──────────────────────────────────────────────────

    public Project? ActiveProject { get; private set; }
    public string?  CurrentFilePath { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isDirty;

    public string WindowTitle
    {
        get
        {
            if (ActiveProject is null) return "LevelApp";
            string name = _fileService.SuggestFileName(ActiveProject);
            return IsDirty ? $"LevelApp \u2014 {name} *" : $"LevelApp \u2014 {name}";
        }
    }

    // ── UI context (set from MainWindow after InitializeComponent) ────────────

    internal XamlRoot? XamlRoot { get; set; }
    internal nint      Hwnd     { get; set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel(INavigationService navigation, IProjectFileService fileService,
                         IActivityLogger logger)
    {
        _navigation  = navigation;
        _fileService = fileService;
        _logger      = logger;
    }

    // ── Public API for page ViewModels ────────────────────────────────────────

    /// <summary>
    /// Registers a newly created or loaded project as the active project.
    /// Clears the dirty flag and updates the title.
    /// </summary>
    public void SetActiveProject(Project project, string? filePath = null)
    {
        ActiveProject   = project;
        CurrentFilePath = filePath;
        IsDirty         = false;
        OnPropertyChanged(nameof(WindowTitle));
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Marks the active project as having unsaved changes.
    /// No-op if already dirty.
    /// </summary>
    public void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        // [NotifyPropertyChangedFor] and [NotifyCanExecuteChangedFor] handle the rest.
    }

    /// <summary>Clears all project state (used when starting a new project).</summary>
    public void ClearProject()
    {
        if (ActiveProject is not null)
            _logger.Log("File.Close");

        ActiveProject   = null;
        CurrentFilePath = null;
        IsDirty         = false;
        OnPropertyChanged(nameof(WindowTitle));
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
    }

    // ── Menu commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        ClearProject();
        _logger.Log("File.New");
        _navigation.NavigateTo(PageKey.ProjectSetup);
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (!await ConfirmDiscardChangesAsync()) return;

        (Project? project, string? path) result;
        try
        {
            result = await _fileService.OpenAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Open failed", ex.Message);
            return;
        }

        if (result.project is null) return; // user cancelled

        SetActiveProject(result.project, result.path);

        // Log File.Open and snapshot the project file
        if (result.path is not null)
            _logger.AttachProjectSnapshot(result.path);

        try
        {
            await RecalculateMissingResultsAsync(result.project);
        }
        catch { /* proceed without calculated results; user can start a new measurement */ }

        var completedSession = result.project.Measurements
            .LastOrDefault(m => m.InitialRound.Result is not null
                             || m.Corrections.Any(c => c.Result is not null));

        if (completedSession is not null)
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(result.project, completedSession));
        else
            _navigation.NavigateTo(PageKey.ProjectSetup, result.project);
    }

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private async Task SaveProjectAsync()
    {
        await SaveInternalAsync();
    }

    private bool CanSaveProject() => ActiveProject is not null && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSaveProjectAs))]
    private async Task SaveProjectAsAsync()
    {
        if (ActiveProject is null) return;

        string suggested = _fileService.SuggestFileName(ActiveProject);
        string? path;
        try
        {
            path = await _fileService.SaveAsAsync(ActiveProject, suggested);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Save failed", ex.Message);
            return;
        }

        if (path is null) return; // user cancelled

        CurrentFilePath = path;
        IsDirty         = false;
        OnPropertyChanged(nameof(WindowTitle));
        _logger.Log("File.SaveAs", path);
    }

    private bool CanSaveProjectAs() => ActiveProject is not null;

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        _logger.Log("Session.End");
        Application.Current.Exit();
    }

    // ── Unsaved-changes check (also called from MainWindow on window close) ───

    /// <summary>
    /// If there are unsaved changes, shows the "Unsaved changes" dialog.
    /// Returns <c>true</c> if it is safe to proceed (changes saved or discarded),
    /// <c>false</c> if the user cancelled.
    /// </summary>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!IsDirty || ActiveProject is null) return true;

        var dialog = new ContentDialog
        {
            Title             = "Unsaved changes",
            Content           = $"You have unsaved changes to \u201c{ActiveProject.Name}\u201d.\n" +
                                "Do you want to save before continuing?",
            PrimaryButtonText   = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText     = "Cancel",
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary   => await SaveInternalAsync(),
            ContentDialogResult.Secondary => true,
            _                             => false
        };
    }

    // ── Post-load calculation ─────────────────────────────────────────────────

    /// <summary>
    /// Runs the calculator for any round whose steps are fully populated but
    /// whose result is missing — supports files saved before result serialisation
    /// was enforced and externally produced files.
    /// </summary>
    private static async Task RecalculateMissingResultsAsync(Project project)
    {
        foreach (var session in project.Measurements)
        {
            try { await RecalculateSessionAsync(session, project.ObjectDefinition); }
            catch { /* a bad session must not block the rest */ }
        }
    }

    private static async Task RecalculateSessionAsync(MeasurementSession session, ObjectDefinition def)
    {
        var initialRound = session.InitialRound;
        var strategy     = StrategyFactory.Create(session.StrategyId);
        var parameters   = initialRound.CalculationParameters ?? new CalculationParameters();
        var calculator   = CalculatorFactory.Create(parameters.MethodId, strategy);

        if (initialRound.Result is null &&
            initialRound.Steps.Count > 0 &&
            initialRound.Steps.All(s => s.Reading.HasValue))
        {
            initialRound.Result = await Task.Run(() =>
                calculator.Calculate(initialRound.Steps, def, parameters));
        }

        if (!initialRound.Steps.All(s => s.Reading.HasValue)) return;

        foreach (var correction in session.Corrections)
        {
            if (correction.Result is not null) continue;
            if (correction.ReplacedSteps.Count == 0) continue;

            var mergedSteps = MeasurementRound.MergeWithReplacements(
                initialRound.Steps, correction.ReplacedSteps);

            correction.Result = await Task.Run(() =>
                calculator.Calculate(mergedSteps, def, parameters));
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Saves to the known path silently, or shows Save As if no path is stored.
    /// Returns <c>true</c> on success.
    /// </summary>
    private async Task<bool> SaveInternalAsync()
    {
        if (ActiveProject is null) return false;

        if (CurrentFilePath is not null)
        {
            try
            {
                await _fileService.SaveToPathAsync(ActiveProject, CurrentFilePath);
                IsDirty = false;
                OnPropertyChanged(nameof(WindowTitle));
                _logger.Log("File.Save", CurrentFilePath);
                return true;
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Save failed", ex.Message);
                return false;
            }
        }

        // No known path — fall through to Save As
        string suggested = _fileService.SuggestFileName(ActiveProject);
        string? path;
        try
        {
            path = await _fileService.SaveAsAsync(ActiveProject, suggested);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Save failed", ex.Message);
            return false;
        }

        if (path is null) return false; // user cancelled

        CurrentFilePath = path;
        IsDirty         = false;
        OnPropertyChanged(nameof(WindowTitle));
        _logger.Log("File.SaveAs", CurrentFilePath);
        return true;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        if (XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title           = title,
            Content         = message,
            CloseButtonText = "OK",
            XamlRoot        = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
