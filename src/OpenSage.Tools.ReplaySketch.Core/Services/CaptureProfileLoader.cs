using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSage.Tools.ReplaySketch.Model.Capture;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Loads <see cref="CaptureProfile"/> objects from JSON profile files: comment-tolerant,
/// trailing-comma tolerant, enum values as strings, polymorphic commands and expectations
/// discriminated by their <c>Type</c> property.
/// </summary>
public static class CaptureProfileLoader
{
    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Loads a capture profile from the embedded profile resource with the given bare filename
    /// (e.g. <c>construction-capture.json</c>, under the <c>Profiles/</c> folder).
    /// </summary>
    public static CaptureProfile LoadEmbeddedCapture(string resourceName)
    {
        var assembly = typeof(CaptureProfileLoader).Assembly;
        var fullName = $"OpenSage.Tools.ReplaySketch.Core.Profiles.{resourceName}";

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
        var prefix = "OpenSage.Tools.ReplaySketch.Core.Profiles.";
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix + "capture-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name => name[prefix.Length..])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static CaptureProfile Deserialize(Stream stream, string source)
    {
        return JsonSerializer.Deserialize<CaptureProfile>(stream, SerializationOptions)
               ?? throw new InvalidOperationException($"Deserialization of capture profile '{source}' returned null.");
    }
}
