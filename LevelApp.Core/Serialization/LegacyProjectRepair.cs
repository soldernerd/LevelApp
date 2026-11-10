using LevelApp.Core.Models;

namespace LevelApp.Core.Serialization;

/// <summary>
/// Brings projects written by early versions up to the current model after they are read.
/// <para>
/// Early Full Grid files carry only <c>gridCol</c>, <c>gridRow</c> and <c>orientation</c> on each
/// step. The calculators and the plots need <see cref="MeasurementStep.NodeId"/>,
/// <see cref="MeasurementStep.ToNodeId"/> and <see cref="MeasurementStep.PassId"/> (which the
/// strategy sets when it generates steps), so without them a result has a single node and the
/// 3D surface plot is empty. Some of those files also stored the number of grid <i>segments</i> in
/// <c>columnsCount</c>/<c>rowsCount</c>; the current meaning is the number of <i>nodes</i>, so a
/// definition smaller than the steps' grid is widened to fit the steps.
/// </para>
/// </summary>
public static class LegacyProjectRepair
{
    public static void Apply(Project project)
    {
        var definition = project.ObjectDefinition;
        if (definition.GeometryModuleId != "SurfacePlate") return;

        foreach (var session in project.Measurements)
        {
            if (session.StrategyId != "FullGrid") continue;

            var steps = session.InitialRound.Steps;
            if (steps.Count == 0 || steps.All(s => s.NodeId.Length > 0)) continue;

            WidenGridToSteps(definition, steps);
            AssignNodes(steps);
        }
    }

    private static void WidenGridToSteps(ObjectDefinition definition, List<MeasurementStep> steps)
    {
        int maxCol = steps.Max(s => s.GridCol + (s.Orientation == Orientation.East ? 1 : 0));
        int maxRow = steps.Max(s => s.GridRow + (s.Orientation == Orientation.South ? 1 : 0));

        int cols = definition.Parameters.TryGetValue("columnsCount", out var c) ? Convert.ToInt32(c) : 0;
        int rows = definition.Parameters.TryGetValue("rowsCount", out var r) ? Convert.ToInt32(r) : 0;

        if (maxCol >= cols) definition.Parameters["columnsCount"] = maxCol + 1;
        if (maxRow >= rows) definition.Parameters["rowsCount"] = maxRow + 1;
    }

    private static void AssignNodes(List<MeasurementStep> steps)
    {
        int passId = -1;
        (bool IsRow, int Line)? previous = null;

        foreach (var step in steps)
        {
            (int toCol, int toRow) = step.Orientation switch
            {
                Orientation.East  => (step.GridCol + 1, step.GridRow),
                Orientation.West  => (step.GridCol - 1, step.GridRow),
                Orientation.South => (step.GridCol,     step.GridRow + 1),
                Orientation.North => (step.GridCol,     step.GridRow - 1),
                _ => (step.GridCol, step.GridRow)
            };

            step.NodeId   = $"col{step.GridCol}_row{step.GridRow}";
            step.ToNodeId = $"col{toCol}_row{toRow}";

            // A pass is one row (East/West) or one column (North/South), as the strategy generates them.
            bool isRow = step.Orientation is Orientation.East or Orientation.West;
            var key = (isRow, isRow ? step.GridRow : step.GridCol);
            if (previous != key) passId++;
            previous = key;
            step.PassId = passId;
        }
    }
}
