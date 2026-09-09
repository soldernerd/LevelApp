using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Instruments.Leveltronic.Protocol;
using LevelApp.Instruments.Leveltronic.Transport;
using LevelApp.Instruments.UsbHid.Dfu;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// <see cref="IFirmwareUpdater"/> for the Leveltronic, over USB DFU.
/// <para>
/// <b>Recovery caveat.</b> The firmware's "reboot to DFU" (<c>EXECUTE Commands
/// 0x05</c>) works by programming the <c>nBOOT0</c> option byte to 0 and reloading
/// the option bytes, so the STM32 ROM bootloader is entered <i>and stays entered
/// on every subsequent boot</i>. A plain DFU download does not restore
/// <c>nBOOT0</c>, and <see cref="DfuSession"/> does not touch option bytes — so
/// after <see cref="PerformUpdateAsync"/> the device remains in the bootloader
/// until it is reflashed with <c>nBOOT0 = 1</c> restored (e.g.
/// <c>InclinationMeterFirmware/dfu_flash.ps1</c> or
/// <c>STM32_Programmer_CLI ... -ob nBOOT_SEL=1 nBOOT0=1</c>). A power cycle does
/// not recover it. See <c>InclinationMeterFirmware/docs/wp4_reboot_to_dfu.md</c>.
/// </para>
/// </summary>
public sealed class LeveltronicFirmwareUpdater : IFirmwareUpdater
{
    /// <summary>STM32G0 main-flash page size (bytes) for DFU_DNLOAD blocks.</summary>
    private const int Stm32G0PageSize = 2048;

    private readonly KnownDevice _device;
    private bool _isReady;

    public LeveltronicFirmwareUpdater(KnownDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>
    /// Supplies the firmware image to flash. Set by the caller (the UI wires a
    /// file picker) before <see cref="PerformUpdateAsync"/>. Returning
    /// <see langword="null"/> aborts the update.
    /// </summary>
    public Func<CancellationToken, Task<byte[]?>>? FirmwareImageProvider { get; set; }

    public TransportRequirement RequiredTransport => TransportRequirement.UsbOnly;

    public bool IsReady => _isReady;

    public event EventHandler? IsReadyChanged;

    public async Task<FirmwareInfo> GetCurrentFirmwareAsync(CancellationToken ct = default)
    {
        await using var link = new UsbHidLeveltronicLink(_device.TransportAddress);
        await link.ConnectAsync(ct).ConfigureAwait(false);
        await using var client = new LeveltronicDeviceClient(link);

        try
        {
            var id = await client.GetIdentityAsync(ct).ConfigureAwait(false);
            SetReady(true);
            return new FirmwareInfo(id.FirmwareVersion, ReleaseNotes: null, DownloadUrl: null);
        }
        catch
        {
            SetReady(false);
            throw;
        }
    }

    /// <summary>
    /// No update feed exists yet — always returns <see langword="null"/> ("up to
    /// date"). Wire a real source (bundled image version, GitHub release, …) when
    /// firmware distribution is decided.
    /// </summary>
    public Task<FirmwareInfo?> CheckForUpdateAsync(CancellationToken ct = default) =>
        Task.FromResult<FirmwareInfo?>(null);

    public async Task PerformUpdateAsync(IProgress<double> progress, CancellationToken ct)
    {
        if (FirmwareImageProvider is null)
            throw new InvalidOperationException(
                "No firmware image was selected (FirmwareImageProvider is not set).");

        byte[]? image = await FirmwareImageProvider(ct).ConfigureAwait(false);
        if (image is null || image.Length == 0)
            throw new OperationCanceledException("Firmware image selection was cancelled.");

        // 1. Command the device into the ROM bootloader.
        await using (var link = new UsbHidLeveltronicLink(_device.TransportAddress))
        {
            await link.ConnectAsync(ct).ConfigureAwait(false);
            await using var client = new LeveltronicDeviceClient(link);
            await client.RebootToDfuAsync(ct).ConfigureAwait(false);
        }
        SetReady(false);

        // 2. Wait for it to re-enumerate as an STM32 DFU device.
        string dfuId = await new DfuConnectionDetector()
            .WaitForDfuDeviceAsync(LeveltronicApi.DfuVendorId, LeveltronicApi.DfuProductId, ct)
            .ConfigureAwait(false);

        // 3. Download the image page by page. NOTE: leaves the device in the
        //    bootloader (nBOOT0 = 0) — see the class-level recovery caveat.
        using var dfu = new DfuSession(dfuId, Stm32G0PageSize);
        await dfu.FlashAsync(image, progress, ct).ConfigureAwait(false);
    }

    private void SetReady(bool value)
    {
        if (_isReady == value) return;
        _isReady = value;
        IsReadyChanged?.Invoke(this, EventArgs.Empty);
    }
}
