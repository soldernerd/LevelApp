using LevelApp.Core.Geometry.ParallelWays.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

public class ParallelWaysStrategyTests
{
    private readonly ParallelWaysStrategy _sut = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an ObjectDefinition equivalent to what the ViewModel produces
    /// for a Parallel Ways session.
    /// </summary>
    private static ObjectDefinition Def(
        List<RailDefinition>     rails,
        List<ParallelWaysTask>   tasks,
        WaysOrientation          orientation = WaysOrientation.Horizontal,
        int                      refRailIdx  = 0)
    => new()
    {
        GeometryModuleId = "ParallelWays",
        Parameters = new Dictionary<string, object>
        {
            ["orientation"]        = orientation.ToString(),
            ["referenceRailIndex"] = refRailIdx,
            ["rails"]              = rails,
            ["tasks"]              = tasks,
            ["driftCorrection"]    = DriftCorrectionMethod.LeastSquares.ToString(),
            ["solverMode"]         = SolverMode.GlobalLeastSquares.ToString()
        }
    };

    private static RailDefinition Rail(string label, double length,
        double lateral = 0, double vertical = 0, double axial = 0)
    => new()
    {
        Label               = label,
        LengthMm            = length,
        LateralSeparationMm = lateral,
        VerticalOffsetMm    = vertical,
        AxialOffsetMm       = axial
    };

    private static ParallelWaysTask AlongRailTask(int railIdx, double stepDist,
        PassDirection pass = PassDirection.SinglePass)
    => new()
    {
        TaskType       = TaskType.AlongRail,
        RailIndexA     = railIdx,
        StepDistanceMm = stepDist,
        PassDirection  = pass
    };

    private static ParallelWaysTask BridgeTask(int rA, int rB, double stepDist,
        PassDirection pass = PassDirection.SinglePass)
    => new()
    {
        TaskType       = TaskType.Bridge,
        RailIndexA     = rA,
        RailIndexB     = rB,
        StepDistanceMm = stepDist,
        PassDirection  = pass
    };

    // ── Step count ────────────────────────────────────────────────────────────

