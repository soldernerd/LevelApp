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
    /// The object definition in effect for <paramref name="session"/>: the session's own
    /// <see cref="MeasurementSession.StrategyParameters"/> when it has them, else the project's definition.
    /// Use this, not <see cref="ObjectDefinition"/>, wherever a session's steps, plot or result are worked with.
    /// <para>
    /// The session's parameters replace the project's, they are not merged into them: a Union Jack
    /// measurement in a project that started as a Full Grid must not inherit <c>columnsCount</c> / <c>rowsCount</c>,
    /// or the screens would take it for a grid.
    /// </para>
    /// </summary>
    public ObjectDefinition DefinitionFor(MeasurementSession session)
    {
        if (session.StrategyParameters is null) return ObjectDefinition;

        return new ObjectDefinition
        {
            GeometryModuleId = session.StrategyId == "ParallelWays" ? "ParallelWays" : "SurfacePlate",
            Parameters       = new Dictionary<string, object>(session.StrategyParameters)
        };
    }
}
