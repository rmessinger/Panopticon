using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Tools.ReplaySketch.Model.Capture;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Loads <see cref="CaptureProfile"/> objects from JSON profile files: comment-tolerant,
/// trailing-comma tolerant, enum values as strings, polymorphic commands and expectations
/// discriminated by their <c>Type</c> property.
///
/// Unit schema: a profile declaring <c>"UnitSchema": "metric-v1"</c> loads identity; a profile
/// without the marker is the recognized historical schema in legacy game units and has its
/// absolute length fields converted to meters once, at load; any other marker value fails with
/// <see cref="CaptureProfileLoader.UnsupportedUnitSchemaException"/>. After a load the
/// in-memory model is always metric and carries the marker, so a serialized profile always
/// states its unit schema.
/// </summary>
public static class CaptureProfileLoader
{
    /// <summary>Raised when a profile declares a unit schema marker this loader does not know.</summary>
    public sealed class UnsupportedUnitSchemaException : InvalidOperationException
    {
        public UnsupportedUnitSchemaException(string schema, string source)
            : base(
                $"Capture profile '{source}' declares unsupported UnitSchema '{schema}'. "
                + $"Supported: '{CaptureProfile.MetricSchemaMarker}' or no marker (the historical legacy-unit schema).")
        {
        }
    }

    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Historical markerless profiles express lengths in legacy game units.</summary>
    private static readonly float MetersPerLegacyUnit = (float)UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>
    /// Loads a capture profile from the embedded profile resource with the given bare filename
    /// (e.g. <c>construction-capture.json</c>, under the <c>Profiles/</c> folder).
    /// </summary>
    public static CaptureProfile LoadEmbeddedCapture(string resourceName)
    {
        var assembly = typeof(CaptureProfileLoader).Assembly;
        var fullName = $"Panopticon.Tools.ReplaySketch.Core.Profiles.{resourceName}";

        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException(
                $"Embedded capture profile '{fullName}' not found. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");

        return Deserialize(stream, fullName);
    }

    /// <summary>Loads a capture profile from a file on disk.</summary>
    public static CaptureProfile LoadCaptureFromFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Deserialize(stream, filePath);
    }

    /// <summary>All embedded capture profiles (filenames starting with <c>capture-</c>).</summary>
    public static IReadOnlyList<string> EmbeddedCaptureProfileNames()
    {
        var assembly = typeof(CaptureProfileLoader).Assembly;
        var prefix = "Panopticon.Tools.ReplaySketch.Core.Profiles.";
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix + "capture-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name => name[prefix.Length..])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static CaptureProfile Deserialize(Stream stream, string source)
    {
        // The marker decides the unit schema and is read from the raw JSON: a markerless
        // document deserializes with the model's metric default and must not inherit it.
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var schema = ReadUnitSchema(document.RootElement, source);
        var profile = document.RootElement.Deserialize<CaptureProfile>(SerializationOptions)
            ?? throw new InvalidOperationException($"Deserialization of capture profile '{source}' returned null.");

        if (schema is null)
        {
            ConvertLegacyUnitsToMeters(profile);
        }

        profile.UnitSchema = CaptureProfile.MetricSchemaMarker;
        return profile;
    }

    /// <summary>
    /// Returns the marker string when the profile declares <see cref="CaptureProfile.MetricSchemaMarker"/>,
    /// null when no marker is present (or it is JSON null), and otherwise throws.
    /// </summary>
    private static string? ReadUnitSchema(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals("UnitSchema", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind is not JsonValueKind.String)
            {
                throw new UnsupportedUnitSchemaException(property.Value.GetRawText(), source);
            }

            var schema = property.Value.GetString();
            if (!string.Equals(schema, CaptureProfile.MetricSchemaMarker, StringComparison.Ordinal))
            {
                throw new UnsupportedUnitSchemaException(schema ?? string.Empty, source);
            }

            return schema;
        }

        return null;
    }

    /// <summary>
    /// Converts the historical schema's legacy game-unit lengths to meters, once, in place.
    /// Frames, ratios, angles, ids, definition names, and the content imports' declared source
    /// scales (similarity-converted at the import boundary) are not lengths and stay untouched.
    /// </summary>
    private static void ConvertLegacyUnitsToMeters(CaptureProfile profile)
    {
        foreach (var scorch in profile.Scorches)
        {
            scorch.X *= MetersPerLegacyUnit;
            scorch.Y *= MetersPerLegacyUnit;
            scorch.Radius *= MetersPerLegacyUnit;
        }

        foreach (var actor in profile.Actors)
        {
            actor.X *= MetersPerLegacyUnit;
            actor.Y *= MetersPerLegacyUnit;
            actor.Z *= MetersPerLegacyUnit;
        }

        foreach (var command in profile.Commands)
        {
            switch (command)
            {
                case CaptureConstructCommand construct:
                    construct.X *= MetersPerLegacyUnit;
                    construct.Y *= MetersPerLegacyUnit;
                    break;
                case CaptureApplyImpulseCommand impulse:
                    impulse.DeltaVelocityX *= MetersPerLegacyUnit;
                    impulse.DeltaVelocityY *= MetersPerLegacyUnit;
                    impulse.DeltaVelocityZ *= MetersPerLegacyUnit;
                    break;
                case CaptureMoveCommand move:
                    move.X *= MetersPerLegacyUnit;
                    move.Y *= MetersPerLegacyUnit;
                    break;
            }
        }

        var camera = profile.Camera;
        camera.InitialCameraHeight *= MetersPerLegacyUnit;
        camera.MinCameraHeight *= MetersPerLegacyUnit;
        camera.MaxCameraHeight *= MetersPerLegacyUnit;
        camera.Height *= MetersPerLegacyUnit;
        foreach (var point in camera.FramingPoints)
        {
            ScalePoint(point);
        }

        if (camera.LookAt is { } lookAt)
        {
            ScalePoint(lookAt);
        }

        foreach (var expectation in profile.Expectations)
        {
            if (expectation is CaptureMinDisplacementExpectation displacement)
            {
                displacement.MinDisplacement *= MetersPerLegacyUnit;
            }
        }
    }

    private static void ScalePoint(CapturePoint point)
    {
        point.X *= MetersPerLegacyUnit;
        point.Y *= MetersPerLegacyUnit;
        point.Z *= MetersPerLegacyUnit;
    }
}
