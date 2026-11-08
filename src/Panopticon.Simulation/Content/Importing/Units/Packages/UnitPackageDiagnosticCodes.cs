#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Stable diagnostic codes for unit package reading and validation.</summary>
public static class UnitPackageDiagnosticCodes
{
    public const string ContainerKind = "H4-CONTAINER-KIND";
    public const string SchemaVersion = "H4-SCHEMA-VERSION";
    public const string ResourceLimitExceeded = "H4-RESOURCE-LIMIT";
    public const string PathInvalid = "H4-PATH-INVALID";
    public const string PathEscape = "H4-PATH-ESCAPE";
    public const string SymlinkRejected = "H4-SYMLINK-REJECTED";
    public const string DigestInvalid = "H4-DIGEST-INVALID";
    public const string DigestMismatch = "H4-DIGEST-MISMATCH";
    public const string LengthMismatch = "H4-LENGTH-MISMATCH";
    public const string FileMissing = "H4-FILE-MISSING";
    public const string IoFailure = "H4-IO-FAILURE";
    public const string PayloadInvalid = "H4-PAYLOAD-INVALID";
    public const string MissingRetainedPayload = "H4-MISSING-RETAINED-PAYLOAD";
    public const string UnsupportedDialect = "H4-UNSUPPORTED-DIALECT";
    public const string UnsupportedDialectVersion = "H4-UNSUPPORTED-DIALECT-VERSION";
    public const string UnknownField = "H4-UNKNOWN-FIELD";
    public const string ReservedField = "H4-RESERVED-FIELD";
    public const string UnknownManifestMember = "H4-UNKNOWN-MANIFEST-MEMBER";
    public const string InvalidEnumValue = "H4-INVALID-ENUM";
    public const string DiagnosticCapExceeded = "H4-DIAGNOSTIC-CAP";
}
