namespace LevelApp.Core.Geometry.SurfacePlate.Strategies;

/// <summary>
/// Controls which ring closure passes are included in a Union Jack measurement.
/// </summary>
public enum UnionJackRings
{
    /// <summary>No ring passes — arm diagonals only (the bare Union Jack cross).</summary>
    None = 0,

    /// <summary>
    /// One ring pass along the outer perimeter only, connecting the eight arm tips.
    /// Adds a single rectangular circuit at r = segments.
    /// </summary>
    Circumference = 1,

    /// <summary>
    /// All interior ring levels from innermost to outermost (r = 1 … segments).
    /// Maximises the number of independent closure loops.
    /// </summary>
    Full = 2
}
