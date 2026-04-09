using System.Text.Json;
using System.Text.Json.Serialization;

namespace LevelApp.Core.Models;

public enum WaysOrientation { Horizontal, Vertical }

/// <summary>
/// Typed parameter bag for a Parallel Ways object definition.
/// Stored in <see cref="ObjectDefinition.Parameters"/> under the keys
/// "orientation", "referenceRailIndex", and "rails".
/// </summary>
public class ParallelWaysParameters
{
    public WaysOrientation Orientation { get; set; } = WaysOrientation.Horizontal;
    public int ReferenceRailIndex { get; set; } = 0;
    public List<RailDefinition> Rails { get; set; } = [];

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Extracts <see cref="ParallelWaysParameters"/> from an
    /// <see cref="ObjectDefinition.Parameters"/> dictionary.
    /// Handles both live <c>List&lt;RailDefinition&gt;</c> values (set by the ViewModel)
    /// and <c>JsonElement</c> values (produced by <see cref="Serialization.ObjectValueConverter"/>
    /// on deserialisation).
    /// </summary>
    public static ParallelWaysParameters From(Dictionary<string, object> parameters)
    {
        var result = new ParallelWaysParameters();

        if (parameters.TryGetValue("orientation", out var ori))
            result.Orientation = Enum.Parse<WaysOrientation>(ori.ToString()!);

        if (parameters.TryGetValue("referenceRailIndex", out var rri))
            result.ReferenceRailIndex = Convert.ToInt32(rri);

        if (parameters.TryGetValue("rails", out var railsObj))
        {
            result.Rails = railsObj is JsonElement je
                ? je.Deserialize<List<RailDefinition>>(_jsonOptions) ?? []
                : railsObj as List<RailDefinition> ?? [];
        }

        return result;
    }
}
