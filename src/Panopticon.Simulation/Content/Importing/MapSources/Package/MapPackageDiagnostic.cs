#nullable enable

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>One manifest schema validation finding. Any diagnostic marks the manifest invalid.</summary>
public sealed record MapPackageDiagnostic(string Code, string Message)
{
    public const string PackageFormat = "package-format";

    public const string PackageVersion = "package-version";

    public const string SchemaVersion = "schema-version";

    public const string MalformedJson = "malformed-json";

    public const string UnknownJsonMember = "unknown-json-member";

    public const string NullComponent = "null-component";

    public const string PathInvalid = "path-invalid";

    public const string DigestInvalid = "digest-invalid";

    public const string ChunkInventory = "chunk-inventory";

    public const string LatticeDimensions = "lattice-dimensions";

    public const string LayerEncoding = "layer-encoding";

    public const string LayerLength = "layer-length";

    public const string LayersConsistency = "layers-consistency";

    public const string PaletteCount = "palette-count";

    public const string PaletteIndex = "palette-index";

    public const string StableIdDuplicate = "stable-id-duplicate";

    public const string RetainedName = "retained-name";

    public const string RetainedVersion = "retained-version";

    public const string RetainedLength = "retained-length";

    public const string QuantityFinite = "quantity-finite";

    public const string QuantityUnitFrame = "quantity-unit-frame";

    public const string PropertyType = "property-type";

    public const string ResourceLimitExceeded = "resource-limit-exceeded";

    public const string ResourceLimit = ResourceLimitExceeded;

    public const string DiagnosticCapExceeded = "diagnostic-cap-exceeded";

    public const string DiagnosticCap = DiagnosticCapExceeded;

    public const string RetainedNameDuplicate = "retained-name-duplicate";

    public const string RetainedDuplicate = RetainedNameDuplicate;

    public const string CompanionNameDuplicate = "companion-name-duplicate";

    public const string CompanionDuplicate = CompanionNameDuplicate;

    public const string IdentitySourceKind = "identity-source-kind";

    public const string SourceKind = IdentitySourceKind;

    public const string NodataPolicy = "nodata-policy";

    public override string ToString() => $"{Code}: {Message}";
}
