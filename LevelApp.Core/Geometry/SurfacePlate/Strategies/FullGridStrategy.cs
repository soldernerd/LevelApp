using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.SurfacePlate.Strategies;

/// <summary>
/// Generates the Full Grid step sequence using a boustrophedon (serpentine) traversal.
///
/// Row pass — traverses every row left-to-right then right-to-left alternately:
///   Row 0 (East):  (0,0)→(1,0)→ … →(cols-2,0)
///   Row 1 (West):  (cols-1,1)→ … →(1,1)
///   Row 2 (East):  (0,2)→ … →(cols-2,2)  …
///
/// Column pass — traverses every column top-to-bottom then bottom-to-top alternately:
///   Col 0 (South): (0,0)→(0,1)→ … →(0,rows-2)
///   Col 1 (North): (1,rows-1)→ … →(1,1)
///   Col 2 (South): (2,0)→ … →(2,rows-2)  …
///
/// GridCol/GridRow on each step is the "from" endpoint; Orientation points to the "to" endpoint.
/// This means every interior grid point is visited twice (once in each axis pass),
/// providing the crossing redundancy required by the least-squares solver.
///
/// Total steps = rows × (cols − 1) + cols × (rows − 1)
/// </summary>
public sealed class FullGridStrategy : IMeasurementStrategy
{
    public string StrategyId => "FullGrid";
    public string DisplayName => "Full Grid";

    public IReadOnlyList<MeasurementStep> GenerateSteps(ObjectDefinition definition)
    {
        int cols = Convert.ToInt32(definition.Parameters["columnsCount"]);
        int rows = Convert.ToInt32(definition.Parameters["rowsCount"]);

        if (cols < 2) throw new ArgumentException("columnsCount must be at least 2.", nameof(definition));
        if (rows < 2) throw new ArgumentException("rowsCount must be at least 2.", nameof(definition));

        var steps = new List<MeasurementStep>(rows * (cols - 1) + cols * (rows - 1));
        int index = 0;

        // ── Row pass ──────────────────────────────────────────────────────────
        for (int row = 0; row < rows; row++)
        {
            if (row % 2 == 0)
            {
                // Even rows: left → right (East)
                for (int col = 0; col < cols - 1; col++)
                    steps.Add(Make(index++, col, row, Orientation.East));
            }
            else
            {
                // Odd rows: right → left (West); gridCol is the right endpoint
                for (int col = cols - 1; col >= 1; col--)
                    steps.Add(Make(index++, col, row, Orientation.West));
            }
        }

        // ── Column pass ───────────────────────────────────────────────────────
        for (int col = 0; col < cols; col++)
        {
            if (col % 2 == 0)
            {
                // Even columns: top → bottom (South)
                for (int row = 0; row < rows - 1; row++)
                    steps.Add(Make(index++, col, row, Orientation.South));
            }
            else
            {
                // Odd columns: bottom → top (North); gridRow is the bottom endpoint
                for (int row = rows - 1; row >= 1; row--)
                    steps.Add(Make(index++, col, row, Orientation.North));
            }
        }

        return steps.AsReadOnly();
    }

    private static MeasurementStep Make(int index, int col, int row, Orientation orientation)
    {
        return new MeasurementStep
        {
            Index = index,
            GridCol = col,
            GridRow = row,
            Orientation = orientation,
            InstructionText = BuildInstruction(col, row, orientation)
        };
    }

    private static string BuildInstruction(int col, int row, Orientation orientation)
    {
        // Use 1-based numbers in operator-facing text.
        return orientation switch
        {
            Orientation.East  => $"Row pass — row {row + 1}, instrument at column {col + 1} → {col + 2}, facing East",
            Orientation.West  => $"Row pass — row {row + 1}, instrument at column {col + 1} → {col}, facing West",
            Orientation.South => $"Column pass — column {col + 1}, instrument at row {row + 1} → {row + 2}, facing South",
            Orientation.North => $"Column pass — column {col + 1}, instrument at row {row + 1} → {row}, facing North",
            _                 => string.Empty
        };
    }
}
