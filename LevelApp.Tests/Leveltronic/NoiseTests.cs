using System.Buffers.Binary;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Diagnostics;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>API 3.2: Topics NOISE (0x5/0x06) and Commands NOISE_RESET (0x1/0x53).</summary>
public sealed class NoiseTests
{
    // ── Wire layout ──────────────────────────────────────────────────────────

    /// <summary>A NOISE payload laid out per the firmware's api-reference.md (52 bytes).</summary>
    private static byte[] Payload(bool valid = true, bool floors = true, int batches = 407)
    {
        var b = new byte[52];
        b[0] = (byte)(valid ? 1 : 0);
        b[1] = (byte)(floors ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), (ushort)batches);
        void F(int off, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(off), v);
        F(4, 0.0004f); F(8, 0.0005f); F(12, 0.00064f);     // sigma s1, s2, diff
        F(16, 0.0024f); F(20, 0.003f); F(24, 0.0038f);     // p2p s1, s2, diff
        F(28, 0.0003f); F(32, 0.00035f);                   // quad s1, s2
        F(36, 0.0002f); F(40, 0.00025f);                   // floor s1, s2
        F(44, 0.0003f); F(48, 0.0008f);                    // step s1, s2
        return b;
    }

    [Fact]
    public void Decode_ReadsEveryFieldAtItsOffset()
    {
        var n = NoiseFigures.Decode(Payload())!;

        Assert.True(n.Valid);
        Assert.True(n.FloorsReady);
        Assert.Equal(407, n.WindowBatches);
        Assert.Equal([0.0004, 0.0005, 0.00064], n.Sigma.Select(v => Math.Round(v, 6)));
        Assert.Equal([0.0024, 0.003, 0.0038], n.P2p.Select(v => Math.Round(v, 6)));
        Assert.Equal([0.0003, 0.00035], n.Quad.Select(v => Math.Round(v, 6)));
        Assert.Equal([0.0002, 0.00025], n.Floor.Select(v => Math.Round(v, 6)));
        Assert.Equal([0.0003, 0.0008], n.Step.Select(v => Math.Round(v, 6)));
    }

    [Fact]
    public void Decode_TooShortIsNull_LongerIsAccepted()
    {
        Assert.Null(NoiseFigures.Decode(new byte[51]));
        // The API only ever appends fields, so a longer payload from a newer 3.x firmware still decodes.
        var longer = new byte[60];
        Payload().CopyTo(longer, 0);
        Assert.NotNull(NoiseFigures.Decode(longer));
    }

    [Fact]
    public void Doubtful_IsStepOverFloorAboveSqrtSix()
    {
        var n = NoiseFigures.Decode(Payload())!;

        Assert.Equal(1.5, n.StepOverFloor(0)!.Value, 3);            // 0.0003 / 0.0002
        Assert.False(n.IsDoubtful(0));
        Assert.Equal(3.2, n.StepOverFloor(1)!.Value, 3);            // 0.0008 / 0.00025
        Assert.True(n.IsDoubtful(1));
        Assert.Equal(Math.Sqrt(6), NoiseFigures.DoubtfulRatio, 9);
    }

    [Fact]
    public void WhileTheFloorsAreLearned_ThereIsNoRatioAndNothingIsDoubtful()
    {
        var b = Payload(floors: false);
        Array.Clear(b, 36, 16); // the device reads floor and step as 0 until the floors are ready
        var n = NoiseFigures.Decode(b)!;

        Assert.Null(n.StepOverFloor(0));
        Assert.False(n.IsDoubtful(0));
        Assert.False(n.IsDoubtful(1));
    }

    // ── Formatting ───────────────────────────────────────────────────────────

    [Fact]
    public void FormatNoise_ShowsMicronsPerMetre()
    {
        Assert.Equal("0.40 µm/m", DiagnosticsMath.FormatNoise(0.0004));
        Assert.Equal("12.35 µm/m", DiagnosticsMath.FormatNoise(0.01235));
        Assert.Equal("250.0 µm/m", DiagnosticsMath.FormatNoise(0.25));
        Assert.Equal("—", DiagnosticsMath.FormatNoise(double.NaN));
    }

    [Fact]
    public void FormatRatio_AndWindow()
    {
        Assert.Equal("1.5×", DiagnosticsMath.FormatRatio(1.5));
        Assert.Equal("—", DiagnosticsMath.FormatRatio(null));

        Assert.Equal("Window full (last 10 s)", DiagnosticsMath.FormatWindow(true, 407));
        Assert.Equal("Window filling 203 / 407 batches (5 s)", DiagnosticsMath.FormatWindow(false, 203));
        Assert.Equal(1.0, DiagnosticsMath.WindowFraction(true, 407));
        Assert.Equal(0.5, DiagnosticsMath.WindowFraction(false, 204), 2);
    }

    // ── Against the simulator ────────────────────────────────────────────────

    private static async Task<NoiseFigures> WaitForAsync(SimRig rig, Func<NoiseFigures, bool> condition, int seconds = 30)
    {
        NoiseFigures? last = null;
        using var sub = rig.Api.SubscribeNoise(n => last = n);
        await TestKit.AdvanceUntilAsync(rig.Time, () => last is not null && condition(last),
            TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(250));
        return last!;
    }

    [Fact]
    public async Task TheWindowFillsOverTenSeconds_ThenTheFiguresAreValid()
    {
        await using var rig = await SimRig.OpenAsync();

        var filling = await WaitForAsync(rig, n => n.WindowBatches > 0);
        Assert.False(filling.Valid);
        Assert.InRange(filling.WindowBatches, 1, 406);

        var full = await WaitForAsync(rig, n => n.Valid);
        Assert.Equal(NoiseFigures.FullWindowBatches, full.WindowBatches);
        Assert.Equal(0.0004, full.Sigma[0], 6);
        Assert.True(full.Sigma[2] > full.Sigma[0]);   // the difference is noisier than either sensor
    }

    [Fact]
    public async Task NoiseReset_RestartsTheWindow_ButKeepsTheFloors()
    {
        await using var rig = await SimRig.OpenAsync();
        await WaitForAsync(rig, n => n.Valid);

        await rig.Api.NoiseResetAsync();
        Assert.Equal(1, rig.Sim.NoiseResets);

        var after = await WaitForAsync(rig, n => !n.Valid && n.WindowBatches < 100);
        Assert.False(after.Valid);
        Assert.True(after.FloorsReady);                // the quiet floors describe the instrument
        await WaitForAsync(rig, n => n.Valid);         // and it fills again
    }

    [Fact]
    public async Task ADisturbedTable_MakesTheReadingsDoubtful()
    {
        await using var rig = await SimRig.OpenAsync();
        rig.Sim.Disturbed = true;

        var n = await WaitForAsync(rig, n => n.Valid);

        Assert.True(n.IsDoubtful(0));
        Assert.True(n.IsDoubtful(1));
    }

    [Fact]
    public async Task FloorsStillLearning_ReadZero()
    {
        await using var rig = await SimRig.OpenAsync();
        rig.Sim.NoiseFloorsReady = false;

        var n = await WaitForAsync(rig, n => n.Valid);

        Assert.False(n.FloorsReady);
        Assert.Null(n.StepOverFloor(0));
        Assert.All(n.Floor, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task Api31Firmware_RefusesBothTheTopicAndTheCommand()
    {
        await using var rig = await SimRig.OpenAsync();
        rig.Sim.ApiMinor = 1;

        var id = await rig.Api.IdentityAsync();
        Assert.False(id.AtLeast(3, 2));
        Assert.Equal("3.1", id.ApiVersionText);

        var ex = await Assert.ThrowsAsync<ApiException>(() => rig.Api.NoiseResetAsync());
        Assert.Equal(ApiStatus.UnknownResource, ex.Status);

        ApiException? refused = null;
        using var sub = rig.Api.SubscribeNoise(_ => { }, e => refused = e as ApiException);
        await TestKit.SettleAsync();
        Assert.Equal(ApiStatus.UnknownResource, refused?.Status);
    }

    [Fact]
    public async Task ARunningDeviceReportsApi32()
    {
        await using var rig = await SimRig.OpenAsync();
        var id = await rig.Api.IdentityAsync();

        Assert.True(id.AtLeast(3, 2));
        Assert.Equal("3.2", id.ApiVersionText);
    }

    [Fact]
    public void Opcodes_MatchTheFirmwareReference()
    {
        Assert.Equal(0x0506, ApiV3Codec.Get(ApiCategory.Topics, LeveltronicApi.Topic.Noise));
        Assert.Equal(0x3506, ApiV3Codec.Subscribe(ApiCategory.Topics, LeveltronicApi.Topic.Noise));
        Assert.Equal(0x2153, ApiV3Codec.Execute(LeveltronicApi.Cmd.NoiseReset));
    }
}
