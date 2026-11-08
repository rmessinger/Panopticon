#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>One model-validation finding. Codes are stable identifiers for tests and reports; any diagnostic blocks publication.</summary>
public sealed record MapInterchangeDiagnostic(string Code, string Message)
{
    public const string SchemaVersion = "schema-version";

    public const string ChunkInventory = "chunk-inventory";

    public const string LatticeDimensions = "lattice-dimensions";

    public const string HeightLength = "height-length";

    public const string HeightFinite = "height-finite";

    public const string HeightRepresentable = "height-representable";

    public const string ObjectRepresentable = "object-representable";

    public const string PropertyType = "property-type";

    public const string MaterialLength = "material-length";

    public const string MaskLength = "mask-length";

    public const string MaskBits = "mask-bits";

    public const string PaletteCount = "palette-count";

    public const string PaletteIndex = "palette-index";

    public const string PaletteDuplicateIndex = "palette-duplicate-index";

    public const string LayersConsistency = "layers-consistency";

    public const string SidesListVersion = "sideslist-version";

    public const string ImageryConsistency = "imagery-consistency";

    public const string StableIdDuplicate = "stable-id-duplicate";

    public const string RetainedName = "retained-name";

    public const string RetainedVersion = "retained-version";

    public const string RetainedDigest = "retained-digest";

    public const string SeamEquality = "seam-equality";

    public const string ApronAgreement = "apron-agreement";

    public const string NullComponent = "null-component";

    public const string QuantityFinite = "quantity-finite";

    public const string CoordinateFrame = "coordinate-frame";

    public const string UnitFrame = "unit-frame";

    public const string UnsupportedAddressing = "unsupported-addressing";

    public override string ToString() => $"{Code}: {Message}";
}
