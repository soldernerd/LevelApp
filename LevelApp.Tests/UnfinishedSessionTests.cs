using LevelApp.Core.Models;
using LevelApp.Core.Serialization;
using Xunit;

namespace LevelApp.Tests;

/// <summary>A measurement that was started and not finished must survive a save/load and be resumable.</summary>
public class UnfinishedSessionTests
{
    private static MeasurementSession Session(int steps, int withReading)
    {
        var round = new MeasurementRound();
        for (int i = 0; i < steps; i++)
            round.Steps.Add(new MeasurementStep
            {
                Index = i, GridCol = i, GridRow = 0, Orientation = Orientation.East,
                NodeId = $"col{i}_row0", ToNodeId = $"col{i + 1}_row0",
                Reading = i < withReading ? 0.001 * i : null,
            });
        return new MeasurementSession { Label = "Measurement 1", StrategyId = "FullGrid", InitialRound = round };
    }

    [Fact]
    public void PartlyMeasuredSession_IsUnfinished_AndCountsItsReadings()
    {
        var s = Session(12, 8);

        Assert.True(s.IsUnfinished());
        Assert.Equal(8, s.ReadingCount());
    }

    [Fact]
    public void SessionWithAResult_IsFinished()
    {
        var s = Session(4, 4);
        s.InitialRound.Result = new SurfaceResult();

        Assert.False(s.IsUnfinished());
    }

    [Fact]
    public void SessionWithoutSteps_IsNotUnfinished() =>
        Assert.False(new MeasurementSession().IsUnfinished());

    [Fact]
    public void ThePartialReadings_SurviveASaveAndLoad_InTheirPlaces()
    {
        var project = new Project
        {
            Name = "interrupted",
            ObjectDefinition = new ObjectDefinition { GeometryModuleId = "SurfacePlate" },
            Measurements = [Session(12, 8)],
        };

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        var session = loaded.Measurements.Single();

        Assert.True(session.IsUnfinished());
        Assert.Equal(8, session.ReadingCount());
        // The first step without a reading is where the measurement continues.
        Assert.Equal(8, session.InitialRound.Steps.FindIndex(s => !s.Reading.HasValue));
        Assert.Equal(0.007, session.InitialRound.Steps[7].Reading!.Value, 9);
    }

    [Fact]
    public void InstrumentNameAndDetails_AreSavedWithTheMeasurement()
    {
        var session = Session(2, 2);
        session.InstrumentName = "Leveltronic — Prototyp Grün (BLE)";
        session.InstrumentDetails = "serial 6DA98059 · firmware 0.15.0 · API 3.1";
        var project = new Project
        {
            Name = "meta",
            ObjectDefinition = new ObjectDefinition { GeometryModuleId = "SurfacePlate" },
            Measurements = [session],
        };

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)).Measurements.Single();

        Assert.Equal(session.InstrumentName, loaded.InstrumentName);
        Assert.Equal(session.InstrumentDetails, loaded.InstrumentDetails);
    }
}
