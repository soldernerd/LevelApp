using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

/// <summary>
/// Dialog that scans for nearby devices and allows the user to register
/// one as a <see cref="KnownDevice"/> in the <see cref="IDeviceRegistry"/>.
/// </summary>
public sealed partial class ScanForDevicesDialog : ContentDialog
{
    private readonly IInstrumentPlugin  _plugin;
    private readonly IDeviceRegistry    _registry;
    private readonly List<IDeviceScanner> _scanners;

    private CancellationTokenSource? _scanCts;

    /// <summary>Set after the dialog closes with a successful Add.</summary>
    public KnownDevice? AddedDevice { get; private set; }

    // Simple display DTO for the ListView
    private sealed record CandidateItem(DeviceCandidate Candidate)
    {
        public string DisplayName => Candidate.DisplayName;
        public string SignalText  => Candidate.SignalStrength.HasValue
            ? $"{Candidate.SignalStrength} dBm" : string.Empty;
    }

    public ScanForDevicesDialog(IInstrumentPlugin plugin, IDeviceRegistry registry)
    {
        _plugin   = plugin;
        _registry = registry;
        _scanners = plugin.CreateScanners().ToList();

        this.InitializeComponent();

        // Populate transport selector when there are multiple scanners
        if (_scanners.Count > 1)
        {
            foreach (var s in _scanners)
                TransportCombo.Items.Add(s.Transport.DisplayName);
            TransportCombo.SelectedIndex = 0;
            TransportRow.Visibility = Visibility.Visible;
        }
    }

    private async void OnScanClicked(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        CandidateList.Items.Clear();
        NoCandidatesText.Visibility = Visibility.Collapsed;
        ScanButton.IsEnabled        = false;
        ScanSpinner.IsActive        = true;
        ScanSpinner.Visibility      = Visibility.Visible;
        AddSelectedButton.IsEnabled = false;

        try
        {
            int scannerIndex = _scanners.Count > 1 ? TransportCombo.SelectedIndex : 0;
            if (scannerIndex < 0 || scannerIndex >= _scanners.Count) return;

            var scanner = _scanners[scannerIndex];
            int found   = 0;

            await foreach (var candidate in scanner.ScanAsync(
                               TimeSpan.FromSeconds(8), _scanCts.Token))
            {
                CandidateList.Items.Add(new CandidateItem(candidate));
                found++;
            }

            if (found == 0)
                NoCandidatesText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { /* user cancelled or new scan started */ }
        catch (Exception ex)
        {
            NoCandidatesText.Text       = $"Scan error: {ex.Message}";
            NoCandidatesText.Visibility = Visibility.Visible;
        }
        finally
        {
            ScanButton.IsEnabled   = true;
            ScanSpinner.IsActive   = false;
            ScanSpinner.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCandidateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AddSelectedButton.IsEnabled = CandidateList.SelectedItem is CandidateItem;
    }

    private void OnAddSelectedClicked(object sender, RoutedEventArgs e)
    {
        if (CandidateList.SelectedItem is not CandidateItem item) return;

        var candidate = item.Candidate;
        var device    = new KnownDevice(
            DeviceId:         Guid.NewGuid().ToString(),
            PluginId:         _plugin.PluginId,
            TransportId:      candidate.TransportId,
            DisplayName:      candidate.DisplayName,
            TransportAddress: candidate.CandidateId
        );

        _registry.RegisterDevice(device);
        AddedDevice = device;
        Hide();
    }

    // Clean up any in-progress scan when the dialog is closed via Cancel
    private new void Hide()
    {
        _scanCts?.Cancel();
        base.Hide();
    }
}
