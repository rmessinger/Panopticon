using System;
using System.Collections.Generic;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// Provenance of one import source retained in the payload: manifest identity plus the
/// values observed while reading, including the actual source CRS and any vertical datum
/// embedded in the file.
/// </summary>
public sealed record PayloadSourceProvenance
{
    public PayloadSourceProvenance(
        string sourceId,
        ManifestSourceKind kind,
        ManifestSourceRole role,
        string relativePath,
        string sha256Hex,
        long lengthBytes,
        CoordinateSystemId actualCoordinateSystem,
        string declaredVerticalDatum,
        string? embeddedVerticalDatum = null,
        string? label = null,
        string? attribution = null,
        double? horizontalAccuracyMeters = null,
        double? verticalAccuracyMeters = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined source kind.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Undefined source role.");
        }

        PayloadValidationRules.NormalizedRelativePath(relativePath, nameof(relativePath));
        PayloadValidationRules.Sha256Hex(sha256Hex, nameof(sha256Hex));
        ArgumentOutOfRangeException.ThrowIfNegative(lengthBytes);
        PayloadValidationRules.CoordinateSystem(actualCoordinateSystem, nameof(actualCoordinateSystem));
        ArgumentException.ThrowIfNullOrEmpty(declaredVerticalDatum);
        ValidateDatumConflict(declaredVerticalDatum, embeddedVerticalDatum, nameof(embeddedVerticalDatum));
        ValidateAccuracy(horizontalAccuracyMeters, nameof(horizontalAccuracyMeters));
        ValidateAccuracy(verticalAccuracyMeters, nameof(verticalAccuracyMeters));

        SourceId = sourceId;
        Kind = kind;
        Role = role;
        RelativePath = relativePath;
        Sha256Hex = sha256Hex;
        LengthBytes = lengthBytes;
        ActualCoordinateSystem = actualCoordinateSystem;
        DeclaredVerticalDatum = declaredVerticalDatum;
        EmbeddedVerticalDatum = embeddedVerticalDatum;
        Label = label;
        Attribution = attribution;
        HorizontalAccuracyMeters = horizontalAccuracyMeters;
        VerticalAccuracyMeters = verticalAccuracyMeters;
    }

    /// <summary>Stable identifier of the source within the import.</summary>
    public string SourceId { get; }

    public ManifestSourceKind Kind { get; }

    public ManifestSourceRole Role { get; }

    /// <summary>Normalized manifest-relative path of the source file.</summary>
    public string RelativePath { get; }

    /// <summary>SHA-256 of the source bytes as 64 lowercase hex characters.</summary>
    public string Sha256Hex { get; }

    public long LengthBytes { get; }

    /// <summary>CRS actually read from the source file, not merely declared.</summary>
    public CoordinateSystemId ActualCoordinateSystem { get; }

    /// <summary>Vertical datum declared by the manifest for this source.</summary>
    public string DeclaredVerticalDatum { get; }

    /// <summary>Vertical datum embedded in the source file, when it declares one.</summary>
    public string? EmbeddedVerticalDatum { get; }

    public string? Label { get; }

    public string? Attribution { get; }

    public double? HorizontalAccuracyMeters { get; }

    public double? VerticalAccuracyMeters { get; }

    private static void ValidateDatumConflict(string declared, string? embedded, string paramName)
    {
        if (PayloadValidationRules.IsKnownDatumConflict(declared, embedded))
        {
            throw new ArgumentException(
                $"The declared vertical datum '{declared}' conflicts with the embedded vertical datum '{embedded}'; no geoid conversion is performed.",
                paramName);
        }
    }

    private static void ValidateAccuracy(double? meters, string paramName)
    {
        if (meters is { } value && (!double.IsFinite(value) || value < 0d))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Accuracy must be finite and non-negative.");
        }
    }
}
