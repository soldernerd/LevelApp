using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

public sealed class PrecisionRunTests
{
    [Fact]
    public async Task RunsToAResultWithProgressAlongTheWay()
    {
        await using var rig = await SimRig.OpenAsync();
        var counts = new List<int>();
        var run = new PrecisionRun(rig.Api, rig.Time);
        run.Progress += (_, s) => { if (s.Phase == PrecisionPhase.Running) counts.Add(s.Count); };

        var task = run.StartAsync();
        await TestKit.AdvanceUntilAsync(rig.Time, () => task.IsCompleted, TimeSpan.FromSeconds(10));
        var result = await task;

        Assert.False(result.Failed);
        Assert.Equal(PrecisionPhase.Done, result.Phase);
        Assert.True(counts.Count > 1 && counts.Max() > counts.Min());
        // 0.8 mm/m on S1, 0.5 on S2, calibration slightly off but close
        Assert.InRange(result.TiltDiff, 0.0, 0.6);
    }

    [Fact]
    public async Task SaysSoWhenNoQuietWindowAppears()
    {
        await using var rig = await SimRig.OpenAsync(t => new SimulatedLeveltronicLink(t) { PrecisionNeverClean = true });
        var run = new PrecisionRun(rig.Api, rig.Time);

        var task = run.StartAsync();
        await TestKit.AdvanceUntilAsync(rig.Time, () => task.IsCompleted, TimeSpan.FromSeconds(10));

        Assert.True((await task).Failed);
    }

    [Fact]
    public async Task ReportsARefusalFromTheDevice()
    {
        await using var rig = await SimRig.OpenAsync();
        await rig.Api.SetTiltDemodAsync(false);
        var run = new PrecisionRun(rig.Api, rig.Time);

        var task = run.StartAsync();
        await TestKit.SettleAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => TestKit.WithTimeout(task));
        Assert.Equal(ApiStatus.BusyResource, ex.Status);
    }

    [Fact]
    public async Task CancelStopsTheDevice_AndCompletesWithCancelled()
    {
        await using var rig = await SimRig.OpenAsync();
        var run = new PrecisionRun(rig.Api, rig.Time);

        var task = run.StartAsync();
        await TestKit.AdvanceAsync(rig.Time, TimeSpan.FromMilliseconds(300));
        await run.CancelAsync();

        await Assert.ThrowsAsync<PrecisionCancelledException>(() => TestKit.WithTimeout(task));
        Assert.Equal(PrecisionPhase.Idle, (await rig.Api.PrecisionStatusAsync()).Phase);
    }

    [Fact]
    public async Task ASecondMeasurementIsNotFooledByTheFirstOnesResult()
    {
        await using var rig = await SimRig.OpenAsync();
        var first = new PrecisionRun(rig.Api, rig.Time).StartAsync();
        await TestKit.AdvanceUntilAsync(rig.Time, () => first.IsCompleted, TimeSpan.FromSeconds(10));
        await first;

        rig.Sim.PhysicalTilt = [0.2, 0.2]; // the table moved between the runs
        var second = new PrecisionRun(rig.Api, rig.Time).StartAsync();
        await TestKit.AdvanceUntilAsync(rig.Time, () => second.IsCompleted, TimeSpan.FromSeconds(10));

        Assert.NotEqual((await first).TiltS1, (await second).TiltS1);
    }
}

public sealed class SessionManagerTests
{
    [Fact]
    public async Task TwoLeases_ShareOneSession_AndOneLink()
    {
        var rig = new SimRig();
        await using var a = await rig.Sessions.AcquireAsync(TestKit.Device());
        await using var b = await rig.Sessions.AcquireAsync(TestKit.Device());

        Assert.Same(a.Session, b.Session);
        Assert.Equal(1, rig.LinksCreated);
    }

    [Fact]
    public async Task LastRelease_ClosesTheSession()
    {
        var rig = new SimRig();
        var a = await rig.Sessions.AcquireAsync(TestKit.Device());
        var b = await rig.Sessions.AcquireAsync(TestKit.Device());
        var session = a.Session;

        await a.DisposeAsync();
        Assert.False(session.IsClosed);
        await b.DisposeAsync();
        Assert.True(session.IsClosed);
    }

    [Fact]
    public async Task AfterTheLinkIsLost_TheNextAcquireOpensAFreshSession()
    {
        var rig = new SimRig();
        await using var first = await rig.Sessions.AcquireAsync(TestKit.Device());

        rig.Sim.LinkDown();
        Assert.True(first.Session.IsClosed);

        await using var second = await rig.Sessions.AcquireAsync(TestKit.Device());
        Assert.NotSame(first.Session, second.Session);
        Assert.Equal(2, rig.LinksCreated);
    }

    [Fact]
    public async Task FailedOpen_DoesNotLeaveASlotBehind()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        int attempts = 0;
        var manager = new LeveltronicSessionManager(
            _ => new SimulatedLeveltronicLink(time, apiVersion: ++attempts == 1 ? 2 : 3), time);

        await Assert.ThrowsAsync<IncompatibleFirmwareException>(() => manager.AcquireAsync(TestKit.Device()));
        await using var lease = await manager.AcquireAsync(TestKit.Device());

