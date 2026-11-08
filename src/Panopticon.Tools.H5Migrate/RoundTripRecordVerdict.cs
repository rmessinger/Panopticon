#nullable enable

namespace Panopticon.Tools.H5Migrate;

/// <summary>Verdict for one expected record (units mode).</summary>
public sealed record RoundTripRecordVerdict(string Record, bool Passed);
