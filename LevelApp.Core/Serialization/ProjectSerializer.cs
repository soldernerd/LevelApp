using System.Text.Json;
using LevelApp.Core.Models;

namespace LevelApp.Core.Serialization;

/// <summary>
/// Serialises and deserialises <see cref="Project"/> to/from the
/// <c>.levelproj</c> JSON file format.
/// </summary>
public static class ProjectSerializer
{
    public const string CurrentSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new ObjectValueConverter() }
    };

    /// <summary>Serialises <paramref name="project"/> to a JSON string.</summary>
    public static string Serialize(Project project)
    {
        var file = new LevelProjectFile
        {
            SchemaVersion = CurrentSchemaVersion,
            Project       = project
        };
        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>
    /// Deserialises a JSON string produced by <see cref="Serialize"/>.
    /// Throws <see cref="NotSupportedException"/> for unrecognised schema versions.
    /// </summary>
    public static Project Deserialize(string json)
    {
        var file = JsonSerializer.Deserialize<LevelProjectFile>(json, Options)
            ?? throw new JsonException("Root object is null.");

        if (file.SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException(
                $"Unsupported schema version '{file.SchemaVersion}'. " +
                $"Expected '{CurrentSchemaVersion}'.");

        return file.Project ?? throw new JsonException("'project' field is missing or null.");
    }
}

/// <summary>Root wrapper that carries the schema version alongside the project.</summary>
internal sealed class LevelProjectFile
{
    public string   SchemaVersion { get; set; } = ProjectSerializer.CurrentSchemaVersion;
    public Project? Project       { get; set; }
}
