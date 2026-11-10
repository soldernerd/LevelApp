using CommunityToolkit.Mvvm.ComponentModel;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Diagnostics;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>One converter channel's card on the diagnostics screen.</summary>
public sealed partial class SignalChannelCard(int adcChannel, string name) : ObservableObject
{
    /// <summary>ADC channel index ch0..ch3 (the index into the SIGNAL_DIAG arrays).</summary>
    public int AdcChannel { get; } = adcChannel;

    /// <summary>Signal name: A, B, S1 or S2.</summary>
    public string Name { get; } = name;

    public string AdcText { get; } = DiagnosticsMath.FormatAdc(adcChannel);

    [ObservableProperty] private string _pgaText = "";
    [ObservableProperty] private string _rmsText = "—";
    [ObservableProperty] private string _p2pText = "—";
    [ObservableProperty] private string _phaseText = "—";

    /// <summary>Calibrated phase (S1 and S2 only); empty when not available.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCalibratedPhase))]
    private string _calibratedPhaseText = "";

    /// <summary>RMS relative to the strongest channel, 0…1.</summary>
    [ObservableProperty] private double _rmsFraction;

    public bool HasCalibratedPhase => CalibratedPhaseText.Length > 0;
}

/// <summary>
/// The improved diagnostics screen: RMS, peak-to-peak and phase of A, B, S1, S2 (display
/// order) with ADC channel and PGA gain, the calibrated phase of S1/S2 and the theoretical
/// tilt. SIGNAL_DIAG (500 ms) is subscribed only while a session is attached AND
/// <see cref="IsActive"/>; the PGA gains and stored phases are read once per activation.
/// </summary>
public sealed partial class DiagnosticsViewModel : SessionViewModelBase
{
    private const string NotConnectedText = "Connect to the instrument to see the diagnostics.";

    private IDisposable? _sub;
    private int _generation;
    private string? _error;
    private SignalDiag? _latest;
    private long?[] _pga = new long?[4];
    private long?[] _storedPhase = new long?[2];

    /// <summary>The four channel cards in display order: A, B, S1, S2.</summary>
    public IReadOnlyList<SignalChannelCard> Channels { get; } =
        SignalDiag.DisplayOrder.Select(i => new SignalChannelCard(i, SignalDiag.ChannelNames[i])).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private bool _hasSample;

    [ObservableProperty] private string _theoreticalTiltS1Text = "—";
    [ObservableProperty] private string _theoreticalTiltS2Text = "—";

    /// <summary>Why nothing is shown (not connected, waiting, no firmware support); empty once data flows.</summary>
    [ObservableProperty]
    private string _statusText = NotConnectedText;

    /// <summary>
    /// Set by the host from the tab selection: true while the screen is showing. Only then is
    /// the signal subscription open.
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    public bool HasData => HasSample;

    partial void OnIsActiveChanged(bool value) => Sync();

    protected override void OnAttached(LeveltronicSession session) => Sync();

    protected override void OnDetached()
    {
        StopSubscription();
        _error = null;
        ClearSample();
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
                        _latest = d;
                        HasSample = true;
                        Refresh();
                        RefreshStatus();
                    }),
                    ex => Post(() =>
                    {
                        if (gen != _generation) return;
                        _error = ex is ApiException { Status: ApiStatus.UnknownResource }
                            ? "This firmware has no signal diagnostics."
                            : "Could not read the diagnostics.";
                        RefreshStatus();
                    }));
                _ = LoadStaticValuesAsync(s, gen);
            }
        }
        else
        {
            StopSubscription();
            ClearSample();
        }
        RefreshStatus();
    }

    private void StopSubscription()
    {
        _generation++;
        _sub?.Dispose();
        _sub = null;
    }

    private void ClearSample()
    {
        _latest = null;
        HasSample = false;
        _pga = new long?[4];
        _storedPhase = new long?[2];
        foreach (var c in Channels)
        {
            c.PgaText = "";
            c.RmsText = c.P2pText = c.PhaseText = "—";
            c.CalibratedPhaseText = "";
            c.RmsFraction = 0;
        }
        TheoreticalTiltS1Text = TheoreticalTiltS2Text = "—";
    }

    /// <summary>Reads the PGA of all four channels and the stored S1/S2 phases once.</summary>
    private async Task LoadStaticValuesAsync(LeveltronicSession s, int gen)
    {
        var pga = new long?[4];
        var phase = new long?[2];
        for (int i = 0; i < 4; i++) pga[i] = await TryReadAsync(s, ValueCatalog.ChannelPga[i]).ConfigureAwait(false);
        for (int i = 0; i < 2; i++) phase[i] = await TryReadAsync(s, ValueCatalog.SensorPhase[i]).ConfigureAwait(false);
        Post(() =>
        {
            if (gen != _generation) return;
            _pga = pga;
            _storedPhase = phase;
            Refresh();
        });
    }

    /// <summary>The value, or null where the firmware cannot tell (unknown resource, link error).</summary>
    private static async Task<long?> TryReadAsync(LeveltronicSession s, ValueDef def)
    {
        try { return await s.Api.TryReadValueAsync(def).ConfigureAwait(false); }
        catch (Exception) { return null; }
    }

    /// <summary>Pushes the latest sample and the static values into the cards.</summary>
    private void Refresh()
    {
        foreach (var c in Channels)
            c.PgaText = DiagnosticsMath.FormatPga(_pga[c.AdcChannel]);

        if (_latest is not { } d) return;

        double maxRms = d.RmsMv.Max();
        foreach (var c in Channels)
        {
            int i = c.AdcChannel;
            c.RmsText = DiagnosticsMath.FormatMv(d.RmsMv[i]);
            c.P2pText = DiagnosticsMath.FormatMv(d.P2pMv[i]);
            c.PhaseText = DiagnosticsMath.FormatSigned(d.PhaseDeg[i], 1) + "°";
            c.RmsFraction = DiagnosticsMath.RmsFraction(d.RmsMv[i], maxRms);

            double? cal = DiagnosticsMath.SensorIndex(c.Name) is { } k
                ? DiagnosticsMath.CalibratedPhase(d.PhaseDeg[i], _storedPhase[k])
                : null;
            c.CalibratedPhaseText = cal is { } v ? DiagnosticsMath.FormatSigned(v, 1) + "°" : "";
        }

        TheoreticalTiltS1Text = DiagnosticsMath.FormatTilt(d.TheoreticalTiltS1) + " mm/m";
        TheoreticalTiltS2Text = DiagnosticsMath.FormatTilt(d.TheoreticalTiltS2) + " mm/m";
    }

    private void RefreshStatus()
    {
        StatusText = !IsConnected ? NotConnectedText
            : HasSample ? ""
            : _error ?? "Waiting for signals…";
    }
}
