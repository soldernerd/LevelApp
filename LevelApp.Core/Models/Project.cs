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
}
