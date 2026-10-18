using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// WI-A5-09: the run bundle surface. A bundle is a directory holding the run manifest
/// (<c>run.json</c>, a <see cref="RunOutput"/> whose internal paths are bundle-relative per
/// WI-A5-07), the world-state artifact (<c>state.bin</c>), and the structured event stream
/// (<c>events.json</c>). This class writes the event stream and loads + validates a whole
/// bundle: required files must exist, the events schema version must be understood, and absent
/// optional T2 detail degrades explicitly instead of failing.
/// </summary>
public static class RunBundle
{
    public const string ManifestFileName = "run.json";
    public const string StateFileName = "state.bin";
    public const string EventsFileName = "events.json";
    public const int CurrentEventsSchemaVersion = 1;

    private static readonly JsonSerializerOptions EventsJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes the structured event stream with its schema version header.</summary>
    public static void WriteEvents(string path, IReadOnlyList<RunEventRecord> events)
    {
        var document = new RunEventsDocument(CurrentEventsSchemaVersion, [.. events]);
        File.WriteAllText(path, JsonSerializer.Serialize(document, EventsJsonOptions));
    }

    /// <summary>
    /// Loads and validates a bundle. <paramref name="manifestOrBundlePath"/> may be the manifest
    /// file or the bundle directory. Manifest-internal artifact paths resolve against the
    /// manifest's location (WI-A5-07), so a relocated bundle loads unchanged.
    /// </summary>
    public static RunBundleContents Load(string manifestOrBundlePath)
    {
        var manifestPath = Directory.Exists(manifestOrBundlePath)
            ? Path.Combine(manifestOrBundlePath, ManifestFileName)
            : manifestOrBundlePath;
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"Run bundle is missing the required manifest '{manifestPath}'.");
        }

        var manifest = RunOutput.LoadFromFile(manifestPath)
            ?? throw new InvalidOperationException(
                $"Run bundle manifest '{manifestPath}' is empty or unreadable.");

        if (string.IsNullOrEmpty(manifest.SnapshotArtifactPath)
            || !File.Exists(manifest.SnapshotArtifactPath))
        {
            throw new InvalidOperationException(
                $"Run bundle is missing the required state artifact '"
                + $"{manifest.SnapshotArtifactPath ?? StateFileName}' (expected beside "
                + $"'{manifestPath}').");
        }

        var eventsPath = Path.Combine(
            Path.GetDirectoryName(manifestPath)!, EventsFileName);
        if (!File.Exists(eventsPath))
        {
            throw new InvalidOperationException(
                $"Run bundle is missing the required event stream '{eventsPath}'.");
        }

        var events = ReadEvents(eventsPath);
        return new RunBundleContents(
            manifestPath,
            manifest,
            manifest.SnapshotArtifactPath!,
            eventsPath,
            events,
            HasTriggeredDetail: manifest.TriggeredCaptures is { Count: > 0 });
    }

    private static IReadOnlyList<RunEventRecord> ReadEvents(string eventsPath)
    {
        RunEventsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RunEventsDocument>(
                File.ReadAllText(eventsPath), EventsJsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                $"Run bundle event stream '{eventsPath}' is not readable JSON: {e.Message}", e);
        }

        if (document is null)
        {
            throw new InvalidOperationException(
                $"Run bundle event stream '{eventsPath}' is empty.");
        }

        if (document.SchemaVersion != CurrentEventsSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Run bundle event stream '{eventsPath}' has schema version "
                + $"{document.SchemaVersion}; this reader understands "
                + $"{CurrentEventsSchemaVersion}.");
        }

        return document.Events;
    }

    private sealed record RunEventsDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        List<RunEventRecord> Events);
}

/// <summary>Everything a loaded, validated run bundle provides (WI-A5-09).</summary>
public sealed record RunBundleContents(
    string ManifestPath,
    RunOutput Manifest,
    string StateArtifactPath,
    string EventsPath,
    IReadOnlyList<RunEventRecord> Events,
    bool HasTriggeredDetail);
