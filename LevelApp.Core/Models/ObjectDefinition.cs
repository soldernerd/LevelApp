namespace LevelApp.Core.Models;

public class ObjectDefinition
{
    public string GeometryModuleId { get; set; } = string.Empty;

    /// <summary>
    /// Flexible parameter bag. Each geometry module defines and interprets its own keys.
    /// Example keys for SurfacePlate: widthMm, heightMm, columnsCount, rowsCount.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = [];
}
