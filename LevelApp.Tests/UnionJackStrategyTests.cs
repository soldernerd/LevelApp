using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

public class UnionJackStrategyTests
{
    private readonly UnionJackStrategy _sut = new();

    private static ObjectDefinition Def(
        int segments,
        UnionJackRings rings,
        double widthMm  = 1200.0,
        double heightMm = 800.0) => new()
    {
        GeometryModuleId = "SurfacePlate",
        Parameters = new Dictionary<string, object>
        {
            ["segments"] = segments,
            ["rings"]    = rings.ToString(),   // stored as string
            ["widthMm"]  = widthMm,
            ["heightMm"] = heightMm
        }
    };

    // ── Step count ────────────────────────────────────────────────────────────
    // None          → 8 × segments
    // Circumference → 8 × segments + 8   (one ring at r = segments)
    // Full          → 8 × segments + 8 × segments = 16 × segments

    [Theory]
    [InlineData(1, 0,  8)]    // segments=1, None
    [InlineData(1, 1, 16)]    // segments=1, Circumference  (ring at r=1=segments)
    [InlineData(1, 2, 16)]    // segments=1, Full            (same ring, same count)
    [InlineData(2, 0, 16)]    // segments=2, None
    [InlineData(2, 1, 24)]    // segments=2, Circumference  (ring at r=2)
    [InlineData(2, 2, 32)]    // segments=2, Full            (rings r=1..2)
    [InlineData(4, 0, 32)]    // segments=4, None
    [InlineData(4, 1, 40)]    // segments=4, Circumference  (ring at r=4)
    [InlineData(4, 2, 64)]    // segments=4, Full            (rings r=1..4)
    public void StepCount_MatchesFormula(int segments, int ringsInt, int expected)
    {
        var steps = _sut.GenerateSteps(Def(segments, (UnionJackRings)ringsInt));
        Assert.Equal(expected, steps.Count);
    }

    // ── Index sequence ────────────────────────────────────────────────────────

    [Fact]
    public void Indices_AreSequentialFromZero()
    {
        var steps = _sut.GenerateSteps(Def(3, UnionJackRings.Full));
        for (int i = 0; i < steps.Count; i++)
            Assert.Equal(i, steps[i].Index);
    }

    // ── Node ids ──────────────────────────────────────────────────────────────

    [Fact]
    public void AllSteps_HaveNonEmptyNodeIds()
    {
        var steps = _sut.GenerateSteps(Def(2, UnionJackRings.Circumference));
        Assert.All(steps, s =>
        {
            Assert.False(string.IsNullOrEmpty(s.NodeId));
            Assert.False(string.IsNullOrEmpty(s.ToNodeId));
        });
    }

    [Fact]
    public void Pass0_StartsAtWTip_EndsAtETip()
    {
        var steps = _sut.GenerateSteps(Def(2, UnionJackRings.None));
        var pass0 = steps.Where(s => s.PassId == 0).ToList();

        Assert.Equal("armW_seg2", pass0.First().NodeId);
        Assert.Equal("armE_seg2", pass0.Last().ToNodeId);
    }

    [Fact]
    public void Pass1_StartsAtNTip_EndsAtSTip()
    {
        var steps = _sut.GenerateSteps(Def(2, UnionJackRings.None));
        var pass1 = steps.Where(s => s.PassId == 1).ToList();

        Assert.Equal("armN_seg2", pass1.First().NodeId);
        Assert.Equal("armS_seg2", pass1.Last().ToNodeId);
    }

    [Fact]
    public void CenterNode_AppearsInAllFourArmPasses()
    {
        var steps   = _sut.GenerateSteps(Def(1, UnionJackRings.None));
        var centers = steps.Where(s => s.NodeId == "center" || s.ToNodeId == "center").ToList();

        // With segments=1, each arm pass has 2 steps touching center (arriving + departing)
        Assert.Equal(8, centers.Count);
    }

    // ── Pass ids ──────────────────────────────────────────────────────────────

    [Fact]
    public void FourArmPasses_HaveIds0to3()
    {
        var steps   = _sut.GenerateSteps(Def(2, UnionJackRings.None));
        var passIds = steps.Select(s => s.PassId).Distinct().OrderBy(x => x).ToList();

        Assert.Equal([0, 1, 2, 3], passIds);
    }

    [Fact]
    public void Circumference_HasSingleRingPassWithId4()
    {
        var steps      = _sut.GenerateSteps(Def(3, UnionJackRings.Circumference));
        var ringSteps  = steps.Where(s => s.PassId >= 4).ToList();
        var ringPasses = ringSteps.Select(s => s.PassId).Distinct().ToList();

        Assert.Single(ringPasses);
        Assert.Equal(4, ringPasses[0]);
        Assert.Equal(8, ringSteps.Count);
    }

    [Fact]
    public void Circumference_RingUsesArmTipNodes()
    {
        int segments = 3;
        var steps    = _sut.GenerateSteps(Def(segments, UnionJackRings.Circumference));
        var ringStep = steps.First(s => s.PassId == 4);

        // The ring is at r = segments; all ring-step node ids end in _seg{segments}
        Assert.EndsWith($"_seg{segments}", ringStep.NodeId);
    }

    [Fact]
    public void Full_RingPasses_HaveIdsStartingAt4()
    {
        var steps   = _sut.GenerateSteps(Def(2, UnionJackRings.Full));
        var ringIds = steps.Where(s => s.PassId >= 4).Select(s => s.PassId).Distinct().OrderBy(x => x).ToList();

        Assert.Equal([4, 5], ringIds);
    }

