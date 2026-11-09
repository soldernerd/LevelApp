using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>Typed operations, the live feed and the session open checks.</summary>
public sealed class ApiAndFeedTests
{
    // ── Session open ────────────────────────────────────────────────────────

    [Fact]
    public async Task Open_ReadsIdentity()
    {
        await using var rig = await SimRig.OpenAsync();
        Assert.Equal("1.0.0", rig.Session.Identity.Firmware);
        Assert.Equal("6DA08073", rig.Session.Serial);
    }

    [Fact]
    public async Task Open_OldFirmware_IsIncompatible()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var sim = new SimulatedLeveltronicLink(time, apiVersion: 2);

        var ex = await Assert.ThrowsAsync<IncompatibleFirmwareException>(
            () => LeveltronicSession.OpenAsync(TestKit.Device(), sim, time));

        Assert.Equal(2, ex.ApiVersion);
        Assert.Contains("needs API v3", ex.Message);
    }

    [Fact]
    public async Task Open_OtherProduct_IsUnsupported()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var sim = new SimulatedLeveltronicLink(time, product: "NeigMess2");

        var ex = await Assert.ThrowsAsync<UnsupportedDeviceException>(
            () => LeveltronicSession.OpenAsync(TestKit.Device(), sim, time));

        Assert.Contains("NeigMess2", ex.Message);
    }

    // ── System / values / commands ──────────────────────────────────────────

    [Fact]
    public async Task Rtc_SetThenGet()
    {
        await using var rig = await SimRig.OpenAsync();
        var when = new DateTime(2026, 11, 9, 10, 20, 30);

        await rig.Api.SetRtcAsync(when);
        var rtc = await rig.Api.RtcAsync();

        Assert.True(rtc.IsSet);
        Assert.Equal(when.Date, rtc.DateTime.Date);
        Assert.Equal(10, rtc.Hour);
    }

    [Fact]
    public async Task Settings_ReadAndWrite_NoServiceModeNeeded()
    {
        await using var rig = await SimRig.OpenAsync();
        var autoOff = ValueCatalog.Settings.Single(d => d.Id == 0x60);

        Assert.Equal(300, await rig.Api.ReadValueAsync(autoOff));
        await rig.Api.WriteValueAsync(autoOff, 900);
        Assert.Equal(900, await rig.Api.ReadValueAsync(autoOff));
    }

    [Fact]
    public async Task Settings_OutOfRangeAndCrossFieldAreRejected()
    {
        await using var rig = await SimRig.OpenAsync();
        var critical = ValueCatalog.Settings.Single(d => d.Id == 0x50);
        var low = ValueCatalog.Settings.Single(d => d.Id == 0x51);

        var range = await Assert.ThrowsAsync<ApiException>(() => rig.Api.WriteValueAsync(critical, 100));
        Assert.Equal(ApiStatus.InvalidParameter, range.Status);

        // critical must stay below low (3600)
        var cross = await Assert.ThrowsAsync<ApiException>(() => rig.Api.WriteValueAsync(critical, 3600));
        Assert.Equal(ApiStatus.InvalidParameter, cross.Status);
        await rig.Api.WriteValueAsync(low, 3800);
    }

    [Fact]
    public async Task OptionalSetting_IsNullWhenTheFirmwareLacksIt()
    {
        await using var rig = await SimRig.OpenAsync(t => new SimulatedLeveltronicLink(t) { SupportsChargeFullTimeout = false });
        var maxAt100 = ValueCatalog.Settings.Single(d => d.Id == 0x53);

        Assert.Null(await rig.Api.TryReadValueAsync(maxAt100));
        await Assert.ThrowsAsync<ApiException>(() => rig.Api.ReadValueAsync(maxAt100));
    }

    [Fact]
    public async Task CalibrationWrite_NeedsServiceMode()
    {
        await using var rig = await SimRig.OpenAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => rig.Api.WriteValueAsync(ValueCatalog.S1K, 47000));
        Assert.True(ex.IsServiceModeRequired);
        Assert.Contains("SETTINGS > Service mode", ex.UserMessage);

        rig.Sim.SetServiceMode(true);
        await rig.Api.WriteValueAsync(ValueCatalog.S1K, 47000);
        Assert.Equal(47000, await rig.Api.ReadValueAsync(ValueCatalog.S1K));
    }

    [Fact]
    public async Task ServiceMode_EndsWithServiceEnd_AndAfterTenIdleMinutes()
    {
        await using var rig = await SimRig.OpenAsync();
        rig.Sim.SetServiceMode(true);
        Assert.True((await rig.Api.StateAsync()).ServiceMode);

        await rig.Api.ServiceEndAsync();
        Assert.False((await rig.Api.StateAsync()).ServiceMode);

        rig.Sim.SetServiceMode(true);
        rig.Time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        Assert.False((await rig.Api.StateAsync()).ServiceMode);
    }

    [Fact]
    public async Task ServiceKeepAlive_SendsARequestEvery30Seconds()
    {
        await using var rig = await SimRig.OpenAsync();
        rig.Sim.SetServiceMode(true);
        using var keepAlive = rig.Session.KeepServiceModeAlive();

        // 9 simulated minutes would not end it either way; the keep-alive pushes the 10 min out.
        await TestKit.AdvanceAsync(rig.Time, TimeSpan.FromMinutes(25), TimeSpan.FromSeconds(15));

        Assert.True(rig.Sim.ServiceMode);
    }

    [Fact]
    public async Task Commands_ReachTheDevice()
    {
        await using var rig = await SimRig.OpenAsync();
        rig.Sim.SetServiceMode(false);

        await rig.Api.TestBeepAsync();
        await rig.Api.SetChargeInhibitAsync(true);
        Assert.True((await rig.Api.StateAsync()).ChargeInhibited);

        var ex = await Assert.ThrowsAsync<ApiException>(() => rig.Api.RebootToDfuAsync());
        Assert.True(ex.IsServiceModeRequired);
    }

    // ── Live feed ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LiveFeed_SeedsAllThreeParts_ThenKeepsUpdating()
    {
        await using var rig = await SimRig.OpenAsync();
        await TestKit.AdvanceUntilAsync(rig.Time, () =>
            rig.Session.Feed.State is { Live: not null, Status: not null, Env: not null });

        var state = rig.Session.Feed.State;
        Assert.True(state.HasReading);
        Assert.Equal(85, state.Status!.BatterySocPct);
        Assert.Equal(24.5, state.Env!.OnboardTempC, 2);

        int seq = state.Live!.Seq;
        await TestKit.AdvanceUntilAsync(rig.Time, () => rig.Session.Feed.State.Live!.Seq > seq);
    }

    [Fact]
    public async Task LiveFeed_RestartsAStoppedDemodulationOnce()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var sim = new SimulatedLeveltronicLink(time);
        await sim.ConnectAsync();
        await new InstrumentApi(new LeveltronicDeviceClient(sim, time)).SetTiltDemodAsync(false);

        await using var session = await LeveltronicSession.OpenAsync(TestKit.Device(), sim, time);
        await TestKit.AdvanceUntilAsync(time, () => session.Feed.State.Status is not null);
        await TestKit.AdvanceUntilAsync(time, () => session.Feed.State.Status!.DemodRunning, TimeSpan.FromSeconds(5));

        Assert.True(session.Feed.State.Status!.DemodRunning);
    }

    [Fact]
    public async Task LiveFeed_ShowsDoubtfulReadings()
    {
        await using var rig = await SimRig.OpenAsync();
        rig.Sim.Disturbed = true;

        await TestKit.AdvanceUntilAsync(rig.Time, () => rig.Session.Feed.State.Live is { AnyDoubtful: true });
    }
}
