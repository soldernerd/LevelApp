using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.SurfacePlate.Strategies;

/// <summary>
/// Generates the Full Grid step sequence. Every pass runs in the same direction, so the instrument is
/// never turned by 180° (a differential measurement needs it to stay as it is): row passes always face
/// East, column passes always face South. Between two passes the instrument is only carried back to the
/// start of the next row / column, not turned.
///
/// Row pass — every row left → right (East), top row first:
///   Row 0 (East):  (0,0)→(1,0)→ … →(cols-1,0)
///   Row 1 (East):  (0,1)→ … →(cols-1,1)  …
///
/// Column pass — every column top → bottom (South), left column first:
///   Col 0 (South): (0,0)→(0,1)→ … →(0,rows-1)
///   Col 1 (South): (1,0)→ … →(1,rows-1)  …
///
/// With the optional <c>reverseDirection</c> parameter the whole walk is mirrored through the plate centre:
/// rows face West, starting with the bottom row, then columns face North, starting with the right column.
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

        bool reversed = WalkDirection.IsReversed(definition);

        var steps = new List<MeasurementStep>(rows * (cols - 1) + cols * (rows - 1));
        int index  = 0;
        int passId = 0;

        // The walk is generated in the normal direction; a reversed walk is its point mirror:
        // a normal step from (c,r) facing East becomes a step from (cols-1-c, rows-1-r) facing West.
        void Add(int col, int row, Orientation orientation)
        {
            if (reversed)
                steps.Add(Make(index++, cols - 1 - col, rows - 1 - row, WalkDirection.Opposite(orientation), passId));
            else
                steps.Add(Make(index++, col, row, orientation, passId));
        }

        // ── Row pass: every row East ──────────────────────────────────────────
        for (int row = 0; row < rows; row++, passId++)
            for (int col = 0; col < cols - 1; col++)
                Add(col, row, Orientation.East);

        // ── Column pass: every column South ───────────────────────────────────
        for (int col = 0; col < cols; col++, passId++)
            for (int row = 0; row < rows - 1; row++)
                Add(col, row, Orientation.South);

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
        // The closure calculation adds a step forward or backward depending on how it was measured,
        // so the loops are the same for either walking direction.
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
