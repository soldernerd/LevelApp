using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

public interface IGeometryModule
{
    string ModuleId { get; }
    string DisplayName { get; }
    IEnumerable<IMeasurementStrategy> AvailableStrategies { get; }
    IGeometryCalculator CreateCalculator(ObjectDefinition definition);
}
