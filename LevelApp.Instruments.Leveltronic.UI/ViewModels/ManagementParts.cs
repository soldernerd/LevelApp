using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>One label/value line of the Info tab.</summary>
public sealed partial class InfoRow(string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty] private string _value = "—";
}

/// <summary>A titled group of <see cref="InfoRow"/>s.</summary>
public sealed class InfoGroup(string title, params InfoRow[] rows)
{
    public string Title { get; } = title;
    public IReadOnlyList<InfoRow> Rows { get; } = rows;
}

/// <summary>One line of the Diagnostics tab: a converter channel's RMS, peak-to-peak and phase.</summary>
public sealed partial class DiagChannelRow(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty] private string _rms = "—";
    [ObservableProperty] private string _p2p = "—";
    [ObservableProperty] private string _phase = "—";
}

/// <summary>A one-shot action of the Actions tab.</summary>
public sealed record ActionItem(string Label, string Subtitle, IAsyncRelayCommand Command, bool Danger = false);

/// <summary>A titled group of value rows (Settings and Calibrations are grouped by <see cref="ValueDef.Group"/>).</summary>
public sealed class ValueGroup(string name, ObservableCollection<ValueRowViewModel> rows)
{
    public string Name { get; } = name;
    public ObservableCollection<ValueRowViewModel> Rows { get; } = rows;
}

/// <summary>
/// One editable Settings or Calibrations value: the device's current value, an edit
/// box, and read / write. Writes that need service mode say so when the device
/// refuses.
/// </summary>
public sealed partial class ValueRowViewModel : ObservableObject
{
    private readonly Func<LeveltronicSession?> _session;
    private readonly Action<string, bool> _report;

    public ValueRowViewModel(ValueDef def, Func<LeveltronicSession?> session, Action<string, bool> report)
    {
        Def = def;
        _session = session;
        _report = report;
    }

    public ValueDef Def { get; }

    public string Label => Def.Label;
    public string Description => Def.Description;
    public string Unit => Def.Unit;
    public string RangeText => $"{Def.Min}…{Def.Max}{(Def.Unit.Length > 0 ? " " + Def.Unit : "")}";
    public bool NeedsService => Def.Service;
    public double Minimum => Def.Min;
    public double Maximum => Def.Max;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceValueText))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private long? _deviceValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private double _editValue;

    /// <summary>False when an optional resource is not present in this firmware build (the row is hidden).</summary>
    [ObservableProperty] private bool _isAvailable = true;

    [ObservableProperty] private bool _isBusy;

    public string DeviceValueText => DeviceValue is { } v ? v.ToString(CultureInfo.InvariantCulture) : "—";
    public bool IsDirty => DeviceValue is null || (long)Math.Round(EditValue) != DeviceValue;

    /// <summary>Reads the current value (optional rows: hides the row when the firmware lacks it).</summary>
    public async Task LoadAsync()
    {
        if (_session() is not { } s) return;
        try
        {
            long? v = Def.Optional
                ? await s.Api.TryReadValueAsync(Def)
                : await s.Api.ReadValueAsync(Def);
            if (v is null) { IsAvailable = false; return; }
            DeviceValue = v;
            EditValue = v.Value;
        }
        catch (ApiException ex) { _report(ex.UserMessage, true); }
        catch (LinkException ex) { _report(ex.Message, true); }
    }

    [RelayCommand]
    private async Task ReadAsync()
    {
        IsBusy = true;
        try { await LoadAsync(); _report($"{Def.Label}: {DeviceValueText}", false); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task WriteAsync()
    {
        if (_session() is not { } s) return;
        IsBusy = true;
        try
        {
            await s.Api.WriteValueAsync(Def, (long)Math.Round(EditValue));
            long back = await s.Api.ReadValueAsync(Def);
            DeviceValue = back;
            EditValue = back;
            _report($"{Def.Label}: saved ({back}{(Def.Unit.Length > 0 ? " " + Def.Unit : "")})", false);
        }
        catch (ApiException ex) { _report(ex.UserMessage, true); }
        catch (LinkException ex) { _report(ex.Message, true); }
        finally { IsBusy = false; }
    }
}
