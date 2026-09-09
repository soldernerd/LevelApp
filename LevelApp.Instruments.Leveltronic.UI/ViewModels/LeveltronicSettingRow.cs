using CommunityToolkit.Mvvm.ComponentModel;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>One editable Settings resource row in the management view.</summary>
public sealed partial class LeveltronicSettingRow : ObservableObject
{
    public LeveltronicApi.SettingDescriptor Descriptor { get; }

    public string Group => Descriptor.Group;
    public string Field => Descriptor.Field;
    public string Range => $"{Descriptor.Min}…{Descriptor.Max}";

    /// <summary>Last value read from the device, or <see langword="null"/> if never read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceValueText))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private long? _deviceValue;

    /// <summary>Value bound to the editor; written back on demand.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private double _editValue;

    public string DeviceValueText => DeviceValue?.ToString() ?? "—";

    public bool IsDirty => DeviceValue is null || (long)EditValue != DeviceValue;

    public double Minimum => Descriptor.Min;
    public double Maximum => Descriptor.Max;

    public LeveltronicSettingRow(LeveltronicApi.SettingDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public void SetFromDevice(long value)
    {
        DeviceValue = value;
        EditValue = value;
    }
}
