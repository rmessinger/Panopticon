using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

namespace Panopticon.Tools.ReplaySketch.Services;

public sealed class HarnessPresetStore
{
    private readonly string _root;
    public HarnessPresetStore(string? root = null) => _root = root ?? Path.Combine(PlatformPaths.AppDataBase, "harness-presets");
    public IReadOnlyList<string> List() => !Directory.Exists(_root) ? [] : Directory.GetFiles(_root, "*.json").Select(path => Path.GetFileNameWithoutExtension(path)!).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    public void Save(string name, HarnessLaunchRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(_root);
        File.WriteAllText(PathFor(name), JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }));
    }

    public HarnessLaunchRequest Load(string name) => JsonSerializer.Deserialize<HarnessLaunchRequest>(File.ReadAllText(PathFor(name))) ?? throw new InvalidDataException("The preset is empty.");
    public void Delete(string name) { var path = PathFor(name); if (File.Exists(path)) File.Delete(path); }

    public (HarnessLaunchRequest? Request, IReadOnlyList<string> UnknownKeys) ImportConfig(string path, HarnessCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(catalog);
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var document = JsonDocument.Parse(File.ReadAllText(path), options);
        if (!document.RootElement.TryGetProperty("Scenario", out var scenario)) return (null, ["Scenario"]);
        var known = catalog.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
        var overrides = new Dictionary<string, JsonNode?>(); var unknown = new List<string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals("Scenario")) continue;
            if (!known.TryGetValue(property.Name, out var descriptor)) { unknown.Add(property.Name); continue; }
            var value = JsonNode.Parse(property.Value.GetRawText());
            if (!JsonNode.DeepEquals(value, descriptor.Default)) overrides[property.Name] = value;
        }

        return (new HarnessLaunchRequest(scenario.GetString() ?? string.Empty, overrides), unknown);
    }

    private string PathFor(string name) => Path.Combine(_root, Sanitize(name) + ".json");
    private static string Sanitize(string name) => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
}
