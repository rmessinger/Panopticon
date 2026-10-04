namespace Panopticon.Recording;

/// <summary>
/// Recording mode (WI-A1-19): <see cref="Full"/> is the A1-5 stack unchanged; <see cref="Operational"/>
/// samples only at cadence boundaries and defers T0/T1/T2 detail to the operational stream.
/// </summary>
public enum RecordingMode
{
    Full,
    Operational,
}
