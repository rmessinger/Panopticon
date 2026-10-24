namespace Panopticon.Navigation;

/// <summary>
/// Immutable passability state handed to the async path worker (WI-N1-03). Version is the
/// <see cref="Navigation"/> passability version at submission time. Cells stores one byte per
/// node in the worker graph's layout (index = x + y * width), non-zero meaning passable.
/// Submissions share one snapshot until <see cref="Navigation.UpdateAreaPassability"/> bumps
/// the version, replacing the per-tick graph clone.
/// </summary>
public sealed class PassabilitySnapshot
{
    public int Version { get; }

    public byte[] Cells { get; }

    public PassabilitySnapshot(int version, byte[] cells)
    {
        Version = version;
        Cells = cells;
    }
}
