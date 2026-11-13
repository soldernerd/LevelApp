using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;
using Microsoft.Extensions.Time.Testing;

namespace LevelApp.Tests.Leveltronic;

/// <summary>A link that records frames and answers nothing unless the test injects a packet.</summary>
internal sealed class StubLink : ILeveltronicLink
{
    public List<byte[]> Sent { get; } = [];
    public bool IsConnected { get; private set; } = true;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<byte[]>? PacketReceived;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync() { Drop(); return Task.CompletedTask; }
    public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        Sent.Add(frame.ToArray());
        return Task.CompletedTask;
    }
    public void Inject(ushort op, params byte[] payload) =>
        PacketReceived?.Invoke(this, ApiV3Codec.BuildFrame(op, payload));
    public void Drop()
    {
        IsConnected = false;
        ConnectionChanged?.Invoke(this, false);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ClientTests
{
    [Fact]
    public async Task Request_ReturnsReplyAndData()
    {
        await using var rig = await SimRig.OpenAsync();
        var r = await rig.Session.Client.RequestAsync(ApiV3Codec.Get(ApiCategory.System, LeveltronicApi.Sys.State));
        Assert.True(r.Ok);
        Assert.Equal(15, r.Data.Length);
    }

    [Fact]
    public async Task DeviceError_IsAReplyNotAnException_UntilOrThrow()
    {
        await using var rig = await SimRig.OpenAsync();
        var r = await rig.Session.Client.RequestAsync(ApiV3Codec.Get(ApiCategory.Measurements, 0x09));
        Assert.Equal(ApiStatus.UnknownResource, r.Status);
        var ex = Assert.Throws<ApiException>(() => r.OrThrow("tilt"));
        Assert.Contains("does not support", ex.UserMessage);
    }

    [Fact]
    public async Task NoAnswer_TimesOutWithLinkException()
    {
        var time = new FakeTimeProvider();
        var link = new StubLink();
        await using var client = new LeveltronicDeviceClient(link, time);

        var task = client.RequestAsync(0x0000);
        time.Advance(LeveltronicDeviceClient.DefaultTimeout + TimeSpan.FromMilliseconds(1));

        var ex = await Assert.ThrowsAsync<LinkException>(() => TestKit.WithTimeout(task));
        Assert.Contains("no response", ex.Message);
    }

    [Fact]
    public async Task Requests_AreSentOneAtATime_EachAfterTheAnswerToThePreviousOne()
    {
        // The instrument drops requests that arrive back to back (firmware 0.15 over USB HID), so
        // a second request waits until the first was answered, even for the same opcode.
        var link = new StubLink();
        await using var client = new LeveltronicDeviceClient(link, new FakeTimeProvider());

        var first = client.RequestAsync(0x0001);
        var second = client.RequestAsync(0x0001);
        await TestKit.WithTimeout(Task.Run(async () => { while (link.Sent.Count < 1) await Task.Delay(1); }));
        Assert.Single(link.Sent);

        link.Inject(0x0001, (byte)ApiStatus.Ok, 41);
        Assert.Equal(new byte[] { 41 }, (await TestKit.WithTimeout(first)).Data);

        await TestKit.WithTimeout(Task.Run(async () => { while (link.Sent.Count < 2) await Task.Delay(1); }));
        link.Inject(0x0001, (byte)ApiStatus.Ok, 42);
        Assert.Equal(new byte[] { 42 }, (await TestKit.WithTimeout(second)).Data);
    }

    [Fact]
    public async Task LinkLoss_FailsPendingRequests_AndRaisesClosed()
    {
        var link = new StubLink();
        await using var client = new LeveltronicDeviceClient(link, new FakeTimeProvider());
        bool closed = false;
        client.Closed += (_, _) => closed = true;

        var pending = client.RequestAsync(0x0001);
        link.Drop();

        await Assert.ThrowsAsync<LinkException>(() => TestKit.WithTimeout(pending));
        Assert.True(closed);
        await Assert.ThrowsAsync<LinkException>(() => client.RequestAsync(0x0001));
    }

    [Fact]
    public async Task Subscribe_DeliversPushData_AfterSeqAndPage()
    {
        await using var rig = await SimRig.OpenAsync();
        var got = new List<LiveTilt>();

        using var sub = rig.Api.SubscribeLive(got.Add);
        await TestKit.AdvanceUntilAsync(rig.Time, () => got.Count >= 2);

        Assert.All(got, l => Assert.True(l.Valid));
        // the simulator's stored calibration is deliberately a little off its truth
        Assert.InRange(got[0].TiltDiff, 0.2, 0.7);
    }

    [Fact]
    public async Task Subscribe_SharesOneDeviceSubscriptionBetweenListeners_AndEndsItWithTheLast()
    {
        var link = new StubLink();
        await using var client = new LeveltronicDeviceClient(link, new FakeTimeProvider());
        ushort op = ApiV3Codec.Subscribe(ApiCategory.Topics, LeveltronicApi.Topic.Live);
        var a = new List<byte[]>();
        var b = new List<byte[]>();

        var s1 = client.Subscribe(op, a.Add);
        await TestKit.SettleAsync();
        link.Inject(op, (byte)ApiStatus.Ok); // the ack
        var s2 = client.Subscribe(op, b.Add);
        await TestKit.SettleAsync();

        Assert.Single(link.Sent); // one SUBSCRIBE for two listeners

        link.Inject(op, (byte)ApiStatus.Ok, 1, 0, 9, 8, 7);
        Assert.Equal(new byte[] { 9, 8, 7 }, Assert.Single(a));
        Assert.Equal(new byte[] { 9, 8, 7 }, Assert.Single(b));

        s1.Dispose();
        await TestKit.SettleAsync();
        Assert.Single(link.Sent); // still one listener

        s2.Dispose();
        await TestKit.SettleAsync();
        Assert.Equal(2, link.Sent.Count);
        Assert.Equal(ApiV3Codec.WithVerb(op, ApiVerb.Unsubscribe), BitConverter.ToUInt16(link.Sent[1]));
    }

    [Fact]
    public async Task Subscribe_RefusedByTheDevice_ReportsAnError()
    {
        await using var rig = await SimRig.OpenAsync();
        ApiException? error = null;

        using var sub = rig.Session.Client.Subscribe(
            ApiV3Codec.Subscribe(ApiCategory.Topics, 0x77), _ => { }, e => error = e as ApiException);
        await TestKit.SettleAsync();

        Assert.Equal(ApiStatus.UnknownResource, error?.Status);
    }

    [Fact]
    public async Task Subscribe_LinkLoss_NotifiesListeners()
    {
        await using var rig = await SimRig.OpenAsync();
        bool closed = false;

        using var sub = rig.Api.SubscribeStatus(_ => { }, onClosed: () => closed = true);
        await TestKit.SettleAsync();
        rig.Sim.LinkDown();
        await TestKit.SettleAsync();

        Assert.True(closed);
    }
}
