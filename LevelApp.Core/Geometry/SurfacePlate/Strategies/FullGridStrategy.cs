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
/// Column pass — continues seamlessly where the row pass ended (bottom right for an odd number
/// of rows, bottom left for an even number): the first column is measured upwards from there,
/// the next one downwards, and so on across the plate. For 3 rows × 3 columns (row pass ends
/// bottom right):
///   Col 2 (North): (2,rows-1)→ … →(2,1)
///   Col 1 (South): (1,0)→ … →(1,rows-2)
///   Col 0 (North): (0,rows-1)→ … →(0,1)
///
/// GridCol/GridRow on each step is the "from" endpoint; Orientation points to the "to" endpoint.
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
        int index  = 0;
        int passId = 0;

        // ── Row pass ──────────────────────────────────────────────────────────
        for (int row = 0; row < rows; row++, passId++)
        {
            if (row % 2 == 0)
            {
                // Even rows: left → right (East)
                for (int col = 0; col < cols - 1; col++)
                    steps.Add(Make(index++, col, row, Orientation.East, passId));
            }
            else
            {
                // Odd rows: right → left (West); gridCol is the right endpoint
                for (int col = cols - 1; col >= 1; col--)
                    steps.Add(Make(index++, col, row, Orientation.West, passId));
            }
        }

        // ── Column pass ───────────────────────────────────────────────────────
        // Continues where the row pass ended: the last row is traversed East when the number of
        // rows is odd (the instrument ends at the bottom right) and West when it is even (bottom
        // left). The first column is that end column, measured upwards (North) from the bottom;
        // the next column is measured downwards (South), and so on towards the opposite side.
        bool startRight = rows % 2 == 1;

        for (int k = 0; k < cols; k++, passId++)
        {
            int col = startRight ? cols - 1 - k : k;

            if (k % 2 == 0)
            {
                // bottom → top (North); gridRow is the bottom endpoint
                for (int row = rows - 1; row >= 1; row--)
                    steps.Add(Make(index++, col, row, Orientation.North, passId));
            }
            else
            {
                // top → bottom (South)
                for (int row = 0; row < rows - 1; row++)
                    steps.Add(Make(index++, col, row, Orientation.South, passId));
            }
        }

        return steps.AsReadOnly();
    }

    // ── IMeasurementStrategy position methods ─────────────────────────────────

    public (double X, double Y) GetNodePosition(MeasurementStep step, ObjectDefinition definition)
    {
        int cols = Convert.ToInt32(definition.Parameters["columnsCount"]);
        int rows = Convert.ToInt32(definition.Parameters["rowsCount"]);
        double widthMm  = Convert.ToDouble(definition.Parameters["widthMm"]);
        double heightMm = Convert.ToDouble(definition.Parameters["heightMm"]);
        return (step.GridCol * widthMm / (cols - 1),
                step.GridRow * heightMm / (rows - 1));
    }

    public (double X, double Y) GetToNodePosition(MeasurementStep step, ObjectDefinition definition)
    {
        int cols = Convert.ToInt32(definition.Parameters["columnsCount"]);
        int rows = Convert.ToInt32(definition.Parameters["rowsCount"]);
        double widthMm  = Convert.ToDouble(definition.Parameters["widthMm"]);
        double heightMm = Convert.ToDouble(definition.Parameters["heightMm"]);

        (int toCol, int toRow) = step.Orientation switch
        {
            Orientation.East  => (step.GridCol + 1, step.GridRow),
            Orientation.West  => (step.GridCol - 1, step.GridRow),
            Orientation.South => (step.GridCol,     step.GridRow + 1),
            Orientation.North => (step.GridCol,     step.GridRow - 1),
            _ => throw new ArgumentException($"Unexpected orientation {step.Orientation} on Full Grid step.")
        };

        return (toCol * widthMm / (cols - 1),
                toRow * heightMm / (rows - 1));
    }

    public IReadOnlyList<IReadOnlyList<string>> GetPrimitiveLoopNodeIds(ObjectDefinition definition)
    {
        int cols = Convert.ToInt32(definition.Parameters["columnsCount"]);
        int rows = Convert.ToInt32(definition.Parameters["rowsCount"]);

        // One unit rectangle per interior crossing: (C-1)×(R-1) loops.
        // Clockwise (screen y-down): top-left → top-right → bottom-right → bottom-left.
        var loops = new List<IReadOnlyList<string>>((cols - 1) * (rows - 1));
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
                loops.Add(new[]
                {
                    NodeId(c,     r),
                    NodeId(c + 1, r),
                    NodeId(c + 1, r + 1),
                    NodeId(c,     r + 1)
                });

        return loops;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NodeId(int col, int row) => $"col{col}_row{row}";

    private static MeasurementStep Make(int index, int col, int row, Orientation orientation, int passId)
    {
        (int toCol, int toRow) = orientation switch
        {
            Orientation.East  => (col + 1, row),
            Orientation.West  => (col - 1, row),
            Orientation.South => (col,     row + 1),
            Orientation.North => (col,     row - 1),
            _ => (col, row)
        };

        return new MeasurementStep
        {
            Index           = index,
            GridCol         = col,
            GridRow         = row,
            Orientation     = orientation,
            PassId          = passId,
            NodeId          = NodeId(col, row),
            ToNodeId        = NodeId(toCol, toRow),
            InstructionText = BuildInstruction(col, row, orientation)
        };
    }

    private static string BuildInstruction(int col, int row, Orientation orientation)
    {
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
