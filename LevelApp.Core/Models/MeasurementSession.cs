namespace LevelApp.Core.Models;

public class MeasurementSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = string.Empty;
    public DateTime TakenAt { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string InstrumentId { get; set; } = string.Empty;
    public string StrategyId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public MeasurementRound InitialRound { get; set; } = new();
    public List<CorrectionRound> Corrections { get; set; } = [];
}