    [Fact]
    public void AlongRailSinglePass_TwoRails_StepCount()
    {
        // 2 rails × 1000 mm with 200 mm step = 5 intervals per rail
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200), AlongRailTask(1, 200)]);

        var steps = _sut.GenerateSteps(def);

        // 5 steps + 5 steps = 10
        Assert.Equal(10, steps.Count);
    }

    [Fact]
    public void AlongRailForwardReturn_SingleRailTask_StepCount()
    {
        // 1 task, 1 rail, 1000 mm / 250 mm = 4 intervals → 4 fwd + 4 ret = 8 steps
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 250, PassDirection.ForwardAndReturn)]);

        var steps = _sut.GenerateSteps(def);

        Assert.Equal(8, steps.Count);
    }

    [Fact]
    public void BridgeTask_SinglePass_StepCount()
    {
        // Bridge from rail 0 to rail 1, 1000 mm / 200 mm = 5 intervals → 6 stations
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200), AlongRailTask(1, 200),
             BridgeTask(0, 1, 200)]);

        var steps = _sut.GenerateSteps(def);

        // 5 + 5 along-rail + 6 bridge = 16
        Assert.Equal(16, steps.Count);
    }

    [Fact]
    public void BridgeTask_ForwardReturn_StepCount()
    {
        // 5 intervals → 6 stations × 2 (fwd+ret) = 12 bridge steps
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200), AlongRailTask(1, 200),
             BridgeTask(0, 1, 200, PassDirection.ForwardAndReturn)]);

        var steps = _sut.GenerateSteps(def);

        // 5 + 5 along-rail + 12 bridge = 22
        Assert.Equal(22, steps.Count);
    }

    // ── Node ID format ────────────────────────────────────────────────────────

    [Fact]
    public void AlongRail_NodeIds_FollowConvention()
    {
        var def = Def(
            [Rail("A", 500), Rail("B", 500, lateral: 400)],
            [AlongRailTask(0, 250)]);

        var steps = _sut.GenerateSteps(def);

        // 2 intervals: sta0→sta1, sta1→sta2
        Assert.Equal("rail0_sta0", steps[0].NodeId);
        Assert.Equal("rail0_sta1", steps[0].ToNodeId);
        Assert.Equal("rail0_sta1", steps[1].NodeId);
        Assert.Equal("rail0_sta2", steps[1].ToNodeId);
    }

    [Fact]
    public void Bridge_NodeIds_AcrossRails()
    {
        var def = Def(
            [Rail("A", 500), Rail("B", 500, lateral: 400)],
            [BridgeTask(0, 1, 250)]);

        var steps = _sut.GenerateSteps(def);

        // All bridge steps should go from rail0 to rail1
        Assert.All(steps, s =>
        {
            var (rFrom, _) = ParallelWaysStrategy.ParseNodeId(s.NodeId);
            var (rTo,   _) = ParallelWaysStrategy.ParseNodeId(s.ToNodeId);
            Assert.Equal(0, rFrom);
            Assert.Equal(1, rTo);
        });
    }

    // ── Pass phase ────────────────────────────────────────────────────────────

    [Fact]
    public void SinglePass_PassPhase_IsNotApplicable()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200)]);

        var steps = _sut.GenerateSteps(def);

        Assert.All(steps, s => Assert.Equal(PassPhase.NotApplicable, s.PassPhase));
    }

    [Fact]
    public void ForwardReturn_PassPhase_IsCorrect()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200, PassDirection.ForwardAndReturn)]);

        var steps = _sut.GenerateSteps(def);

        int n = 5; // 1000 / 200 intervals
        Assert.Equal(n * 2, steps.Count);

        for (int i = 0; i < n;     i++) Assert.Equal(PassPhase.Forward, steps[i].PassPhase);
        for (int i = n; i < n * 2; i++) Assert.Equal(PassPhase.Return,  steps[i].PassPhase);
    }

    // ── Index sequence ────────────────────────────────────────────────────────

    [Fact]
    public void Indices_AreSequentialFromZero()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200), AlongRailTask(1, 200)]);

        var steps = _sut.GenerateSteps(def);

        for (int i = 0; i < steps.Count; i++)
            Assert.Equal(i, steps[i].Index);
    }

    // ── Orientation ───────────────────────────────────────────────────────────

    [Fact]
    public void HorizontalWays_ForwardPass_OrientationIsEast()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200)]);

        var steps = _sut.GenerateSteps(def);

        Assert.All(steps, s => Assert.Equal(Orientation.East, s.Orientation));
    }

    [Fact]
    public void HorizontalWays_ReturnPass_OrientationIsWest()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200, PassDirection.ForwardAndReturn)]);

        var steps = _sut.GenerateSteps(def);

        int n = 5;
        for (int i = n; i < n * 2; i++)
            Assert.Equal(Orientation.West, steps[i].Orientation);
    }

    // ── ParseNodeId ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("rail0_sta0",  0, 0)]
    [InlineData("rail1_sta3",  1, 3)]
    [InlineData("rail2_sta10", 2, 10)]
    public void ParseNodeId_ReturnsCorrectValues(string nodeId, int expectedRail, int expectedSta)
    {
        var (rail, sta) = ParallelWaysStrategy.ParseNodeId(nodeId);
        Assert.Equal(expectedRail, rail);
        Assert.Equal(expectedSta,  sta);
    }

    // ── GetPrimitiveLoopNodeIds ────────────────────────────────────────────────

    [Fact]
    public void GetPrimitiveLoopNodeIds_ReturnsEmpty()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRailTask(0, 200), AlongRailTask(1, 200)]);

        var loops = _sut.GetPrimitiveLoopNodeIds(def);

        Assert.Empty(loops);
    }

    // ── Min rails / tasks guards ──────────────────────────────────────────────

    [Fact]
    public void LessThanTwoRails_Throws()
    {
        var def = Def(
            [Rail("A", 1000)],  // only 1 rail — invalid
            [AlongRailTask(0, 200)]);

        Assert.Throws<ArgumentException>(() => _sut.GenerateSteps(def));
    }

    [Fact]
    public void NoTasks_Throws()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            []);   // no tasks — invalid

        Assert.Throws<ArgumentException>(() => _sut.GenerateSteps(def));
    }
}
