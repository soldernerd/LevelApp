using LevelApp.Core.Geometry;
using LevelApp.Core.Models;
using LevelApp.Core.Serialization;
using Xunit;

namespace LevelApp.Tests;

/// <summary>One project, several measurements with different strategies and sizes.</summary>
public class MixedStrategyProjectTests
{
    private static Project TwoStrategyProject()
    {
        var definition = new ObjectDefinition
        {
            GeometryModuleId = "SurfacePlate",
            Parameters = new Dictionary<string, object>
            {
                ["widthMm"] = 600.0, ["heightMm"] = 400.0, ["columnsCount"] = 3, ["rowsCount"] = 3,
            },
        };
        var project = new Project { Name = "mixed", ObjectDefinition = definition };

        var gridSteps = StrategyFactory.Create("FullGrid").GenerateSteps(definition).ToList();
        project.Measurements.Add(new MeasurementSession
        {
            Label = "Grid", StrategyId = "FullGrid",
            InitialRound = new MeasurementRound { Steps = gridSteps },
        });

        // Added later through the setup page: its own parameters, the project definition untouched.
        var jackParameters = new Dictionary<string, object>
        {
            ["widthMm"] = 600.0, ["heightMm"] = 400.0, ["segments"] = 3, ["rings"] = "Full",
        };
        var jackDefinition = new ObjectDefinition { GeometryModuleId = "SurfacePlate", Parameters = jackParameters };
        var jackSteps = StrategyFactory.Create("UnionJack").GenerateSteps(jackDefinition).ToList();
        project.Measurements.Add(new MeasurementSession
        {
            Label = "Jack", StrategyId = "UnionJack",
            StrategyParameters = new Dictionary<string, object>(jackParameters),
            InitialRound = new MeasurementRound { Steps = jackSteps },
        });
        return project;
    }

    [Fact]
    public void DefinitionFor_UsesTheSessionsOwnParameters_WithoutInheritingTheProjectsGrid()
    {
        var project = TwoStrategyProject();

        var grid = project.DefinitionFor(project.Measurements[0]);
        var jack = project.DefinitionFor(project.Measurements[1]);

        Assert.Same(project.ObjectDefinition, grid);                       // no own parameters: the project's
        Assert.Equal(3, Convert.ToInt32(jack.Parameters["segments"]));
        // A Union Jack measurement must not look like a grid (the screens decide that from columnsCount).
        Assert.False(jack.Parameters.ContainsKey("columnsCount"));
        Assert.False(jack.Parameters.ContainsKey("rowsCount"));
        Assert.False(project.ObjectDefinition.Parameters.ContainsKey("segments"));
    }

    [Fact]
    public void BothMeasurements_SurviveASaveAndLoad_AndStayUsableWithTheirOwnStrategy()
    {
        var project = TwoStrategyProject();

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.Equal(2, loaded.Measurements.Count);
        Assert.Null(loaded.Measurements[0].StrategyParameters);
        Assert.NotNull(loaded.Measurements[1].StrategyParameters);

        // The Union Jack measurement can still generate its steps from the loaded definition.
        var jackSession = loaded.Measurements[1];
        var jackSteps = StrategyFactory.Create(jackSession.StrategyId).GenerateSteps(loaded.DefinitionFor(jackSession));
        Assert.Equal(jackSession.InitialRound.Steps.Count, jackSteps.Count);

        // And the grid one is unchanged.
        var gridSession = loaded.Measurements[0];
        Assert.Equal(12, StrategyFactory.Create("FullGrid").GenerateSteps(loaded.DefinitionFor(gridSession)).Count);
    }
}
