#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Package format constants and resource limits for the H4 unit-package container.</summary>
public static class UnitPackageFormat
{
    public const string ContainerKind = "unit-package";

    public const string ManifestFileName = "manifest.json";

    public const int CurrentSchemaVersion = 1;

    public const string SupportedGeneralsIniDialect = "generals-ini";

    public const int SupportedGeneralsIniDialectVersion = 1;

    /// <summary>Maximum allowed size in bytes for manifest.json (64 MiB).</summary>
    public const long MaxManifestBytes = 67_108_864;

    /// <summary>Maximum allowed size in bytes for a single record or retained payload file (64 MiB).</summary>
    public const long MaxPayloadFileBytes = 67_108_864;

    /// <summary>Maximum allowed record entries in a package manifest (100,000).</summary>
    public const int MaxRecordCount = 100_000;

    /// <summary>Maximum diagnostics reported before truncation and aggregate emission (100).</summary>
    public const int MaxDiagnosticCount = 100;

    public const int MaxPathLength = 260;

    public const int Sha256HexLength = 64;
}
