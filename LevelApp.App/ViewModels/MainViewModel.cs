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
    private readonly IWindowContext      _windowContext;
    private readonly ISettingsService    _settings;
    private readonly SemaphoreSlim       _autoSaveGate = new(1, 1);

    // ── Global project state ──────────────────────────────────────────────────

    public Project? ActiveProject { get; private set; }
    private string? _currentFilePath;

    /// <summary>Where the active project is saved; remembered as the project to offer at the next start.</summary>
    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            _currentFilePath = value;
            if (value is null) return;
            _settings.LastProjectPath = value;
            _settings.Save();
        }
    }

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

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel(INavigationService navigation, IProjectFileService fileService,
                         IActivityLogger logger, IWindowContext windowContext,
                         ISettingsService settings)
    {
        _settings      = settings;
        _navigation    = navigation;
        _fileService   = fileService;
        _logger        = logger;
        _windowContext = windowContext;
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

        await ActivateOpenedProjectAsync(result.project, result.path);
    }

    /// <summary>
    /// At start-up: when a project was open last time and its file still exists, asks whether to
    /// open it again, and opens it if so. Does nothing otherwise.
    /// </summary>
    public async Task OfferReopenLastProjectAsync()
    {
        if (_windowContext.XamlRoot is null) return;

        // An unsaved project that was interrupted comes first.
        if (await OfferRecoveryAsync()) return;

        string? path = _settings.LastProjectPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var dialog = new ContentDialog
        {
            Title             = "Open the last project?",
            Content           = $"You last worked on\n{path}\n\nOpen it again?",
            PrimaryButtonText = "Open",
            CloseButtonText   = "Not now",
            DefaultButton     = ContentDialogButton.Primary,
            XamlRoot          = _windowContext.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Project project;
        try
        {
            project = await _fileService.OpenFromPathAsync(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Open failed", ex.Message);
            return;
        }

        await ActivateOpenedProjectAsync(project, path);
    }

    /// <summary>
    /// Offers the project that was being measured when the app last ended without saving it.
    /// Returns true when it was recovered (and opened).
    /// </summary>
    private async Task<bool> OfferRecoveryAsync()
    {
        var recovered = await _fileService.TryLoadRecoveryAsync();
        if (recovered is null) return false;

        var dialog = new ContentDialog
        {
            Title               = "Recover the unsaved project?",
            Content             = $"The last session ended before \u201c{recovered.Name}\u201d was saved. " +
                                  "Its measurements were kept. Open it now and save it with Save As?",
            PrimaryButtonText   = "Recover",
            SecondaryButtonText = "Discard it",
            CloseButtonText     = "Not now",
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = _windowContext.XamlRoot
        };

        var answer = await dialog.ShowAsync();
        if (answer == ContentDialogResult.Secondary)
        {
            _fileService.DeleteRecovery();
            return false;
        }
        if (answer != ContentDialogResult.Primary) return false;

        await ActivateOpenedProjectAsync(recovered, null);
        MarkDirty(); // it has no file yet
        return true;
    }

    private async Task ActivateOpenedProjectAsync(Project project, string? path)
    {
        var result = (project, path);
        SetActiveProject(result.project, result.path);

        // Log File.Open and snapshot the project file
        if (result.path is not null)
            _logger.AttachProjectSnapshot(result.path);

        try
        {
            await RecalculateMissingResultsAsync(result.project);
        }
        catch { /* proceed without calculated results; user can start a new measurement */ }

        // A measurement that was started and not finished is offered first: its readings are kept.
        if (FindUnfinished(result.project) is { } unfinished && await AskResumeAsync(unfinished))
        {
            _navigation.NavigateTo(PageKey.Measurement, new MeasurementArgs(result.project, unfinished));
            return;
        }

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
        _fileService.DeleteRecovery();
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
        if (_windowContext.XamlRoot is null)   return true; // guard: window not yet loaded — mirrors ShowErrorAsync

        var dialog = new ContentDialog
        {
            Title             = "Unsaved changes",
            Content           = $"You have unsaved changes to \u201c{ActiveProject.Name}\u201d.\n" +
                                "Do you want to save before continuing?",
            PrimaryButtonText   = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText     = "Cancel",
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = _windowContext.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary   => await SaveInternalAsync(),
            ContentDialogResult.Secondary => DiscardRecovery(),
            _                             => false
        };
    }

    private bool DiscardRecovery()
    {
        // The operator chose to throw the unsaved work away: no recovery offer for it at the next start.
        if (CurrentFilePath is null) _fileService.DeleteRecovery();
        return true;
    }

    // ── Auto-save ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps the work on disk while measuring, so an interruption (the app closing, a crash, a
    /// power cut) loses nothing: a project with a file is saved to it, a project without one to
    /// the recovery file that is offered at the next start. Called after every accepted reading.
    /// Best effort: a failure here must never interrupt the measurement.
    /// </summary>
    public async Task AutoSaveAsync()
    {
        var project = ActiveProject;
        if (project is null) return;

        await _autoSaveGate.WaitAsync();
        try
        {
            if (CurrentFilePath is { } path)
            {
                await _fileService.SaveToPathAsync(project, path);
                IsDirty = false;
                OnPropertyChanged(nameof(WindowTitle));
            }
            else
            {
                await _fileService.SaveRecoveryAsync(project);
            }
        }
        catch
        {
            // The explicit Save (and the unsaved-changes prompt on exit) still works.
        }
        finally
        {
            _autoSaveGate.Release();
        }
    }

    /// <summary>True for a project that was started and not finished: it can be resumed.</summary>
    private static MeasurementSession? FindUnfinished(Project project) =>
        project.Measurements.LastOrDefault(m => m.IsUnfinished());

    private async Task<bool> AskResumeAsync(MeasurementSession session)
    {
        if (_windowContext.XamlRoot is null) return false;

        var dialog = new ContentDialog
        {
            Title             = "Resume the unfinished measurement?",
            Content           = $"\u201c{session.Label}\u201d has {session.ReadingCount()} of " +
                                $"{session.InitialRound.Steps.Count} readings. Continue it where it stopped?",
            PrimaryButtonText = "Resume",
            CloseButtonText   = "Not now",
            DefaultButton     = ContentDialogButton.Primary,
            XamlRoot          = _windowContext.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ── Post-load calculation ─────────────────────────────────────────────────

    /// <summary>
    /// True when <paramref name="result"/> has to be (re)calculated: it is missing, or it
    /// carries no node heights — which is what an older file deserialises to (it stored a
    /// <c>heightMapMm</c> grid instead of <c>nodeHeights</c>), and that leaves the 3D
    /// surface plot empty.
    /// </summary>
    private static bool NeedsCalculation(SurfaceResult? result) =>
        result is null || result.NodeHeights.Count == 0;

    /// <summary>
    /// Runs the calculator for any round whose steps are fully populated but
    /// whose result is missing or unusable — supports files saved before result
    /// serialisation was enforced, older file layouts and externally produced files.
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

        if (NeedsCalculation(initialRound.Result) &&
            initialRound.Steps.Count > 0 &&
            initialRound.Steps.All(s => s.Reading.HasValue))
        {
            initialRound.Result = await Task.Run(() =>
                calculator.Calculate(initialRound.Steps, def, parameters));
        }

        if (!initialRound.Steps.All(s => s.Reading.HasValue)) return;

        foreach (var correction in session.Corrections)
        {
            if (!NeedsCalculation(correction.Result)) continue;
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
                _fileService.DeleteRecovery();
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
        _fileService.DeleteRecovery();
        IsDirty         = false;
        OnPropertyChanged(nameof(WindowTitle));
        _logger.Log("File.SaveAs", CurrentFilePath);
        return true;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        if (_windowContext.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title           = title,
            Content         = message,
            CloseButtonText = "OK",
            XamlRoot        = _windowContext.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
