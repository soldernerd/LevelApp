namespace LevelApp.Instruments.Leveltronic.Calibration;

// The arithmetic of the phase and gain calibration wizards, as pure functions so it
// is unit-tested against the simulator's known truth. A port of the phone app's
// lib/devices/leveltronic/calibration/calibration_math.dart.
//
// Model (firmware Math/math_displacement.c): per batch and sensor u = S / D with
// D = A - B; a real tilt moves u along e^{j*phi}, phi being the sensor's signal
// phase. The firmware derotates by the stored phase, x = u * e^{-j*phiCal}, and
// reports tilt = (Re x - zero) / k.

/// <summary>Running mean / variance of a complex quantity (Welford).</summary>
public sealed class ComplexStats
{
    private double _m2Re, _m2Im;

    public int N { get; private set; }
    public double MeanRe { get; private set; }
    public double MeanIm { get; private set; }

    public void Reset()
    {
        N = 0;
        MeanRe = MeanIm = 0;
        _m2Re = _m2Im = 0;
    }

    public void Add(double re, double im)
    {
        N++;
        double dr = re - MeanRe;
        MeanRe += dr / N;
        _m2Re += dr * (re - MeanRe);
        double di = im - MeanIm;
        MeanIm += di / N;
        _m2Im += di * (im - MeanIm);
    }

    public double VarRe => N > 1 ? _m2Re / (N - 1) : 0;
    public double VarIm => N > 1 ? _m2Im / (N - 1) : 0;
}

/// <summary>The result of comparing two captures of <c>u</c>: a flat reference and a known tilt step.</summary>
public sealed record PhaseEstimate(double AngleDeg, double UncertaintyDeg, double Snr, double StepMagnitude)
{
    /// <summary>
    /// The instrument stores the phase in ±180° (firmware 0.11.5+; it was ±45°
    /// before). The estimate is folded to (−90°, 90°], so it always fits.
    /// </summary>
    public bool InRange => Math.Abs(AngleDeg) <= 180.0;

    /// <summary>Stored unit: 0.01°.</summary>
    public long CentiDegrees => Math.Clamp((long)Math.Round(AngleDeg * 100), -18000, 18000);

    /// <summary>Good enough to apply without a second thought.</summary>
    public bool Confident => Snr >= 8 && UncertaintyDeg <= 0.5;
}

/// <summary>The result of a gain (sensitivity k) calibration step.</summary>
public sealed record GainEstimate(double MeasuredChange, double TrueTilt, long KOldMicro, long KNewMicro)
{
    /// <summary>kNew / kOld — 1.0 means the old calibration was already right.</summary>
    public double Factor => (double)KNewMicro / KOldMicro;

    /// <summary>Percent change of k.</summary>
    public double ChangePercent => (Factor - 1) * 100;

    public bool InRange => KNewMicro >= 100 && KNewMicro <= 1_000_000;

    /// <summary>
    /// The measured step has the sign a positive step should have. When it does
    /// not, the invert flag (or the shim direction) is the suspect.
    /// </summary>
    public bool Positive => MeasuredChange > 0;
}

public static class CalibrationMath
{
    /// <summary><c>(sr + j·si) / (dr + j·di)</c>, or null if the divisor is (nearly) zero.</summary>
    public static (double Re, double Im)? ComplexDivide(double sr, double si, double dr, double di)
    {
        double d = dr * dr + di * di;
        if (d < 1e-12) return null;
        return ((sr * dr + si * di) / d, (si * dr - sr * di) / d);
    }

    /// <summary>
    /// Phase from a tilt step: the direction of <c>Δu = u_tilted − u_flat</c>, folded
    /// into (−90°, 90°] (the sign of the step is the invert flag's business, not the
    /// phase's). Null if either capture is too short or there is no step.
    /// </summary>
    public static PhaseEstimate? EstimatePhase(ComplexStats flat, ComplexStats tilted)
    {
        if (flat.N < 5 || tilted.N < 5) return null;
        double dRe = tilted.MeanRe - flat.MeanRe;
        double dIm = tilted.MeanIm - flat.MeanIm;
        double mag = Math.Sqrt(dRe * dRe + dIm * dIm);
        if (mag == 0) return null;

        double a = Math.Atan2(dIm, dRe) * 180 / Math.PI;
        while (a > 90) a -= 180;
        while (a <= -90) a += 180;

        double seRe = Math.Sqrt(flat.VarRe / flat.N + tilted.VarRe / tilted.N);
        double seIm = Math.Sqrt(flat.VarIm / flat.N + tilted.VarIm / tilted.N);
        double ux = dRe / mag, uy = dIm / mag;
        // Error component perpendicular to the step direction.
        double sePerp = Math.Sqrt(Math.Pow(seRe * uy, 2) + Math.Pow(seIm * ux, 2));
        double unc = Math.Atan2(sePerp, mag) * 180 / Math.PI;
        double snr = sePerp == 0 ? 1e9 : mag / sePerp;
        return new PhaseEstimate(a, unc, snr, mag);
    }

    /// <summary>
    /// k from a baseline and a known-tilt reading: <c>reading = (ratio − zero) / k</c>,
    /// so the ratio step is <c>Δm·kOld</c>, which the true tilt <c>T</c> produces with
    /// <c>kTrue</c> — <c>kTrue = kOld·|Δm| / T</c>.
    /// </summary>
    public static GainEstimate? EstimateGain(long kOldMicro, double baseline, double tilted, double trueTilt)
    {
        double change = tilted - baseline;
        if (trueTilt <= 0 || Math.Abs(change) < 1e-6) return null;
        long kNew = (long)Math.Round(kOldMicro * Math.Abs(change) / trueTilt);
        return new GainEstimate(change, trueTilt, kOldMicro, kNew);
    }

    /// <summary>
    /// Tilt in mm/m of a shim of <paramref name="heightMm"/> under one end of a baseline of
    /// <paramref name="baselineMm"/> (1000 mm = 1 m → h mm over 1 m is h mm/m).
    /// </summary>
    public static double ShimTilt(double heightMm, double baselineMm) =>
        baselineMm <= 0 ? 0 : heightMm / baselineMm * 1000.0;
}
