using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Services;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;

namespace LevelApp.App.ViewModels;

/// <summary>
/// The instrument half of the reading entry, shared by the guided measurement and
/// the correction session: connects the step's <see cref="IInstrumentProvider"/>
/// and — when it is an <see cref="ILiveReadingSource"/> — shows its live value,
/// lets the operator pick the channel, take a settled measurement into the
/// reading box and zero the difference. Providers without live readings (manual
/// entry) leave <see cref="IsAvailable"/> false and the panel hidden.
/// </summary>
public sealed partial class InstrumentReadingViewModel : ObservableObject
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly ILocalisationService _loc;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private IInstrumentProvider? _provider;
    private ILiveReadingSource? _source;
    private CancellationTokenSource? _cts;
    private bool _syncingChannel;

    public InstrumentReadingViewModel(ILocalisationService loc) => _loc = loc;

    /// <summary>A settled measurement finished; the value is in µm/m, ready for the reading box.</summary>
    public event EventHandler<double>? ReadingTaken;

    /// <summary>
    /// A settled measurement finished from "Take reading and accept": the host puts the value
    /// (µm/m) in the reading box and accepts it at once.
    /// </summary>
    public event EventHandler<double>? ReadingAccepted;

    /// <summary>Raised when the provider's connection state changed (the host refreshes its status bar).</summary>
    public event EventHandler? ConnectionChanged;

    public ObservableCollection<ReadingChannel> Channels { get; } = [];

    [ObservableProperty] private bool _isAvailable;

    /// <summary>The host can accept a reading straight away; shows the "Take reading and accept" button.</summary>
    [ObservableProperty] private bool _autoAcceptEnabled;
    [ObservableProperty] private ReadingChannel? _selectedChannel;
    [ObservableProperty] private string _liveText = "—";
    [ObservableProperty] private bool _isDoubtful;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;
    [ObservableProperty] private bool _supportsZero;
    [ObservableProperty] private bool _isZeroed;
    [ObservableProperty] private string _zeroText = string.Empty;
    [ObservableProperty] private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanZero))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanZero))]
    private bool _isConnected;

    /// <summary>Why the last connection attempt failed, in words for the operator; null if it did not.</summary>
    public string? ConnectionError { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public bool CanAct => IsAvailable && IsConnected && !IsBusy;
    public bool CanZero => CanAct && SupportsZero;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>Takes over <paramref name="provider"/>: wires its live source and connects it.</summary>
    public async Task AttachAsync(IInstrumentProvider provider)
    {
        await DetachAsync().ConfigureAwait(false);

        _provider = provider;
        _provider.ConnectionStateChanged += OnProviderStateChanged;

        _source = provider as ILiveReadingSource;
        Post(() =>
        {
            IsAvailable = _source is not null;
            Channels.Clear();
            if (_source is not null)
            {
                foreach (var c in _source.Channels) Channels.Add(c);
                _source.LiveReadingChanged += OnLiveChanged;
            }
            Refresh();
        });

        await ConnectAsync().ConfigureAwait(false);
    }

    /// <summary>Retry after a failed or lost connection.</summary>
    [RelayCommand]
    private Task RetryAsync() => ConnectAsync();

    private async Task ConnectAsync()
    {
        var provider = _provider;
        if (provider is null || provider.ConnectionState == InstrumentConnectionState.Connected)
        {
            Post(Refresh);
            return;
        }
        try
        {
            await provider.ConnectAsync().ConfigureAwait(false);
            ConnectionError = null;
        }
        catch (Exception ex)
        {
            ConnectionError = ex.Message;
        }
        Post(() =>
        {
            Refresh();
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Stops any measurement, releases the instrument.</summary>
    public async Task DetachAsync()
    {
        _cts?.Cancel();
        var provider = _provider;
        var source = _source;
        _provider = null;
        _source = null;

        if (source is not null) source.LiveReadingChanged -= OnLiveChanged;
        if (provider is not null)
        {
            provider.ConnectionStateChanged -= OnProviderStateChanged;
            try { await provider.DisconnectAsync().ConfigureAwait(false); } catch { /* going away anyway */ }
        }
        Post(() =>
        {
            IsAvailable = false;
            IsConnected = false;
            Channels.Clear();
        });
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task TakeReadingAsync()
    {
        if (_source is null || IsBusy) return;
        await RunAsync(async (src, progress, ct) =>
        {
            double v = await src.MeasureAsync(progress, ct);
            Post(() => ReadingTaken?.Invoke(this, v));
        });
    }

    [RelayCommand]
    private async Task TakeAndAcceptAsync()
    {
        if (_source is null || IsBusy) return;
        await RunAsync(async (src, progress, ct) =>
        {
            double v = await src.MeasureAsync(progress, ct);
            Post(() => ReadingAccepted?.Invoke(this, v));
        });
    }

    [RelayCommand]
    private async Task ZeroAsync()
    {
        if (_source is null || IsBusy || !_source.SupportsZero) return;
        await RunAsync((src, progress, ct) => src.ZeroAsync(progress, ct));
    }

    [RelayCommand]
    private void ClearZero()
    {
        _source?.ClearZero();
        Refresh();
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunAsync(Func<ILiveReadingSource, IProgress<double>, CancellationToken, Task> action)
    {
        var src = _source!;
        _cts = new CancellationTokenSource();
        IsBusy = true;
        ErrorText = null;
        Progress = 0;
        StatusText = _loc.Get("Instrument_Measuring.Text");
        try
        {
            await action(src, new Progress<double>(p => Progress = p * 100), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // cancelled by the operator
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            Refresh();
        }
    }

    // ── Channel selection ────────────────────────────────────────────────────

    partial void OnSelectedChannelChanged(ReadingChannel? value)
    {
        if (_syncingChannel || value is null || _source is null) return;
        _source.SelectedChannelId = value.Id;
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    private void OnLiveChanged(object? sender, EventArgs e) => Post(Refresh);

    private void OnProviderStateChanged(object? sender, InstrumentConnectionState state)
    {
        Post(() =>
        {
            Refresh();
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Refresh()
    {
        var provider = _provider;
        IsConnected = provider?.ConnectionState == InstrumentConnectionState.Connected;

        var src = _source;
        if (src is null) return;

        _syncingChannel = true;
        SelectedChannel = Channels.FirstOrDefault(c => c.Id == src.SelectedChannelId);
        _syncingChannel = false;

        SupportsZero = src.SupportsZero;
        IsZeroed = src.IsZeroed;
        ZeroText = src.ZeroOffsetMicronsPerMetre is { } z
            ? string.Format(Inv, _loc.Get("Instrument_ZeroedAt.Text"), Format(z))
            : string.Empty;
        OnPropertyChanged(nameof(CanZero));

        if (IsBusy) return;

        var live = src.Latest;
        if (live is null || !live.IsValid)
        {
            LiveText = "—";
            IsDoubtful = false;
            StatusText = IsConnected ? _loc.Get("Instrument_NoReading.Text") : string.Empty;
            return;
        }
        LiveText = $"{Format(live.ValueMicronsPerMetre)} µm/m";
        IsDoubtful = live.IsDoubtful;
        StatusText = live.IsDoubtful ? _loc.Get("Instrument_Unsteady.Text") : string.Empty;
    }

    private static string Format(double microns) => microns.ToString("+0.0;-0.0;0.0", Inv);

    private void Post(Action a)
    {
        if (_ui is null || ReferenceEquals(SynchronizationContext.Current, _ui)) a();
        else _ui.Post(_ => a(), null);
    }
}
