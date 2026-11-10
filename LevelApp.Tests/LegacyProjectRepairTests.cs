using LevelApp.Core.Geometry;
using LevelApp.Core.Models;
using LevelApp.Core.Serialization;
using Xunit;

namespace LevelApp.Tests;

/// <summary>Early files have no node ids on their steps; loading must rebuild them.</summary>
public class LegacyProjectRepairTests
{
    private static Project LegacyProject(int cols, int rows, int declaredCols, int declaredRows,
                                         out List<MeasurementStep> original)
    {
        var def = new ObjectDefinition
        {
            GeometryModuleId = "SurfacePlate",
            Parameters = new Dictionary<string, object>
            {
                ["widthMm"] = 600.0, ["heightMm"] = 400.0,
                ["columnsCount"] = cols, ["rowsCount"] = rows,
            },
        };
        var strategy = StrategyFactory.Create("FullGrid");
        original = strategy.GenerateSteps(def).Select(s => new MeasurementStep
        {
            Index = s.Index, GridCol = s.GridCol, GridRow = s.GridRow, Orientation = s.Orientation,
            NodeId = s.NodeId, ToNodeId = s.ToNodeId, PassId = s.PassId, Reading = 0.001 * (s.Index % 3),
        }).ToList();

        // What an early version wrote: no node ids, a possibly different declared grid size.
        var legacySteps = original.Select(s => new MeasurementStep
        {
            Index = s.Index, GridCol = s.GridCol, GridRow = s.GridRow, Orientation = s.Orientation,
            Reading = s.Reading,
        }).ToList();
        def.Parameters["columnsCount"] = declaredCols;
        def.Parameters["rowsCount"] = declaredRows;

        return new Project
        {
            Name = "legacy",
            ObjectDefinition = def,
            Measurements =
            [
                new MeasurementSession
                {
                    StrategyId = "FullGrid",
                    InitialRound = new MeasurementRound { Steps = legacySteps },
                },
            ],
        };
    }

    [Fact]
    public void MissingNodeIdsAndPassIds_AreRebuilt_AndTheSurfaceHasAllNodes()
    {
        var project = LegacyProject(4, 3, 4, 3, out var original);

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        var steps = loaded.Measurements[0].InitialRound.Steps;

        Assert.Equal(original.Select(s => s.NodeId), steps.Select(s => s.NodeId));
        Assert.Equal(original.Select(s => s.ToNodeId), steps.Select(s => s.ToNodeId));
        Assert.Equal(original.Select(s => s.PassId), steps.Select(s => s.PassId));

        var strategy = StrategyFactory.Create("FullGrid");
        var parameters = new CalculationParameters();
        var result = CalculatorFactory.Create(parameters.MethodId, strategy)
            .Calculate(steps, loaded.ObjectDefinition, parameters);
        Assert.Equal(12, result.NodeHeights.Count);
    }

    [Fact]
    public void DeclaredSegmentCounts_AreWidenedToTheNodesOfTheSteps()
    {
        // The file says 3 x 2 (segments) but its steps use 4 x 3 nodes.
        var project = LegacyProject(4, 3, 3, 2, out _);

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.Equal(4, Convert.ToInt32(loaded.ObjectDefinition.Parameters["columnsCount"]));
        Assert.Equal(3, Convert.ToInt32(loaded.ObjectDefinition.Parameters["rowsCount"]));
    }

    [Fact]
    public void CurrentFiles_AreLeftAlone()
    {
        var project = LegacyProject(4, 3, 4, 3, out var original);
        project.Measurements[0].InitialRound.Steps = original;

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.Equal(original.Select(s => s.NodeId), loaded.Measurements[0].InitialRound.Steps.Select(s => s.NodeId));
        Assert.Equal(4, Convert.ToInt32(loaded.ObjectDefinition.Parameters["columnsCount"]));
    }
}
