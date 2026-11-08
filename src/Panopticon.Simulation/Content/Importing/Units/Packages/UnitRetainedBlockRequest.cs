#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Identifies one record that needs its exact INI block retained. One request per record: the
/// supplied block holds the whole record source, including nested module blocks, so whole-record
/// and per-module bindings re-issue from one payload. Handles are session-local and never appear
/// in the request or the package bytes.
/// </summary>
public sealed record UnitRetainedBlockRequest(ContentId RecordId);
