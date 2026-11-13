using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

public class FullGridStrategyTests
{
    private readonly FullGridStrategy _sut = new();

    private static ObjectDefinition Def(int cols, int rows) => new()
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

    // ── Row-pass boustrophedon ─────────────────────────────────────────────────

    [Theory]
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    public void RowPass_EvenRowsGoEast_OddRowsGoWest(int cols, int rows)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows))
                        .Where(s => s.Orientation is Orientation.East or Orientation.West);

        foreach (var step in steps)
        {
            var expected = step.GridRow % 2 == 0 ? Orientation.East : Orientation.West;
            Assert.Equal(expected, step.Orientation);
        }
    }

    [Fact]
    public void RowPass_ColSequenceIsBoustrophedon()
    {
        int cols = 4, rows = 3;
        var steps = _sut.GenerateSteps(Def(cols, rows));

        // Row 0 (East): gridCol should increase 0 → cols-2
        var row0Cols = steps
            .Where(s => s.GridRow == 0 && s.Orientation == Orientation.East)
            .Select(s => s.GridCol).ToList();
        Assert.Equal(Enumerable.Range(0, cols - 1).ToList(), row0Cols);

        // Row 1 (West): gridCol should decrease cols-1 → 1
        var row1Cols = steps
            .Where(s => s.GridRow == 1 && s.Orientation == Orientation.West)
            .Select(s => s.GridCol).ToList();
        Assert.Equal(Enumerable.Range(1, cols - 1).Reverse().ToList(), row1Cols);
    }

    // ── Column-pass boustrophedon ──────────────────────────────────────────────

    [Theory]
    [InlineData(3, 3)]   // odd rows: the row pass ends bottom right
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    [InlineData(4, 4)]   // even rows: the row pass ends bottom left
    [InlineData(5, 2)]
    public void ColumnPass_StartsWhereTheRowPassEnded_MeasuringUpwards(int cols, int rows)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows));
        var lastRowStep = steps.Last(s => s.Orientation is Orientation.East or Orientation.West);
        var firstColumnStep = steps.First(s => s.Orientation is Orientation.North or Orientation.South);

        // The instrument ends the row pass at the to-node of the last row step ...
        Assert.Equal(lastRowStep.ToNodeId, firstColumnStep.NodeId);
        // ... on the bottom row, and the column pass goes up from there.
        Assert.Equal(rows - 1, firstColumnStep.GridRow);
        Assert.Equal(Orientation.North, firstColumnStep.Orientation);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    [InlineData(4, 4)]
    public void ColumnPass_AlternatesNorthAndSouth_AcrossTheWholePlate(int cols, int rows)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows))
                        .Where(s => s.Orientation is Orientation.North or Orientation.South)
                        .ToList();

        // Columns in the order they are measured.
        var columns = steps.Select(s => s.GridCol).Distinct().ToList();
        Assert.Equal(cols, columns.Count);
        bool descending = columns[0] > columns[^1];
        Assert.Equal(
            descending ? Enumerable.Range(0, cols).Reverse().ToList() : Enumerable.Range(0, cols).ToList(),
            columns);

        for (int k = 0; k < columns.Count; k++)
        {
            var expected = k % 2 == 0 ? Orientation.North : Orientation.South;
            Assert.All(steps.Where(s => s.GridCol == columns[k]), s => Assert.Equal(expected, s.Orientation));
        }
    }

    [Fact]
    public void ColumnPass_RowSequenceWithinAColumn()
    {
        int cols = 3, rows = 3; // odd rows: the first column is the right one (col 2), measured upwards
        var steps = _sut.GenerateSteps(Def(cols, rows));

        var col2Rows = steps
            .Where(s => s.GridCol == 2 && s.Orientation == Orientation.North)
            .Select(s => s.GridRow).ToList();
        Assert.Equal(Enumerable.Range(1, rows - 1).Reverse().ToList(), col2Rows);

        var col1Rows = steps
            .Where(s => s.GridCol == 1 && s.Orientation == Orientation.South)
            .Select(s => s.GridRow).ToList();
        Assert.Equal(Enumerable.Range(0, rows - 1).ToList(), col1Rows);
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
