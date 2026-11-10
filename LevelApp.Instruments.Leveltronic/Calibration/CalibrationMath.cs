namespace LevelApp.Instruments.Leveltronic.Calibration;

// The arithmetic of the phase and gain calibration wizards, as pure functions so it
// is unit-tested against the simulator's known truth. A port of the phone app's
// lib/devices/leveltronic/calibration/calibration_math.dart.
//
// Model (firmware Math/math_displacement.c): per batch and sensor u = S / D with
// D = A - B; a real tilt moves u along e^{j*phi}, phi being the sensor's signal
// phase. The firmware derotates by the stored phase, x = u * e^{-j*phiCal}, and
// reports tilt = (Re x - zero) / k. A positive tilt points at phi, a negative one
// at phi + 180 degrees.

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

/// <summary>
/// The signal phase of a sensor found from captures of <c>u = S / (A − B)</c> with a
/// clear tilt applied (see <see cref="CalibrationMath.EstimatePhaseFromTilts"/>).
/// </summary>
/// <param name="AngleDeg">The signal phase in (−180°, 180°]: the direction of <c>u</c> for a positive tilt.</param>
/// <param name="UncertaintyDeg">1σ uncertainty of <paramref name="AngleDeg"/>.</param>
/// <param name="Snr">|signal| over the standard error perpendicular to it.</param>
/// <param name="StepMagnitude">|u| (one tilt) or |u₊ − u₋| (both), the signal size in the ratio plane.</param>
/// <param name="SingleSided">
/// Found from one tilt direction only, so the sensor's zero offset is not cancelled (it
/// adds a small error when the tilt is not large against it).
/// </param>
public sealed record PhaseEstimate(
    double AngleDeg, double UncertaintyDeg, double Snr, double StepMagnitude, bool SingleSided = false)
{
    /// <summary>The instrument stores the phase in ±180°.</summary>
    public bool InRange => Math.Abs(AngleDeg) <= 180.0;

    /// <summary>Stored unit: 0.01°.</summary>
    public long CentiDegrees =>
        Math.Clamp((long)Math.Round(AngleDeg * 100, MidpointRounding.AwayFromZero), -18000, 18000);

    /// <summary>Good enough to apply without a second thought.</summary>
    public bool Confident => Snr >= 8 && UncertaintyDeg <= 0.5;

    /// <summary>The same estimate rotated by <paramref name="deg"/>, kept in (−180°, 180°].</summary>
    public PhaseEstimate Rotated(double deg) => this with { AngleDeg = CalibrationMath.WrapDegrees(AngleDeg + deg) };
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

    /// <summary><paramref name="deg"/> folded into (−180°, 180°].</summary>
    public static double WrapDegrees(double deg)
    {
        double a = deg;
        while (a > 180) a -= 360;
        while (a <= -180) a += 360;
        return a;
    }

    /// <summary>
    /// Phase from captures of <c>u</c> with a clear tilt of each sign. A tilt moves <c>u</c>
    /// along <c>e^{jφ}</c>: a positive tilt points at φ, a negative one at φ + 180°.
    /// <list type="bullet">
    /// <item>both <paramref name="positive"/> and <paramref name="negative"/>: φ is the direction of
    /// <c>u₊ − u₋</c>, so the sensor's zero offset cancels and the tilt size does not matter;</item>
    /// <item>only one: φ is the direction of <c>u</c> itself (turned by 180° for the negative
    /// tilt), which assumes the zero offset is small against the signal.</item>
    /// </list>
    /// Null if a capture is too short or there is no signal.
    /// </summary>
    public static PhaseEstimate? EstimatePhaseFromTilts(ComplexStats? positive = null, ComplexStats? negative = null)
    {
        if (positive is null && negative is null) return null;
        if ((positive is not null && positive.N < 5) || (negative is not null && negative.N < 5)) return null;

        double dRe, dIm, seRe, seIm, flip;
        if (positive is not null && negative is not null)
        {
            dRe = positive.MeanRe - negative.MeanRe;
            dIm = positive.MeanIm - negative.MeanIm;
            seRe = Math.Sqrt(positive.VarRe / positive.N + negative.VarRe / negative.N);
            seIm = Math.Sqrt(positive.VarIm / positive.N + negative.VarIm / negative.N);
            flip = 0;
        }
        else
        {
            var one = positive ?? negative!;
            dRe = one.MeanRe;
            dIm = one.MeanIm;
            seRe = Math.Sqrt(one.VarRe / one.N);
            seIm = Math.Sqrt(one.VarIm / one.N);
            flip = positive is not null ? 0 : 180;
        }
        double mag = Math.Sqrt(dRe * dRe + dIm * dIm);
        if (mag == 0) return null;

        double ux = dRe / mag, uy = dIm / mag;
        // Error component perpendicular to the signal direction.
        double sePerp = Math.Sqrt(Math.Pow(seRe * uy, 2) + Math.Pow(seIm * ux, 2));
        return new PhaseEstimate(
            AngleDeg: WrapDegrees(Math.Atan2(dIm, dRe) * 180 / Math.PI + flip),
            UncertaintyDeg: Math.Atan2(sePerp, mag) * 180 / Math.PI,
            Snr: sePerp == 0 ? 1e9 : mag / sePerp,
            StepMagnitude: mag,
            SingleSided: positive is null || negative is null);
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