    [Fact]
    public void Full_EachRingPass_HasExactly8Steps()
    {
        int segments = 3;
        var steps    = _sut.GenerateSteps(Def(segments, UnionJackRings.Full));

        for (int pass = 4; pass <= 3 + segments; pass++)
            Assert.Equal(8, steps.Count(s => s.PassId == pass));
    }

    // ── Node positions ────────────────────────────────────────────────────────

    [Fact]
    public void CenterNodePosition_IsPlateCenter()
    {
        double w = 1200.0, h = 800.0;
        var def  = Def(2, UnionJackRings.None, w, h);
        var step = _sut.GenerateSteps(def).First(s => s.ToNodeId == "center");

        var (x, y) = _sut.GetToNodePosition(step, def);

        Assert.Equal(w / 2.0, x, precision: 9);
        Assert.Equal(h / 2.0, y, precision: 9);
    }

    [Fact]
    public void ETipPosition_IsAtPlateRightEdge_WhenSegmentsReachEdge()
    {
        double w = 1200.0, h = 800.0;
        int    s = 2;
        var    def  = Def(s, UnionJackRings.None, w, h);
        var    step = _sut.GenerateSteps(def).Last(s2 => s2.PassId == 0);

        var (x, y) = _sut.GetToNodePosition(step, def);

        Assert.Equal(w,     x, precision: 9);
        Assert.Equal(h / 2, y, precision: 9);
    }

    [Fact]
    public void NodePositions_AreSymmetricAboutCenter()
    {
        double w = 1000.0, h = 1000.0;
        var def  = Def(2, UnionJackRings.None, w, h);

        var (exE, eyE) = UnionJackStrategy.NodePositionById("armE_seg1", def);
        var (exW, eyW) = UnionJackStrategy.NodePositionById("armW_seg1", def);

        Assert.Equal(w / 2 + (exE - w / 2), w / 2 - (exW - w / 2), precision: 9);
        Assert.Equal(eyE, eyW, precision: 9);
    }

    [Fact]
    public void NonSquarePlate_DiagonalNodePositions_UseCorrectAspectRatio()
    {
        double w = 1200.0, h = 600.0;
        var    def = Def(1, UnionJackRings.None, w, h);

        var (x, y) = UnionJackStrategy.NodePositionById("armSE_seg1", def);

        Assert.Equal(w, x, precision: 9);
        Assert.Equal(h, y, precision: 9);
    }

    // ── Primitive loops ───────────────────────────────────────────────────────

    [Fact]
    public void None_ProducesNoLoops()
    {
        var loops = _sut.GetPrimitiveLoopNodeIds(Def(2, UnionJackRings.None));
        Assert.Empty(loops);
    }

    [Fact]
    public void Circumference_Segments1_Produces8Triangles()
    {
        // With segments=1 the circumference ring sits at r=1 and touches center —
        // all triangle edges are single measured steps.
        var loops = _sut.GetPrimitiveLoopNodeIds(Def(1, UnionJackRings.Circumference));

        Assert.Equal(8, loops.Count);
        Assert.All(loops, l => Assert.Equal(3, l.Count));
    }

    [Fact]
    public void Circumference_SegmentsGt1_ProducesNoUnitLoops()
    {
        // With segments > 1 the ring sits at r=segments; the triangle edges between
        // inner diagonal nodes and outer cardinal nodes are not single steps.
        var loops = _sut.GetPrimitiveLoopNodeIds(Def(2, UnionJackRings.Circumference));
        Assert.Empty(loops);
    }

    [Fact]
    public void Full_Segments1_Produces8Triangles()
    {
        var loops = _sut.GetPrimitiveLoopNodeIds(Def(1, UnionJackRings.Full));

        Assert.Equal(8, loops.Count);
        Assert.All(loops, l => Assert.Equal(3, l.Count));
    }

    [Fact]
    public void Full_Segments2_Produces24Loops()
    {
        // 8×2 = 16 triangles + 8×1 = 8 rectangles = 24
        var loops = _sut.GetPrimitiveLoopNodeIds(Def(2, UnionJackRings.Full));

        Assert.Equal(24, loops.Count);
        Assert.Equal(16, loops.Count(l => l.Count == 3));
        Assert.Equal(8,  loops.Count(l => l.Count == 4));
    }

    // ── Instruction text ──────────────────────────────────────────────────────

    [Fact]
    public void InstructionText_IsNonEmptyForAllSteps()
    {
        var steps = _sut.GenerateSteps(Def(2, UnionJackRings.Full));
        Assert.All(steps, s => Assert.False(string.IsNullOrEmpty(s.InstructionText)));
    }

    // ── Backward-compatibility: legacy numeric rings parameter ────────────────

    [Theory]
    [InlineData(0, UnionJackRings.None)]
    [InlineData(1, UnionJackRings.Full)]
    [InlineData(3, UnionJackRings.Full)]
    public void ParseRingsOption_LegacyInteger_MapsCorrectly(int legacyValue, UnionJackRings expected)
    {
        var result = UnionJackStrategy.ParseRingsOption((object)legacyValue);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("None",          UnionJackRings.None)]
    [InlineData("Circumference", UnionJackRings.Circumference)]
    [InlineData("Full",          UnionJackRings.Full)]
    [InlineData("none",          UnionJackRings.None)]         // case-insensitive
    [InlineData("FULL",          UnionJackRings.Full)]
    public void ParseRingsOption_StringValues_ParseCorrectly(string input, UnionJackRings expected)
    {
        var result = UnionJackStrategy.ParseRingsOption(input);
        Assert.Equal(expected, result);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public void ZeroSegments_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.GenerateSteps(Def(0, UnionJackRings.None)));
    }
}
