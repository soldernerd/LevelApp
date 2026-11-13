using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.Core.Instruments;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.Calibration;

/// <summary>
/// The calibration entry — the phone app's <c>CalibrationHubScreen</c>: the three wizards
/// in the order they should be run (phase, gain, zero), a service-mode card, and the
/// wizard currently shown. Owns one lease on the instrument's shared session.
/// </summary>
public sealed partial class CalibrationHubViewModel : ObservableObject, IAsyncDisposable
{
    private readonly LeveltronicSessionManager _sessions;
    private readonly ILeveltronicPreferences _prefs;
    private readonly KnownDevice _device;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private SessionLease? _lease;
    private IDisposable? _keepAlive;

    public CalibrationHubViewModel(LeveltronicSessionManager sessions, ILeveltronicPreferences prefs, KnownDevice device)
    {
        _sessions = sessions;
        _prefs = prefs;
        _device = device;
        DeviceName = device.DisplayName;
    }

    [ObservableProperty] private string _deviceName;
    [ObservableProperty] private string _statusMessage = "Connecting…";
    [ObservableProperty] private bool _statusIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHub))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceTitle))]
    [NotifyPropertyChangedFor(nameof(ServiceText))]
    [NotifyPropertyChangedFor(nameof(ServiceAccent))]
    [NotifyPropertyChangedFor(nameof(ServiceGlyph))]
    private bool _serviceMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHub))]
    [NotifyPropertyChangedFor(nameof(ShowWizard))]
    private WizardViewModelBase? _activeWizard;

    public bool ShowHub => IsConnected && ActiveWizard is null;
    public bool ShowWizard => ActiveWizard is not null;
    public bool ShowRetry => !IsConnected;

    /// <summary>Green while service mode is on, amber while it is off (the phone app's card).</summary>
    public Microsoft.UI.Xaml.Media.Brush ServiceAccent => new Microsoft.UI.Xaml.Media.SolidColorBrush(
        ServiceMode
            ? Windows.UI.Color.FromArgb(255, 0x43, 0xA0, 0x47)
            : Windows.UI.Color.FromArgb(255, 0xFF, 0xA0, 0x00));

    /// <summary>Open padlock while service mode is on, closed while it is off.</summary>
    public string ServiceGlyph => ServiceMode ? "\uE785" : "\uE72E";

    public string ServiceTitle => ServiceMode ? "Service mode is on" : "Service mode is off";
    public string ServiceText => ServiceMode
        ? "Calibration values can be written. It ends by itself 10 minutes after the last request, or when the instrument sleeps."
        : "Switch it on at the instrument: SETTINGS > Service mode, press the right knob twice. This screen continues by itself.";

    private LeveltronicSession? Session => _lease?.Session;

    /// <summary>Opens the session (call when the view loads).</summary>
    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (_lease is not null) return;
        Report("Connecting…", false);
        try
        {
            var lease = await _sessions.AcquireAsync(_device);
            _lease = lease;
            var s = lease.Session;
            s.Closed += OnSessionClosed;
            s.Feed.Updated += OnFeedUpdated;
            // Service mode ends after 10 minutes without a request: keep it alive while this is open.
            _keepAlive = s.KeepServiceModeAlive();
            Post(() =>
            {
                ServiceMode = s.Feed.State.ServiceMode;
                IsConnected = true;
                Report($"Connected to {_device.DisplayName} · {s.Serial}", false);
            });
        }
        catch (Exception ex)
        {
            Report(ex is ApiException api ? api.UserMessage : ex.Message, true);
        }
    }

    [RelayCommand] private void OpenPhase() => Open(s => new PhaseCalViewModel(s));
    [RelayCommand] private void OpenGain() => Open(s => new GainCalViewModel(s, _prefs));
    [RelayCommand] private void OpenFlip() => Open(s => new FlipCalViewModel(s));

    private void Open(Func<LeveltronicSession, WizardViewModelBase> create)
    {
        if (Session is not { } s || ActiveWizard is not null) return;
        var wizard = create(s);
        wizard.CloseRequested += (_, _) => CloseWizard();
        ActiveWizard = wizard;
    }

    /// <summary>Back to the hub (the wizard's own Done, or the Back link).</summary>
    [RelayCommand]
    public void CloseWizard() => Post(() =>
    {
        var w = ActiveWizard;
        ActiveWizard = null;
        w?.Dispose();
    });

    [RelayCommand]
    private async Task LeaveServiceModeAsync()
    {
        if (Session is not { } s) return;
        try { await s.Api.ServiceEndAsync(); }
        catch (Exception ex) when (ex is ApiException or LinkException) { Report(ex.Message, true); }
    }

    private void OnFeedUpdated(object? sender, InstrumentSnapshot e) => Post(() => ServiceMode = e.ServiceMode);

    private void OnSessionClosed(object? sender, EventArgs e) => Post(() =>
    {
        CloseWizard();
        _ = ReleaseAsync();
        Report("Lost contact with the instrument.", true);
    });

    private async Task ReleaseAsync()
    {
        _keepAlive?.Dispose();
        _keepAlive = null;
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is not null)
        {
            lease.Session.Closed -= OnSessionClosed;
            lease.Session.Feed.Updated -= OnFeedUpdated;
            await lease.DisposeAsync();
        }
        Post(() =>
        {
            IsConnected = false;
            ServiceMode = false;
        });
    }

    private void Report(string message, bool error) => Post(() =>
    {
        StatusMessage = message;
        StatusIsError = error;
    });

    private void Post(Action a)
    {
        if (_ui is null || ReferenceEquals(SynchronizationContext.Current, _ui)) a();
        else _ui.Post(_ => a(), null);
    }

    public async ValueTask DisposeAsync()
    {
        ActiveWizard?.Dispose();
        await ReleaseAsync();
    }
}
