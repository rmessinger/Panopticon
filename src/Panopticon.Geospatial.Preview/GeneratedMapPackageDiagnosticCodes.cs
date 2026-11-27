namespace Panopticon.Geospatial.Preview;

/// <summary>Stable diagnostic codes of the payload-to-package converter.</summary>
public static class GeneratedMapPackageDiagnosticCodes
{
    public const string ChunkReadFailed = "GEO-PKG-001";

    public const string ImageryDecodeFailed = "GEO-PKG-002";

    public const string ImageryDimensionsMismatch = "GEO-PKG-003";

    public const string ImageryOversized = "GEO-PKG-004";

    public const string ChunkCountOverflow = "GEO-PKG-005";

    public const string LatticeAreaOverflow = "GEO-PKG-006";

    public const string FillCountOverflow = "GEO-PKG-007";

    public const string StableIdDeduplicated = "GEO-PKG-008";

    public const string StableIdSourceConflict = "GEO-PKG-009";

    public const string VectorFeatureConflict = "GEO-PKG-010";

    public const string BuildingConflict = "GEO-PKG-011";

    public const string BuildingHeightUnresolved = "GEO-PKG-012";

    public const string BuildingGenerated = "GEO-PKG-013";

    /// <summary>Fatal: terrain samples are non-finite or outside the resident height grid range; no package is written.</summary>
    public const string HeightQuantization = "GEO-PKG-014";

    /// <summary>Informational: count and maximum error of terrain samples quantized to the resident height grid.</summary>
    public const string HeightQuantizationSummary = "GEO-PKG-015";

    /// <summary>Fatal: the palette is empty or a palette entry has no resolvable texture binding; no package is written.</summary>
    public const string BlendTextureUnresolved = "GEO-PKG-016";

    /// <summary>Fatal: the skirmish configuration is invalid (unknown faction, malformed or out-of-map coordinates, duplicate start positions, or no slots); no package is written.</summary>
    public const string SkirmishConfiguration = "GEO-PKG-017";

    /// <summary>Informational: the retained SidesList section and the player start waypoints synthesized from the skirmish configuration.</summary>
    public const string SkirmishSynthesized = "GEO-PKG-018";

    /// <summary>Informational: count of bridge pairs emitted from the bridge rules.</summary>
    public const string BridgePairsEmitted = "GEO-PKG-019";

    /// <summary>Informational: matched features the bridge pass skipped (non-line geometry or multi-segment lines).</summary>
    public const string BridgeFeaturesSkipped = "GEO-PKG-020";

    /// <summary>Fatal: a bridge-matched line feature is degenerate (zero length); no package is written.</summary>
    public const string BridgeLineDegenerate = "GEO-PKG-021";

    /// <summary>Informational: bridge-matched duplicate emissions for source parts already bridged (the first emission kept).</summary>
    public const string BridgeDuplicatesIgnored = "GEO-PKG-022";

    /// <summary>Informational: lattice cells graded so terrain ramps to the bridge deck at each landing (bridge rules opt-in).</summary>
    public const string BridgeLandingGrading = "GEO-PKG-023";

    /// <summary>Informational: bridge pairs whose landings keep their original heights because the runtime plans the approach corridor (supported bridge template).</summary>
    public const string BridgePathPlanHandoff = "GEO-PKG-024";
}
