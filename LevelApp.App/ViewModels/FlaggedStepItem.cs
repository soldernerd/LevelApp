namespace LevelApp.App.ViewModels;

public sealed class FlaggedStepItem
{
    public int    StepIndex   { get; init; }
    public int    GridCol     { get; init; }
    public int    GridRow     { get; init; }
    public string Orientation { get; init; } = string.Empty;
    public double Residual    { get; init; }

    public string Label =>
        $"Step {StepIndex + 1}  ({GridCol},{GridRow}) {Orientation}  " +
        $"r = {Residual * 1000:+0.00;-0.00} µm";
}
