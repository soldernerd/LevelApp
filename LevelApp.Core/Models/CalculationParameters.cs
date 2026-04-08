namespace LevelApp.Core.Models;

public class CalculationParameters
{
    public string MethodId { get; set; } = "LeastSquares";
    public double SigmaThreshold { get; set; } = 2.5;
    public bool AutoExcludeOutliers { get; set; } = true;
    public List<int> ManuallyExcludedStepIndices { get; set; } = [];
}