        Assert.Equal(2, attempts);
        Assert.False(lease.Session.IsClosed);
    }
}

public sealed class PreferencesTests
{
    [Fact]
    public void JsonStore_RemembersTareChannelAndShim_PerSerial()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lt-prefs-{Guid.NewGuid():N}.json");
        try
        {
            var a = new JsonLeveltronicPreferences(path);
            a.SetTare("AAAA0001", 0.123);
            a.SetChannel("AAAA0001", "s2");
            a.SetShim("AAAA0001", 1, new ShimSetup(0.1, 150));
            a.SetTare("BBBB0002", -0.5);

            var b = new JsonLeveltronicPreferences(path);

            Assert.Equal(0.123, b.GetTare("AAAA0001"));
            Assert.Equal("s2", b.GetChannel("AAAA0001"));
            Assert.Equal(new ShimSetup(0.1, 150), b.GetShim("AAAA0001", 1));
            Assert.Null(b.GetShim("AAAA0001", 0));
            Assert.Equal(-0.5, b.GetTare("BBBB0002"));

            b.SetTare("AAAA0001", null);
            Assert.Null(new JsonLeveltronicPreferences(path).GetTare("AAAA0001"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JsonStore_IgnoresACorruptFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lt-prefs-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            var p = new JsonLeveltronicPreferences(path);
            Assert.Null(p.GetTare("X"));
            p.SetTare("X", 1.0); // and it recovers by overwriting
            Assert.Equal(1.0, new JsonLeveltronicPreferences(path).GetTare("X"));
        }
        finally { File.Delete(path); }
    }
}

public sealed class ProviderTests
{
    private static async Task<(SimRig rig, LeveltronicProvider provider)> ConnectedAsync(
        Func<Microsoft.Extensions.Time.Testing.FakeTimeProvider, SimulatedLeveltronicLink>? make = null)
    {
        var rig = new SimRig(make);
        var provider = new LeveltronicProvider(TestKit.Device(), rig.Sessions, rig.Prefs, rig.Time, autoReconnect: false);
        await provider.ConnectAsync();
        await TestKit.AdvanceUntilAsync(rig.Time, () => provider.Latest is not null);
        return (rig, provider);
    }

    private static async Task<double> MeasureAsync(SimRig rig, LeveltronicProvider p, IProgress<double>? progress = null)
    {
        var task = p.MeasureAsync(progress, CancellationToken.None);
        await TestKit.AdvanceUntilAsync(rig.Time, () => task.IsCompleted, TimeSpan.FromSeconds(10));
        return await task;
    }

