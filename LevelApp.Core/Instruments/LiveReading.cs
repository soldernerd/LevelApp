namespace LevelApp.Core.Instruments;

/// <summary>One selectable reading channel of an instrument (e.g. sensor 1, sensor 2, their difference).</summary>
public record ReadingChannel(string Id, string DisplayName);

/// <summary>
/// The instrument's current live value on the selected channel.
/// </summary>
/// <param name="ValueMicronsPerMetre">Tilt in µm/m (the unit the operator would type in), zero offset applied.</param>
/// <param name="IsValid">The instrument delivers a reading.</param>
/// <param name="IsDoubtful">The reading is unsteady (e.g. vibration); a measurement now would be unreliable.</param>
public record LiveReading(double ValueMicronsPerMetre, bool IsValid, bool IsDoubtful);
