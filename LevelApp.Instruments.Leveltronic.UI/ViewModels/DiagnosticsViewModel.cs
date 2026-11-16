using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>One line of the noise table: S1, S2 or their difference.</summary>
public sealed partial class NoiseRow(string name, int? sensor) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>0 = S1, 1 = S2, null for the difference (which has no quadrature / floor figures).</summary>
    public int? Sensor { get; } = sensor;

    [ObservableProperty] private string _sigmaText = "—";
    [ObservableProperty] private string _p2pText = "—";
    [ObservableProperty] private string _quadText = "—";
    [ObservableProperty] private string _ratioText = "—";

    /// <summary>"Steady", "Doubtful", "Learning" or empty.</summary>
    [ObservableProperty] private string _verdictText = "";

    /// <summary>The firmware would flag this sensor's reading doubtful right now.</summary>
    [ObservableProperty] private bool _isDoubtful;

    /// <summary>The quiet floor of this sensor ("floor 0.20 µm/m"), empty for the difference.</summary>
    [ObservableProperty] private string _floorText = "";

    public void Clear()
    {
        SigmaText = P2pText = QuadText = RatioText = "—";
        VerdictText = FloorText = "";
        IsDoubtful = false;
    }
}

/// <summary>
/// The improved diagnostics screen: RMS, peak-to-peak and phase of A, B, S1, S2 (display
/// order) with ADC channel and PGA gain, the calibrated phase of S1/S2 and the theoretical
/// tilt, plus (API 3.2) the noise figures of the raw batch readings. SIGNAL_DIAG (500 ms)
/// and NOISE (1 s) are subscribed only while a session is attached AND <see cref="IsActive"/>;
/// the PGA gains and stored phases are read once per activation.
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

    // ── Noise (API 3.2) ──────────────────────────────────────────────────────

    private IDisposable? _noiseSub;
    private string? _noiseError;

    /// <summary>The noise table in display order: S1, S2, Diff.</summary>
    public IReadOnlyList<NoiseRow> NoiseRows { get; } =
        [new NoiseRow("S1", 0), new NoiseRow("S2", 1), new NoiseRow("Diff (S1 − S2)", null)];

    /// <summary>The instrument offers noise figures (API 3.2 or newer) and a session is attached.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoiseNoteVisible))]
    private bool _noiseSupported;

    /// <summary>A noise sample has arrived (the window may still be filling).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoiseTable))]
    private bool _hasNoiseSample;

    /// <summary>The 10 s window is full, so the figures can be trusted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoiseTable))]
    private bool _noiseValid;

    [ObservableProperty] private string _noiseWindowText = "";
    [ObservableProperty] private double _noiseWindowFraction;

    /// <summary>"Quiet floors established" or the hint that they are being learned.</summary>
    [ObservableProperty] private string _noiseFloorsText = "";

    /// <summary>Why there are no noise figures (old firmware, error), or a note to the operator; empty when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoiseNoteVisible))]
    private string _noiseNote = "";

    public bool ShowNoiseTable => HasNoiseSample && NoiseValid;
    public bool NoiseNoteVisible => NoiseNote.Length > 0;

    /// <summary>Restarts the instrument's noise window ("noise since I touched the table").</summary>
    [RelayCommand]
    private async Task RestartNoiseWindowAsync()
    {
        if (Session is not { } s || !NoiseSupported) return;
        try
        {
            await s.Api.NoiseResetAsync().ConfigureAwait(false);
        }
        catch (ApiException ex) { Post(() => NoiseNote = ex.UserMessage); }
        catch (LinkException ex) { Post(() => NoiseNote = ex.Message); }
    }

    private void ApplyNoise(NoiseFigures n)
    {
        HasNoiseSample = true;
        NoiseValid = n.Valid;
        NoiseWindowText = DiagnosticsMath.FormatWindow(n.Valid, n.WindowBatches, NoiseFigures.FullWindowBatches);
        NoiseWindowFraction = DiagnosticsMath.WindowFraction(n.Valid, n.WindowBatches, NoiseFigures.FullWindowBatches);
        NoiseFloorsText = n.FloorsReady
            ? "Quiet floors established."
            : "Learning the quiet floors — keep the instrument still on a quiet table. The doubtful test starts afterwards.";
        NoiseNote = "";

        foreach (var row in NoiseRows)
        {
            if (!n.Valid) { row.Clear(); continue; }

            if (row.Sensor is not { } i)
            {
                row.SigmaText = DiagnosticsMath.FormatNoise(n.Sigma[2]);
                row.P2pText = DiagnosticsMath.FormatNoise(n.P2p[2]);
                continue;
            }

            row.SigmaText = DiagnosticsMath.FormatNoise(n.Sigma[i]);
            row.P2pText = DiagnosticsMath.FormatNoise(n.P2p[i]);
            row.QuadText = DiagnosticsMath.FormatNoise(n.Quad[i]);
            var ratio = n.StepOverFloor(i);
            row.RatioText = DiagnosticsMath.FormatRatio(ratio);
            row.FloorText = n.FloorsReady ? "floor " + DiagnosticsMath.FormatNoise(n.Floor[i]) : "";
            row.IsDoubtful = n.IsDoubtful(i);
            row.VerdictText = ratio is null ? "Learning" : row.IsDoubtful ? "Doubtful" : "Steady";
        }
    }

    private void StartNoise(LeveltronicSession s)
    {
        if (!s.Identity.AtLeast(3, 2))
        {
            NoiseSupported = false;
            NoiseNote = $"Noise figures need firmware 0.15.1 or newer (API 3.2); this instrument reports API {s.Identity.ApiVersionText}.";
            return;
        }

        NoiseSupported = true;
        NoiseNote = "";
        if (_noiseSub is not null) return;

        int gen = _generation;
        _noiseSub = s.Api.SubscribeNoise(
            n => Post(() =>
            {
                if (gen != _generation) return;
                ApplyNoise(n);
            }),
            ex => Post(() =>
            {
                if (gen != _generation) return;
                _noiseError = ex is ApiException { Status: ApiStatus.UnknownResource }
                    ? "This firmware has no noise figures."
                    : "Could not read the noise figures.";
                NoiseNote = _noiseError;
            }));
    }

    private void StopNoise()
    {
        _noiseSub?.Dispose();
        _noiseSub = null;
    }

    private void ClearNoise()
    {
        HasNoiseSample = false;
        NoiseValid = false;
        NoiseWindowText = NoiseFloorsText = "";
        NoiseWindowFraction = 0;
        foreach (var r in NoiseRows) r.Clear();
    }

    partial void OnIsActiveChanged(bool value) => Sync();

    protected override void OnAttached(LeveltronicSession session) => Sync();

    protected override void OnDetached()
    {
        StopSubscription();
        _error = null;
        ClearSample();
        StatusText = NotConnectedText;
        NoiseSupported = false;
        NoiseNote = "";
    }

    private void Sync()
    {
        if (IsActive && Session is { } s)
        {
            if (_sub is null)
            {
                _error = null;
                int gen = ++_generation;
                StartNoise(s);
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
        StopNoise();
    }

    private void ClearSample()
    {
        _latest = null;
        HasSample = false;
        ClearNoise();
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
