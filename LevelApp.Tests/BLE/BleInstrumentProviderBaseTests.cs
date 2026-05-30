using LevelApp.Core.Instruments;
using LevelApp.Core.Models;
using LevelApp.Instruments.BLE;

namespace LevelApp.Tests.BLE;

/// <summary>
/// Tests for the connection state machine and exponential backoff logic in
/// <see cref="BleInstrumentProviderBase"/>.  Uses a test double that overrides
/// the BLE hardware calls so no real BLE adapter is required.
/// </summary>
public sealed class BleInstrumentProviderBaseTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Concrete test double.
    /// <para>
    /// <see cref="ConnectBehaviour"/> is called on each DoConnectAsync invocation.
    /// Returning normally = success; throwing = failure that triggers a retry.
    /// </para>
    /// </summary>
    private sealed class TestProvider : BleInstrumentProviderBase
    {
        public override string             ProviderId   => "test-ble";
        public override string             DisplayName  => "Test BLE";
        public override InstrumentCapabilities Capabilities =>
            InstrumentCapabilities.SingleMeasurement;

        public Func<int, CancellationToken, Task> ConnectBehaviour { get; set; }
            = (_, _) => Task.CompletedTask;

        private int _attemptCount;

        public int AttemptCount => _attemptCount;

        public TestProvider(ulong address = 0xAABBCCDDEEFF)
            : base(address) { }

        protected override async Task DoConnectAsync(ulong address, CancellationToken ct)
        {
            int attempt = Interlocked.Increment(ref _attemptCount);
            await ConnectBehaviour(attempt, ct).ConfigureAwait(false);
        }

        protected override Task DoDisconnectAsync() => Task.CompletedTask;

        public override Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct)
            => Task.FromResult(0.0);

        // Expose the protected hook for tests
        public new void OnUnexpectedDisconnect() => base.OnUnexpectedDisconnect();
    }

    // ── State machine tests ────────────────────────────────────────────────────

    [Fact]
    public void InitialState_Is_Disconnected()
    {
        var provider = new TestProvider();
        Assert.Equal(InstrumentConnectionState.Disconnected, provider.ConnectionState);
    }

    [Fact]
    public async Task ConnectAsync_Sets_Connected_On_Success()
    {
        var provider = new TestProvider();
        await provider.ConnectAsync();
        Assert.Equal(InstrumentConnectionState.Connected, provider.ConnectionState);
    }

    [Fact]
    public async Task ConnectAsync_Is_NoOp_When_Already_Connected()
    {
        var provider = new TestProvider();
        await provider.ConnectAsync();
        await provider.ConnectAsync();

        Assert.Equal(1, provider.AttemptCount);
    }

    [Fact]
    public async Task DisconnectAsync_Sets_Disconnected()
    {
        var provider = new TestProvider();
        await provider.ConnectAsync();
        await provider.DisconnectAsync();
        Assert.Equal(InstrumentConnectionState.Disconnected, provider.ConnectionState);
    }

    [Fact]
    public async Task ConnectionStateChanged_Fires_Connecting_Then_Connected()
    {
        var provider = new TestProvider();
        var states   = new List<InstrumentConnectionState>();

        provider.ConnectionStateChanged += (_, s) => states.Add(s);
        await provider.ConnectAsync();

        Assert.Equal(
            new[] { InstrumentConnectionState.Connecting, InstrumentConnectionState.Connected },
            states);
    }

    // ── Backoff / retry tests ──────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_Retries_After_Failure()
    {
        var provider = new TestProvider();

        // Fail on attempt 1, succeed on attempt 2
        provider.ConnectBehaviour = (attempt, ct) =>
            attempt == 1
                ? Task.FromException(new InvalidOperationException("first fail"))
                : Task.CompletedTask;

        await provider.ConnectAsync();

        Assert.Equal(InstrumentConnectionState.Connected, provider.ConnectionState);
        Assert.Equal(2, provider.AttemptCount);
    }

    [Fact]
    public async Task ConnectAsync_Emits_Error_State_Between_Retries()
    {
        var provider = new TestProvider();
        var states   = new List<InstrumentConnectionState>();

        provider.ConnectionStateChanged += (_, s) => states.Add(s);

        // Fail once then succeed
        provider.ConnectBehaviour = (attempt, ct) =>
            attempt == 1
                ? Task.FromException(new Exception("boom"))
                : Task.CompletedTask;

        await provider.ConnectAsync();

        Assert.Contains(InstrumentConnectionState.Error,      states);
        Assert.Contains(InstrumentConnectionState.Connecting, states);
        Assert.Contains(InstrumentConnectionState.Connected,  states);
    }

    // ── Cancellation tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_Cancellation_Stops_Retry_Loop()
    {
        var provider = new TestProvider();
        using var cts = new CancellationTokenSource();

        // Always fail so the loop would retry forever — we cancel after 1st attempt
        provider.ConnectBehaviour = (attempt, ct) =>
        {
            if (attempt == 1) cts.Cancel();
            return Task.FromException<object>(new Exception("always fail"));
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task DisconnectAsync_Cancels_InFlight_Backoff_Delay()
    {
        var provider = new TestProvider();

        // Fail permanently so the provider is stuck in the backoff delay
        provider.ConnectBehaviour = (_, ct) =>
            Task.FromException(new Exception("always fail"));

        var connectTask = provider.ConnectAsync();

        // Give it time to enter the first backoff delay (1 s), then disconnect
        await Task.Delay(50);
        await provider.DisconnectAsync();

        // connectTask should have been cancelled (not run for a full second)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connectTask);
        Assert.Equal(InstrumentConnectionState.Disconnected, provider.ConnectionState);
    }

    // ── Unexpected-disconnect / background reconnect tests ────────────────────

    [Fact]
    public async Task OnUnexpectedDisconnect_Starts_Reconnect_And_Reaches_Connected()
    {
        var provider = new TestProvider();
        await provider.ConnectAsync();

        // Simulate peripheral drop: next DoConnect call succeeds immediately
        var tcs = new TaskCompletionSource();
        provider.ConnectBehaviour = (_, _) =>
        {
            tcs.TrySetResult();
            return Task.CompletedTask;
        };

        provider.OnUnexpectedDisconnect();

        // Should become Connecting immediately
        Assert.Equal(InstrumentConnectionState.Connecting, provider.ConnectionState);

        // Background task should reconnect quickly
        await Task.WhenAny(tcs.Task, Task.Delay(2_000));
        Assert.True(tcs.Task.IsCompleted, "Reconnect never called DoConnectAsync");
    }

    [Fact]
    public async Task OnUnexpectedDisconnect_Is_Stopped_By_DisconnectAsync()
    {
        var provider = new TestProvider();
        await provider.ConnectAsync();

        // Always fail → loop would retry forever
        provider.ConnectBehaviour = (_, _) =>
            Task.FromException(new Exception("always fail"));

        provider.OnUnexpectedDisconnect();

        await Task.Delay(50); // let the loop enter the first backoff
        await provider.DisconnectAsync();

        // Must settle in Disconnected, not Error / Connecting
        Assert.Equal(InstrumentConnectionState.Disconnected, provider.ConnectionState);
    }
}
