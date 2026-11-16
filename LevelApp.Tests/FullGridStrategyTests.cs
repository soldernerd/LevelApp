using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

public class FullGridStrategyTests
{
    private readonly FullGridStrategy _sut = new();

    private static ObjectDefinition Def(int cols, int rows, bool reverse = false)
    {
        var def = new ObjectDefinition
        {
            GeometryModuleId = "SurfacePlate",
            Parameters = new Dictionary<string, object>
            {
                ["columnsCount"] = cols,
                ["rowsCount"]    = rows,
                ["widthMm"]      = 1000.0,
                ["heightMm"]     = 1000.0
            }
        };
        if (reverse) def.Parameters["reverseDirection"] = true;
        return def;
    }

    // ── Step count ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2, 2,  4)]   // 2*(2-1) + 2*(2-1) = 4
    [InlineData(3, 3, 12)]   // 3*2 + 3*2 = 12
    [InlineData(4, 3, 17)]   // 3*3 + 4*2 = 17
    [InlineData(8, 5, 67)]   // 5*7 + 8*4 = 67  (example from architecture doc)
    public void StepCount_MatchesFormula(int cols, int rows, int expected)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows));
        Assert.Equal(expected, steps.Count);
    }

    // ── Index sequence ────────────────────────────────────────────────────────

    [Fact]
    public void Indices_AreSequentialFromZero()
    {
        var steps = _sut.GenerateSteps(Def(5, 4));
        for (int i = 0; i < steps.Count; i++)
            Assert.Equal(i, steps[i].Index);
    }

    // ── One-way passes (the instrument is never turned by 180°) ───────────────

    [Theory]
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    [InlineData(2, 2)]
    public void RowPass_EveryRowGoesEast_ColumnPass_EveryColumnGoesSouth(int cols, int rows)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows));

        Assert.Equal(rows * (cols - 1), steps.Count(s => s.Orientation == Orientation.East));
        Assert.Equal(cols * (rows - 1), steps.Count(s => s.Orientation == Orientation.South));
        Assert.Equal(steps.Count, steps.Count(s => s.Orientation is Orientation.East or Orientation.South));
    }

    [Fact]
    public void RowPass_RunsTopToBottom_EachRowLeftToRight()
    {
        int cols = 4, rows = 3;
        var steps = _sut.GenerateSteps(Def(cols, rows));

        var rowSteps = steps.Where(s => s.Orientation == Orientation.East).ToList();
        Assert.Equal(
            Enumerable.Range(0, rows).SelectMany(r => Enumerable.Range(0, cols - 1).Select(c => (c, r))).ToList(),
            rowSteps.Select(s => (s.GridCol, s.GridRow)).ToList());
    }

    [Fact]
    public void ColumnPass_RunsLeftToRight_EachColumnTopToBottom()
    {
        int cols = 3, rows = 4;
        var steps = _sut.GenerateSteps(Def(cols, rows));

        var colSteps = steps.Where(s => s.Orientation == Orientation.South).ToList();
        Assert.Equal(
            Enumerable.Range(0, cols).SelectMany(c => Enumerable.Range(0, rows - 1).Select(r => (c, r))).ToList(),
            colSteps.Select(s => (s.GridCol, s.GridRow)).ToList());
    }

    [Fact]
    public void RowPass_PrecedesColumnPass()
    {
        var steps = _sut.GenerateSteps(Def(5, 4));
        int lastRow  = steps.ToList().FindLastIndex(s => s.Orientation == Orientation.East);
        int firstCol = steps.ToList().FindIndex(s => s.Orientation == Orientation.South);
        Assert.True(lastRow < firstCol);
    }

    // ── Reversed direction ────────────────────────────────────────────────────

    [Theory]
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    [InlineData(2, 2)]
    public void Reversed_EveryRowGoesWest_EveryColumnGoesNorth(int cols, int rows)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows, reverse: true));

        Assert.Equal(rows * (cols - 1), steps.Count(s => s.Orientation == Orientation.West));
        Assert.Equal(cols * (rows - 1), steps.Count(s => s.Orientation == Orientation.North));
        Assert.Equal(steps.Count, steps.Count(s => s.Orientation is Orientation.West or Orientation.North));
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    public void Reversed_IsThePointMirrorOfTheNormalWalk(int cols, int rows)
    {
        var normal   = _sut.GenerateSteps(Def(cols, rows));
        var reversed = _sut.GenerateSteps(Def(cols, rows, reverse: true));

        Assert.Equal(normal.Count, reversed.Count);
        for (int i = 0; i < normal.Count; i++)
        {
            Assert.Equal(cols - 1 - normal[i].GridCol, reversed[i].GridCol);
            Assert.Equal(rows - 1 - normal[i].GridRow, reversed[i].GridRow);
            Assert.Equal(WalkDirection.Opposite(normal[i].Orientation), reversed[i].Orientation);
            Assert.Equal(normal[i].PassId, reversed[i].PassId);
        }
    }

    [Fact]
    public void Reversed_StartsAtTheBottomRightCorner()
    {
        var first = _sut.GenerateSteps(Def(4, 3, reverse: true))[0];
        Assert.Equal(3, first.GridCol);
        Assert.Equal(2, first.GridRow);
        Assert.Equal(Orientation.West, first.Orientation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameEdges_AreMeasuredEitherWay(bool reverse)
    {
        // Both directions measure the same set of grid edges (as unordered pairs of nodes).
        var edges = _sut.GenerateSteps(Def(5, 4, reverse))
                        .Select(s => string.CompareOrdinal(s.NodeId, s.ToNodeId) < 0
                            ? (s.NodeId, s.ToNodeId) : (s.ToNodeId, s.NodeId))
                        .OrderBy(e => e).ToList();
        var normal = _sut.GenerateSteps(Def(5, 4))
                        .Select(s => string.CompareOrdinal(s.NodeId, s.ToNodeId) < 0
                            ? (s.NodeId, s.ToNodeId) : (s.ToNodeId, s.NodeId))
                        .OrderBy(e => e).ToList();
        Assert.Equal(normal, edges);
    }

    [Fact]
    public void ReverseParameter_AcceptsStringAndDefaultsToFalse()
    {
        var def = Def(3, 3);
        Assert.False(WalkDirection.IsReversed(def));
        def.Parameters["reverseDirection"] = false;
        Assert.False(WalkDirection.IsReversed(def));
        def.Parameters["reverseDirection"] = true;
        Assert.True(WalkDirection.IsReversed(def));
        def.Parameters["reverseDirection"] = "true";
        Assert.True(WalkDirection.IsReversed(def));
    }

    // ── Never turned by 180° ──────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoTwoStepsFaceOppositeDirections(bool reverse)
    {
        var facing = _sut.GenerateSteps(Def(6, 5, reverse)).Select(s => s.Orientation).Distinct().ToList();
        foreach (var o in facing)
            Assert.DoesNotContain(WalkDirection.Opposite(o), facing);
    }

    // ── Grid bounds ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    public void AllFromAndToNodes_AreWithinGridBounds(int cols, int rows)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows));

        foreach (var step in steps)
        {
            Assert.InRange(step.GridCol, 0, cols - 1);
            Assert.InRange(step.GridRow, 0, rows - 1);

            (int toCol, int toRow) = step.Orientation switch
            {
                Orientation.East  => (step.GridCol + 1, step.GridRow),
                Orientation.West  => (step.GridCol - 1, step.GridRow),
                Orientation.South => (step.GridCol,     step.GridRow + 1),
                Orientation.North => (step.GridCol,     step.GridRow - 1),
                _ => throw new InvalidOperationException()
            };

            Assert.InRange(toCol, 0, cols - 1);
            Assert.InRange(toRow, 0, rows - 1);
        }
    }

    // ── Instruction text ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    public void InstructionText_IsNonEmptyForAllSteps(int cols, int rows)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows));
        Assert.All(steps, s => Assert.False(string.IsNullOrEmpty(s.InstructionText)));
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void TooSmallGrid_ThrowsArgumentException(int cols, int rows)
    {
        Assert.Throws<ArgumentException>(() => _sut.GenerateSteps(Def(cols, rows)));
    }
}
