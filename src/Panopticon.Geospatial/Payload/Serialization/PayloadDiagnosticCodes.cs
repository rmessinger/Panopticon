namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Diagnostic codes for recoverable payload codec failures. Schema, limit, and resource
/// failures use the Parse stage; value failures rejected by payload model constructors
/// use the Validate stage.
/// </summary>
internal static class PayloadDiagnosticCodes
{
    /// <summary>Malformed JSON, unknown or duplicate members, bad enum tokens, non-finite numbers, unsupported kind or schema version.</summary>
    public const string PayloadSchemaInvalid = "GEO-PAY-001";

    /// <summary>Metadata exceeded MaxPayloadMetadataBytes or a structural chunk, feature, coordinate, or palette limit.</summary>
    public const string PayloadLimitExceeded = "GEO-PAY-002";

    /// <summary>Payload content rejected by a payload model constructor, for example a non-finite transform or a non-canonical path.</summary>
    public const string PayloadValueInvalid = "GEO-PAY-003";

    /// <summary>The metadata stream could not be read.</summary>
    public const string PayloadResourceFailure = "GEO-PAY-004";
}
