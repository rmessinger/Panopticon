#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSage.Content.Importing;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// JSON contract for the external model report. The serialized shape is a versioned
/// camelCase document that omits nulls and never contains exception detail, stack traces,
/// temporary paths, or machine-specific paths.
/// </summary>
public static class ExternalModelReportJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static string Serialize(ExternalModelReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var payload = new ReportPayload
        {
            SchemaVersion = ExternalModelReport.SchemaVersion,
            ModelSource = SerializeIdentity(report.ModelSource),
            AnimationSource = SerializeIdentity(report.AnimationSource),
            Catalog = SerializeCatalog(report.Catalog),
            Diagnostics = report.Diagnostics.Select(SerializeDiagnostic).ToArray(),
            Eligibility = SerializeEligibility(report.Eligibility),
        };

        return JsonSerializer.Serialize(payload, Options);
    }

    /// <summary>
    /// Parses a serialized report into its plain JSON document, for round-trip and consumer
    /// checks. Values are read as raw JSON; no parser objects are reconstructed.
    /// </summary>
    public static JsonDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Report JSON must not be empty.", nameof(json));
        }

        return JsonDocument.Parse(json);
    }

    private static IdentityPayload? SerializeIdentity(ExternalSourceIdentity? identity)
    {
        if (identity is null)
        {
            return null;
        }

        return new IdentityPayload
        {
            DisplayName = identity.DisplayName,
            VirtualKey = identity.VirtualKey,
            ByteLength = identity.ByteLength,
            ContentHash = identity.ContentHash,
        };
    }

    private static CatalogPayload? SerializeCatalog(ExternalAssetCatalog? catalog)
    {
        if (catalog is null)
        {
            return null;
        }

        return new CatalogPayload
        {
            SourceDisplayName = catalog.SourceDisplayName,
            FormatId = catalog.FormatId,
            SceneCount = catalog.SceneCount,
            NodeCount = catalog.NodeCount,
            PrimitiveCount = catalog.PrimitiveCount,
            VertexCount = catalog.VertexCount,
            IndexCount = catalog.IndexCount,
            MaterialCount = catalog.MaterialCount,
            ImageCount = catalog.ImageCount,
            SkinCount = catalog.SkinCount,
            JointCount = catalog.JointCount,
            Bounds = new BoundsPayload
            {
                MinX = catalog.Bounds.Min.X,
                MinY = catalog.Bounds.Min.Y,
                MinZ = catalog.Bounds.Min.Z,
                MaxX = catalog.Bounds.Max.X,
                MaxY = catalog.Bounds.Max.Y,
                MaxZ = catalog.Bounds.Max.Z,
                SphereCenterX = catalog.Bounds.SphereCenter.X,
                SphereCenterY = catalog.Bounds.SphereCenter.Y,
                SphereCenterZ = catalog.Bounds.SphereCenter.Z,
                SphereRadius = catalog.Bounds.SphereRadius,
            },
            RequiredFeatureIds = catalog.RequiredFeatureIds.ToArray(),
            OptionalFeatureIds = catalog.OptionalFeatureIds.ToArray(),
            Animations = catalog.Animations
                .Select(a => new AnimationPayload
                {
                    Key = a.Key,
                    Name = a.Name,
                    DurationSeconds = a.Duration.TotalSeconds,
                    ChannelCount = a.ChannelCount,
                })
                .ToArray(),
            Diagnostics = catalog.Diagnostics
                .Select(d => new CatalogDiagnosticPayload
                {
                    Code = d.Code,
                    Severity = d.Severity.ToString(),
                    Message = d.Message,
                })
                .ToArray(),
        };
    }

    private static DiagnosticPayload SerializeDiagnostic(ExternalModelDiagnostic diagnostic)
    {
        return new DiagnosticPayload
        {
            Code = diagnostic.Code,
            Severity = diagnostic.Severity.ToString(),
            Stage = diagnostic.Stage.ToString(),
            Source = diagnostic.Source.ToString(),
            Context = diagnostic.Context,
            Message = diagnostic.Message,
            FixHint = diagnostic.FixHint,
            OccurrenceCount = diagnostic.OccurrenceCount,
        };
    }

    private static EligibilityPayload SerializeEligibility(PreviewEligibility eligibility)
    {
        return new EligibilityPayload
        {
            CanRender = eligibility.CanRender,
            BlockingDiagnosticCodes = eligibility.BlockingDiagnosticCodes.ToArray(),
            MissingCapabilities = eligibility.MissingCapabilities.ToArray(),
        };
    }

    private sealed class ReportPayload
    {
        public int SchemaVersion { get; set; }

        public IdentityPayload? ModelSource { get; set; }

        public IdentityPayload? AnimationSource { get; set; }

        public CatalogPayload? Catalog { get; set; }

        public IReadOnlyList<DiagnosticPayload> Diagnostics { get; set; } = [];

        public EligibilityPayload Eligibility { get; set; } = new();
    }

    private sealed class IdentityPayload
    {
        public string DisplayName { get; set; } = "";

        public string VirtualKey { get; set; } = "";

        public long ByteLength { get; set; }

        public string ContentHash { get; set; } = "";
    }

    private sealed class CatalogPayload
    {
        public string SourceDisplayName { get; set; } = "";

        public string FormatId { get; set; } = "";

        public int SceneCount { get; set; }

        public int NodeCount { get; set; }

        public int PrimitiveCount { get; set; }

        public long VertexCount { get; set; }

        public long IndexCount { get; set; }

        public int MaterialCount { get; set; }

        public int ImageCount { get; set; }

        public int SkinCount { get; set; }

        public int JointCount { get; set; }

        public BoundsPayload Bounds { get; set; } = new();

        public IReadOnlyList<string> RequiredFeatureIds { get; set; } = [];

        public IReadOnlyList<string> OptionalFeatureIds { get; set; } = [];

        public IReadOnlyList<AnimationPayload> Animations { get; set; } = [];

        public IReadOnlyList<CatalogDiagnosticPayload> Diagnostics { get; set; } = [];
    }

    private sealed class BoundsPayload
    {
        public float MinX { get; set; }

        public float MinY { get; set; }

        public float MinZ { get; set; }

        public float MaxX { get; set; }

        public float MaxY { get; set; }

        public float MaxZ { get; set; }

        public float SphereCenterX { get; set; }

        public float SphereCenterY { get; set; }

        public float SphereCenterZ { get; set; }

        public float SphereRadius { get; set; }
    }

    private sealed class AnimationPayload
    {
        public string Key { get; set; } = "";

        public string Name { get; set; } = "";

        public double DurationSeconds { get; set; }

        public int ChannelCount { get; set; }
    }

    private sealed class CatalogDiagnosticPayload
    {
        public string Code { get; set; } = "";

        public string Severity { get; set; } = "";

        public string Message { get; set; } = "";
    }

    private sealed class DiagnosticPayload
    {
        public string Code { get; set; } = "";

        public string Severity { get; set; } = "";

        public string Stage { get; set; } = "";

        public string Source { get; set; } = "";

        public string? Context { get; set; }

        public string Message { get; set; } = "";

        public string? FixHint { get; set; }

        public int OccurrenceCount { get; set; }
    }

    private sealed class EligibilityPayload
    {
        public bool CanRender { get; set; }

        public IReadOnlyList<string> BlockingDiagnosticCodes { get; set; } = [];

        public IReadOnlyList<string> MissingCapabilities { get; set; } = [];
    }
}
