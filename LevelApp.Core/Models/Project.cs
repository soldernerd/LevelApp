namespace LevelApp.Core.Models;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string Operator { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public ObjectDefinition ObjectDefinition { get; set; } = new();
    public List<MeasurementSession> Measurements { get; set; } = [];

    /// <summary>
    /// The object definition in effect for <paramref name="session"/>: the project's definition with the
    /// session's own <see cref="MeasurementSession.StrategyParameters"/> laid over it. Use this, not
    /// <see cref="ObjectDefinition"/>, wherever a session's steps, plot or result are worked with.
    /// </summary>
    public ObjectDefinition DefinitionFor(MeasurementSession session)
    {
        if (session.StrategyParameters is null) return ObjectDefinition;

        var merged = new Dictionary<string, object>(ObjectDefinition.Parameters);
        foreach (var (key, value) in session.StrategyParameters)
            merged[key] = value;

        return new ObjectDefinition
        {
            GeometryModuleId = session.StrategyId == "ParallelWays" ? "ParallelWays" : "SurfacePlate",
            Parameters       = merged
        };
    }
}
