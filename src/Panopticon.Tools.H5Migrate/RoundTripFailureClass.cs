#nullable enable

namespace Panopticon.Tools.H5Migrate;

/// <summary>Named failure classes reported by the round-trip validator (WI-H5-03).</summary>
public enum RoundTripFailureClass
{
    TypedFieldMismatch,
    RetainedHashMismatch,
    MissingSection,
    MissingRecord,
    ExtraRecord,
    MaterializedTemplateMismatch,
}
