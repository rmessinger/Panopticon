#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Deterministic JSON contract for the external model request: stable camelCase property
/// names, enums as strings, and a strict schema-version check on parse.
/// </summary>
public static class ExternalModelProtocolJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static string Serialize(ExternalModelRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var payload = new RequestPayload
        {
            SchemaVersion = ExternalModelRequest.CurrentSchemaVersion,
            Generation = request.Generation,
            ModelSource = SerializeSource(request.ModelSource),
            AnimationSource = SerializeSource(request.AnimationSource),
            ImportOptions = new ImportOptionsPayload
            {
                UnitsPerMeter = request.ImportOptions.UnitsPerMeter,
                FeatureGates = request.ImportOptions.FeatureGates,
                MorphWeightRanges = request.ImportOptions.MorphWeightRanges,
            },
            PreviewOptions = SerializePreviewOptions(request.PreviewOptions),
            Viewport = new ViewportPayload
            {
                Width = request.Viewport.Width,
                Height = request.Viewport.Height,
            },
            OutputImagePath = request.OutputImagePath,
            OutputReportPath = request.OutputReportPath,
        };

        return JsonSerializer.Serialize(payload, Options);
    }

    /// <summary>
    /// Parses and schema-checks one request document. Throws JsonException for malformed
    /// JSON and InvalidDataException for an unsupported schema version.
    /// </summary>
    public static ExternalModelRequest Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("Request JSON must not be empty.");
        }

        var payload = JsonSerializer.Deserialize<RequestPayload>(json, Options)
            ?? throw new JsonException("Request JSON is not an object.");

        if (payload.SchemaVersion != ExternalModelRequest.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Request schema version {payload.SchemaVersion} is not supported; expected {ExternalModelRequest.CurrentSchemaVersion}.");
        }

        return new ExternalModelRequest
        {
            SchemaVersion = payload.SchemaVersion,
            Generation = payload.Generation,
            ModelSource = DeserializeSource(payload.ModelSource, required: true)!,
            AnimationSource = DeserializeSource(payload.AnimationSource, required: false),
            ImportOptions = new ExternalModelImportOptions(
                payload.ImportOptions?.UnitsPerMeter ?? ExternalModelImportOptions.Default.UnitsPerMeter,
                payload.ImportOptions?.FeatureGates,
                payload.ImportOptions?.MorphWeightRanges),
            PreviewOptions = DeserializePreviewOptions(payload.PreviewOptions),
            Viewport = new ExternalModelViewport(
                payload.Viewport?.Width ?? 256,
                payload.Viewport?.Height ?? 256),
            OutputImagePath = payload.OutputImagePath,
            OutputReportPath = payload.OutputReportPath ?? "",
        };
    }

    private static SourcePayload? SerializeSource(ExternalModelSourceRequest? source)
    {
        if (source is null)
        {
            return null;
        }

        return new SourcePayload
        {
            AbsoluteRoot = source.AbsoluteRoot,
            VirtualKey = source.VirtualKey,
            FormatId = source.FormatId,
        };
    }

    private static PreviewOptionsPayload SerializePreviewOptions(ExternalModelPreviewOptions options)
    {
        return new PreviewOptionsPayload
        {
            ModelYaw = options.ModelYaw,
            ModelPitch = options.ModelPitch,
            ModelRoll = options.ModelRoll,
            ModelTranslation = [options.ModelTranslation.X, options.ModelTranslation.Y, options.ModelTranslation.Z],
            CameraOrbit = options.CameraOrbit,
            CameraElevation = options.CameraElevation,
            CameraDistance = options.CameraDistance,
            CameraFieldOfView = options.CameraFieldOfView,
            Background = options.Background,
            Lighting = options.Lighting,
            SelectedAnimationKey = options.SelectedAnimationKey,
            AnimationSampleTime = options.AnimationSampleTime,
            MorphWeights = new Dictionary<string, float>(options.MorphWeights, StringComparer.Ordinal),
        };
    }

    private static ExternalModelPreviewOptions DeserializePreviewOptions(PreviewOptionsPayload? options)
    {
        if (options is null)
        {
            return ExternalModelPreviewOptions.Default;
        }

        var translation = options.ModelTranslation is { Length: 3 }
            ? new System.Numerics.Vector3(options.ModelTranslation[0], options.ModelTranslation[1], options.ModelTranslation[2])
            : System.Numerics.Vector3.Zero;

        return new ExternalModelPreviewOptions
        {
            ModelYaw = options.ModelYaw,
            ModelPitch = options.ModelPitch,
            ModelRoll = options.ModelRoll,
            ModelTranslation = translation,
            CameraOrbit = options.CameraOrbit,
            CameraElevation = options.CameraElevation,
            CameraDistance = options.CameraDistance,
            CameraFieldOfView = options.CameraFieldOfView,
            Background = options.Background,
            Lighting = options.Lighting,
            SelectedAnimationKey = options.SelectedAnimationKey,
            AnimationSampleTime = options.AnimationSampleTime,
            MorphWeights = new Dictionary<string, float>(options.MorphWeights, StringComparer.Ordinal),
        };
    }

    private static ExternalModelSourceRequest? DeserializeSource(SourcePayload? source, bool required)
    {
        if (source is null)
        {
            if (required)
            {
                throw new JsonException("Request is missing the model source.");
            }

            return null;
        }

        return new ExternalModelSourceRequest(
            source.AbsoluteRoot ?? "",
            source.VirtualKey ?? "",
            source.FormatId ?? "");
    }

    private sealed class RequestPayload
    {
        public int SchemaVersion { get; set; }

        public int Generation { get; set; }

        public SourcePayload? ModelSource { get; set; }

        public SourcePayload? AnimationSource { get; set; }

        public ImportOptionsPayload? ImportOptions { get; set; }

        public PreviewOptionsPayload? PreviewOptions { get; set; }

        public ViewportPayload? Viewport { get; set; }

        public string? OutputImagePath { get; set; }

        public string? OutputReportPath { get; set; } = "";
    }

    private sealed class SourcePayload
    {
        public string? AbsoluteRoot { get; set; }

        public string? VirtualKey { get; set; }

        public string? FormatId { get; set; }
    }

    private sealed class ImportOptionsPayload
    {
        public float UnitsPerMeter { get; set; }

        public IReadOnlyDictionary<string, bool>? FeatureGates { get; set; }

        public IReadOnlyDictionary<string, ExternalMorphWeightRange>? MorphWeightRanges { get; set; }
    }

    private sealed class PreviewOptionsPayload
    {
        public float ModelYaw { get; set; }

        public float ModelPitch { get; set; }

        public float ModelRoll { get; set; }

        public float[]? ModelTranslation { get; set; }

        public float CameraOrbit { get; set; }

        public float CameraElevation { get; set; }

        public float CameraDistance { get; set; }

        public float CameraFieldOfView { get; set; }

        public ExternalPreviewBackground Background { get; set; }

        public ExternalPreviewLighting Lighting { get; set; }

        public string? SelectedAnimationKey { get; set; }

        public float AnimationSampleTime { get; set; }

        public IReadOnlyDictionary<string, float>? MorphWeights { get; set; }
    }

    private sealed class ViewportPayload
    {
        public int Width { get; set; }

        public int Height { get; set; }
    }
}
