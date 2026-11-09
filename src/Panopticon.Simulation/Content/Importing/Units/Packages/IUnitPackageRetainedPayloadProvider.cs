#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Source of the exact INI block text retained for legacy-bound records. Provided bytes are stored
/// verbatim and verified by length and SHA-256 after staging. Null or empty means the block is
/// unavailable, which refuses the write rather than fabricating a re-serialization.
/// </summary>
public interface IUnitPackageRetainedPayloadProvider
{
    /// <summary>The exact source block for one record, including nested module blocks; null when unavailable.</summary>
    UnitPackageRetainedBlock? GetBlock(UnitRetainedBlockRequest request);
}