    [Fact]
    public async Task Connect_GoesConnected_AndIsALiveReadingSource()
    {
        var (rig, p) = await ConnectedAsync();
        Assert.Equal(InstrumentConnectionState.Connected, p.ConnectionState);
        Assert.IsAssignableFrom<ILiveReadingSource>(p);
        Assert.Equal(["s1", "s2", "diff"], p.Channels.Select(c => c.Id));
        await p.DisconnectAsync();
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task LiveValue_FollowsTheSelectedChannel_InMicronsPerMetre()
    {
        var (rig, p) = await ConnectedAsync();
        Assert.Equal("diff", p.SelectedChannelId); // the default channel
        p.SelectedChannelId = "s1";
        var s1 = p.Latest!.ValueMicronsPerMetre;
        p.SelectedChannelId = "s2";
        var s2 = p.Latest!.ValueMicronsPerMetre;
        p.SelectedChannelId = "diff";
        var diff = p.Latest!.ValueMicronsPerMetre;

        Assert.Equal(s1 - s2, diff, 1);
        Assert.NotEqual(s1, s2);
        Assert.True(Math.Abs(s1) > 10, "µm/m, not mm/m"); // 0.8 mm/m ≈ 800 µm/m
        Assert.True(p.Latest!.IsValid);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task LiveReadingChanged_FiresOnNewValuesAndChannelChange()
    {
        var (rig, p) = await ConnectedAsync();
        int fired = 0;
        p.LiveReadingChanged += (_, _) => fired++;

        p.SelectedChannelId = "s1";
        Assert.Equal(1, fired);

        await TestKit.AdvanceAsync(rig.Time, TimeSpan.FromSeconds(1));
        Assert.True(fired > 1);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task DoubtfulIsPerChannel()
    {
        var (rig, p) = await ConnectedAsync();
        rig.Sim.Disturbed = true;
        await TestKit.AdvanceUntilAsync(rig.Time, () => p.Latest is { IsDoubtful: true });
        Assert.True(p.Latest!.IsDoubtful);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task Measure_ReturnsTheSettledValueOfTheChannel_AndReportsProgress()
    {
        var (rig, p) = await ConnectedAsync();
        var progress = new List<double>();

        p.SelectedChannelId = "s1";
        double s1 = await MeasureAsync(rig, p, new Progress<double>(progress.Add));
        p.SelectedChannelId = "diff";
        double diff = await MeasureAsync(rig, p);

        Assert.True(s1 > 10);
        Assert.NotEqual(s1, diff);
        await TestKit.SettleAsync();
        Assert.Contains(1.0, progress);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task GetReadingAsync_IsAMeasurement()
    {
        var (rig, p) = await ConnectedAsync();
        p.SelectedChannelId = "s1";
        var task = p.GetReadingAsync(new MeasurementStep(), CancellationToken.None);
        await TestKit.AdvanceUntilAsync(rig.Time, () => task.IsCompleted, TimeSpan.FromSeconds(10));
        Assert.True(Math.Abs(await task) > 10);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task Measure_NoQuietWindow_SaysWhatToDo()
    {
        var (rig, p) = await ConnectedAsync(t => new SimulatedLeveltronicLink(t) { PrecisionNeverClean = true });
        var task = p.MeasureAsync(null, CancellationToken.None);
        await TestKit.AdvanceUntilAsync(rig.Time, () => task.IsCompleted, TimeSpan.FromSeconds(10));

        var ex = await Assert.ThrowsAsync<InstrumentMeasurementException>(() => task);
        Assert.Contains("Keep the table still", ex.Message);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task Cancel_StopsTheMeasurement()
    {
        var (rig, p) = await ConnectedAsync();
        using var cts = new CancellationTokenSource();

        var task = p.MeasureAsync(null, cts.Token);
        await TestKit.AdvanceAsync(rig.Time, TimeSpan.FromMilliseconds(300));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestKit.WithTimeout(task));
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ZeroDiff_SubtractsTheOffsetFromDiffOnly_AndIsRemembered()
    {
        var (rig, p) = await ConnectedAsync();
        Assert.True(p.SupportsZero);
        double before = p.Latest!.ValueMicronsPerMetre;

        var zero = p.ZeroAsync(null, CancellationToken.None);
        await TestKit.AdvanceUntilAsync(rig.Time, () => zero.IsCompleted, TimeSpan.FromSeconds(10));
        await zero;

        Assert.True(p.IsZeroed);
        Assert.Equal(before, p.ZeroOffsetMicronsPerMetre!.Value, 1);
        Assert.InRange(p.Latest!.ValueMicronsPerMetre, -1.0, 1.0);
        Assert.NotNull(rig.Prefs.GetTare(p.Session!.Serial));

        p.SelectedChannelId = "s1"; // other channels are untouched
        Assert.False(p.SupportsZero);
        Assert.True(Math.Abs(p.Latest!.ValueMicronsPerMetre) > 10);

        p.SelectedChannelId = "diff";
        p.ClearZero();
        Assert.False(p.IsZeroed);
        Assert.Equal(before, p.Latest!.ValueMicronsPerMetre, 1);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ChannelChoice_IsRememberedPerInstrument()
    {
        var (rig, p) = await ConnectedAsync();
        p.SelectedChannelId = "s2";
        await p.DisconnectAsync();

        var again = new LeveltronicProvider(TestKit.Device(), rig.Sessions, rig.Prefs, rig.Time, autoReconnect: false);
        await again.ConnectAsync();

        Assert.Equal("s2", again.SelectedChannelId);
        await again.DisconnectAsync();
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task Disconnect_ReleasesTheSession()
    {
        var (rig, p) = await ConnectedAsync();
        var session = p.Session!;

        await p.DisconnectAsync();

        Assert.Equal(InstrumentConnectionState.Disconnected, p.ConnectionState);
        Assert.True(session.IsClosed);
        Assert.Null(p.Latest);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task IncompatibleFirmware_SetsErrorAndThrowsAClearMessage()
    {
        var rig = new SimRig(t => new SimulatedLeveltronicLink(t, apiVersion: 2));
        var p = new LeveltronicProvider(TestKit.Device(), rig.Sessions, rig.Prefs, rig.Time, autoReconnect: false);

        var ex = await Assert.ThrowsAsync<IncompatibleFirmwareException>(() => p.ConnectAsync());

        Assert.Equal(InstrumentConnectionState.Error, p.ConnectionState);
        Assert.Contains("needs API v3", ex.Message);
    }

    [Fact]
    public async Task LinkLoss_WithoutAutoReconnect_GoesToError()
    {
        var (rig, p) = await ConnectedAsync();
        rig.Sim.LinkDown();
        await TestKit.SettleAsync();
        Assert.Equal(InstrumentConnectionState.Error, p.ConnectionState);
        Assert.Null(p.Latest);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task LinkLoss_OnBle_ReconnectsByItself()
    {
        var rig = new SimRig();
        var p = new LeveltronicProvider(TestKit.Device("ble"), rig.Sessions, rig.Prefs, rig.Time);
        await p.ConnectAsync();
        var states = new List<InstrumentConnectionState>();
        p.ConnectionStateChanged += (_, s) => states.Add(s);

        rig.Sim.LinkDown();
        await TestKit.AdvanceUntilAsync(rig.Time, () => p.ConnectionState == InstrumentConnectionState.Connected,
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(250));

        Assert.Contains(InstrumentConnectionState.Connecting, states);
        Assert.Equal(2, rig.LinksCreated);
        await p.DisconnectAsync();
    }
}
