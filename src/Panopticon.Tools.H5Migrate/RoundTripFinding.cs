#nullable enable

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// One mismatch or read failure. <see cref="FailureClass"/> is null for operational failures
/// (unreadable input or package) that are not a comparison class.
/// </summary>
public sealed record RoundTripFinding(
    string Code,
    RoundTripFailureClass? FailureClass,
    string Section,
    string Subject,
    string Message);
