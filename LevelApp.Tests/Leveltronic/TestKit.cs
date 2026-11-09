using LevelApp.Core.Instruments;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Preferences;
using Microsoft.Extensions.Time.Testing;

namespace LevelApp.Tests.Leveltronic;

/// <summary>Shared helpers for the Leveltronic tests: fake time, a connected simulator, waiting.</summary>
internal static class TestKit
{
    public static KnownDevice Device(string transportId = "ble") => new(
        DeviceId: "dev-1", PluginId: "leveltronic", TransportId: transportId,
        DisplayName: "Leveltronic-1234", TransportAddress: "ABCDEF123456");

    /// <summary>
    /// Advances <paramref name="time"/> in small steps, letting asynchronous
    /// continuations run in between, until <paramref name="condition"/> holds.
    /// Fails after <paramref name="maxSimulated"/> of simulated time.
    /// </summary>
    public static async Task AdvanceUntilAsync(
        FakeTimeProvider time, Func<bool> condition,
        TimeSpan? maxSimulated = null, TimeSpan? step = null)
    {
        var limit = maxSimulated ?? TimeSpan.FromSeconds(30);
        var stepSize = step ?? TimeSpan.FromMilliseconds(50);
        var elapsed = TimeSpan.Zero;

        await SettleAsync();
        while (!condition())
        {
            if (elapsed >= limit)
                throw new TimeoutException($"condition not met within {limit.TotalSeconds:0.#} s of simulated time");
            time.Advance(stepSize);
            elapsed += stepSize;
            await SettleAsync();
        }
    }

    /// <summary>Advances a fixed amount of simulated time, settling after each step.</summary>
    public static async Task AdvanceAsync(FakeTimeProvider time, TimeSpan total, TimeSpan? step = null)
    {
        var stepSize = step ?? TimeSpan.FromMilliseconds(50);
        for (var done = TimeSpan.Zero; done < total; done += stepSize)
        {
            time.Advance(stepSize);
            await SettleAsync();
        }
    }

    /// <summary>Lets queued continuations (thread-pool hops) run.</summary>
    public static async Task SettleAsync()
    {
        // Round-trips through the thread pool drain queued continuations without
        // paying the ~15 ms of a real timer wait.
        for (int i = 0; i < 6; i++)
        {
            await Task.Run(static () => { });
            await Task.Yield();
        }
    }

    public static async Task<T> WithTimeout<T>(Task<T> task, int ms = 5000)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        if (done != task) throw new TimeoutException("task did not complete");
        return await task;
    }

    public static async Task WithTimeout(Task task, int ms = 5000)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        if (done != task) throw new TimeoutException("task did not complete");
        await task;
    }
}

/// <summary>A connected simulator, client, API and session on fake time.</summary>
internal sealed class SimRig : IAsyncDisposable
{
    public FakeTimeProvider Time { get; } = new();
    public SimulatedLeveltronicLink Sim { get; }
    public LeveltronicSession Session { get; private set; } = null!;
    public LeveltronicSessionManager Sessions { get; }
    public InMemoryLeveltronicPreferences Prefs { get; } = new();
    public int LinksCreated { get; private set; }

    public SimRig(Func<FakeTimeProvider, SimulatedLeveltronicLink>? make = null)
    {
        Sim = make?.Invoke(Time) ?? new SimulatedLeveltronicLink(Time);
        Sessions = new LeveltronicSessionManager(_ => { LinksCreated++; return Sim; }, Time);
    }

    public InstrumentApi Api => Session.Api;

    public static async Task<SimRig> OpenAsync(Func<FakeTimeProvider, SimulatedLeveltronicLink>? make = null)
    {
        var rig = new SimRig(make);
        rig.Session = await LeveltronicSession.OpenAsync(TestKit.Device(), rig.Sim, rig.Time);
        return rig;
    }

    public async ValueTask DisposeAsync()
    {
        if (Session is not null) await Session.DisposeAsync();
    }
}
