using System.Text.Json;
using System.Text.Json.Serialization;
using LevelApp.Core.Models;

namespace LevelApp.Core.Serialization;

/// <summary>
/// Serialises and deserialises <see cref="Project"/> to/from the
/// <c>.levelproj</c> JSON file format.
/// </summary>
public static class ProjectSerializer
{
    /// <summary>
    /// Current schema version written to every new file.
    /// v1.1 adds MeasurementRound.ParallelWaysResult and MeasurementStep.PassPhase.
    /// v1.0 files are accepted on load — PassPhase defaults to NotApplicable
    /// and ParallelWaysResult defaults to null, so no migration is required.
    /// </summary>
    public const string CurrentSchemaVersion = "1.1";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new ObjectValueConverter(), new JsonStringEnumConverter() }
    };

    /// <summary>Serialises <paramref name="project"/> to a JSON string.</summary>
    public static string Serialize(Project project)
    {
        var file = new LevelProjectFile
        {
            SchemaVersion = CurrentSchemaVersion,
            AppVersion    = Core.AppVersion.Full,
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

        // Accept both the current version and the previous v1.0 format.
        // New fields (PassPhase, ParallelWaysResult) default correctly via JSON binding.
        if (file.SchemaVersion is not ("1.0" or "1.1"))
            throw new NotSupportedException(
                $"Unsupported schema version '{file.SchemaVersion}'. " +
                $"Expected '1.0' or '1.1'.");

        return file.Project ?? throw new JsonException("'project' field is missing or null.");
    }
}

/// <summary>Root wrapper that carries schema/app version alongside the project.</summary>
internal sealed class LevelProjectFile
{
    public string   SchemaVersion { get; set; } = ProjectSerializer.CurrentSchemaVersion;
    public string?  AppVersion    { get; set; }
    public Project? Project       { get; set; }
}
