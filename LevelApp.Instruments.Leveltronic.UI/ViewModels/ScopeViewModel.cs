using CommunityToolkit.Mvvm.ComponentModel;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Diagnostics;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>
/// The oscilloscope screen: feeds the latest SIGNAL_DIAG sample (amplitude and phase of
/// A, B, S1, S2) to the view, which redraws the traces. SIGNAL_DIAG (500 ms) is subscribed
/// only while a session is attached AND <see cref="IsActive"/> (the screen is showing).
/// </summary>
public sealed partial class ScopeViewModel : SessionViewModelBase
{
    private const string NotConnectedText = "Connect to the instrument to see the signals.";

    private IDisposable? _sub;
    private int _generation;
    private string? _error;

    /// <summary>The most recent signal sample, or null while none has arrived.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private SignalDiag? _latest;

    /// <summary>Index into <see cref="ScopeMath.ScalesMv"/> of the sensor panel's full scale.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SensorFullScaleMv))]
    private int _scaleIndex;

    /// <summary>Why nothing is drawn (not connected, waiting, no firmware support); empty once data flows.</summary>
    [ObservableProperty]
    private string _statusText = NotConnectedText;

    /// <summary>
    /// Set by the host from the tab selection: true while the screen is showing. Only then is
    /// the signal subscription open.
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    public bool HasData => Latest is not null;

    /// <summary>Entries of the scale menu: "S1/S2 ±1200 mV" ….</summary>
    public IReadOnlyList<string> ScaleItems { get; } =
        ScopeMath.ScalesMv.Select(ScopeMath.ScaleItemText).ToList();

    /// <summary>Full scale of the sensor panel (S1, S2), ± mV.</summary>
    public double SensorFullScaleMv =>
        ScopeMath.ScalesMv[Math.Clamp(ScaleIndex, 0, ScopeMath.ScalesMv.Count - 1)];

    partial void OnIsActiveChanged(bool value) => Sync();

    protected override void OnAttached(LeveltronicSession session) => Sync();

    protected override void OnDetached()
    {
        StopSubscription();
        _error = null;
        Latest = null;
        StatusText = NotConnectedText;
    }

    private void Sync()
    {
        if (IsActive && Session is { } s)
        {
            if (_sub is null)
            {
                _error = null;
                int gen = ++_generation;
                _sub = s.Api.SubscribeSignalDiag(
                    d => Post(() =>
                    {
                        if (gen != _generation) return;
                        Latest = d;
                        RefreshStatus();
                    }),
                    ex => Post(() =>
                    {
                        if (gen != _generation) return;
                        _error = ex is ApiException { Status: ApiStatus.UnknownResource }
                            ? "This firmware has no signal diagnostics."
                            : "Could not read the signals.";
                        RefreshStatus();
                    }));
            }
        }
        else
        {
            StopSubscription();
            Latest = null;
        }
        RefreshStatus();
    }

    private void StopSubscription()
    {
        _generation++;
        _sub?.Dispose();
        _sub = null;
    }

    private void RefreshStatus()
    {
        StatusText = !IsConnected ? NotConnectedText
            : Latest is not null ? ""
            : _error ?? "Waiting for signals…";
    }
}
