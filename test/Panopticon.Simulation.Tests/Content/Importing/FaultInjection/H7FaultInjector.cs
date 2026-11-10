#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Importing.FaultInjection;

/// <summary>
/// Test-side semantic fault injector (WI-H7-04). Each edit mutates a copy of a converted package and
/// recomputes every manifest SHA-256 and byte length it touches, so the package stays integrity-valid.
/// Only the H5-03 comparer against the original can then catch the change.
/// </summary>
internal static class H7FaultInjector
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var targetPath = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath);
        }
    }

    public static void EditMapManifest(string packageDir, Func<MapPackageManifest, MapPackageManifest> edit)
    {
        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(manifestPath));
        Assert.NotNull(manifest);
        File.WriteAllText(manifestPath, MapPackageManifestJson.Serialize(edit(manifest!)));
    }

    public static void EditMapLayer(string packageDir, string layerKind, Func<byte[], byte[]> edit)
    {
        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(manifestPath));
        Assert.NotNull(manifest);

        var target = manifest!.Layers.First(layer => layer.LayerKind == layerKind);
        var fullPath = ResolvePath(packageDir, target.Path);
        var bytes = edit(File.ReadAllBytes(fullPath));
        File.WriteAllBytes(fullPath, bytes);

        var layers = manifest.Layers
            .Select(layer => ReferenceEquals(layer, target) ? layer with { ByteLength = bytes.Length, Sha256 = Sha256(bytes) } : layer)
            .ToArray();
        File.WriteAllText(manifestPath, MapPackageManifestJson.Serialize(manifest with { Layers = layers }));
    }

    public static void EditMapRetainedSection(string packageDir, string sectionName, Func<byte[], byte[]> edit)
    {
        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(manifestPath));
        Assert.NotNull(manifest);

        var target = manifest!.RetainedSections.Single(section => section.SectionName == sectionName);
        var fullPath = ResolvePath(packageDir, target.Path);
        var bytes = edit(File.ReadAllBytes(fullPath));
        File.WriteAllBytes(fullPath, bytes);

        var sections = manifest.RetainedSections
            .Select(section => ReferenceEquals(section, target) ? section with { ByteLength = bytes.Length, Sha256 = Sha256(bytes) } : section)
            .ToArray();
        File.WriteAllText(manifestPath, MapPackageManifestJson.Serialize(manifest with { RetainedSections = sections }));
    }

    public static void EditUnitRecord(string packageDir, string recordName, Action<JsonObject> edit)
    {
        var manifest = LoadUnitManifest(packageDir);
        var entry = FindUnitEntry(manifest, "records", recordName);
        var fullPath = ResolvePath(packageDir, entry["path"]!.GetValue<string>());
        var payload = JsonNode.Parse(File.ReadAllBytes(fullPath))!.AsObject();
        var before = payload.ToJsonString();

        edit(payload);
        Assert.NotEqual(before, payload.ToJsonString());

        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString(Indented));
        File.WriteAllBytes(fullPath, bytes);
        entry["byteLength"] = bytes.Length;
        entry["sha256"] = Sha256(bytes);
        SaveUnitManifest(packageDir, manifest);
    }

    public static void EditUnitRetainedPayload(string packageDir, string fileName, Func<byte[], byte[]> edit)
    {
        var manifest = LoadUnitManifest(packageDir);
        var entry = Assert.Single(
            manifest["retainedPayloads"]!.AsArray().OfType<JsonObject>(),
            retained => Path.GetFileName(retained["path"]!.GetValue<string>()) == fileName);
        var fullPath = ResolvePath(packageDir, entry["path"]!.GetValue<string>());
        var bytes = edit(File.ReadAllBytes(fullPath));
        File.WriteAllBytes(fullPath, bytes);
        entry["byteLength"] = bytes.Length;
        entry["sha256"] = Sha256(bytes);
        SaveUnitManifest(packageDir, manifest);
    }

    public static void DropUnitRecord(string packageDir, string recordName)
    {
        var manifest = LoadUnitManifest(packageDir);
        var entry = FindUnitEntry(manifest, "records", recordName);
        var fullPath = ResolvePath(packageDir, entry["path"]!.GetValue<string>());
        manifest["records"]!.AsArray().Remove(entry);
        File.Delete(fullPath);
        SaveUnitManifest(packageDir, manifest);
    }

    public static void AssertMapDigestsHold(string packageDir)
    {
        var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(Path.Combine(packageDir, MapPackageFormat.ManifestFileName)));
        Assert.NotNull(manifest);

        foreach (var layer in manifest!.Layers)
        {
            AssertDigest(packageDir, layer.Path, layer.ByteLength, layer.Sha256);
        }

        foreach (var section in manifest.RetainedSections)
        {
            AssertDigest(packageDir, section.Path, section.ByteLength, section.Sha256);
        }
    }

    public static void AssertUnitDigestsHold(string packageDir)
    {
        var manifest = LoadUnitManifest(packageDir);
        var entries = manifest["records"]!.AsArray().OfType<JsonObject>()
            .Concat(manifest["retainedPayloads"]!.AsArray().OfType<JsonObject>());

        foreach (var entry in entries)
        {
            AssertDigest(packageDir, entry["path"]!.GetValue<string>(), entry["byteLength"]!.GetValue<long>(), entry["sha256"]!.GetValue<string>());
        }
    }

    private static void AssertDigest(string packageDir, string relativePath, long byteLength, string sha256)
    {
        var bytes = File.ReadAllBytes(ResolvePath(packageDir, relativePath));
        Assert.Equal(byteLength, bytes.Length);
        Assert.Equal(sha256, Sha256(bytes));
    }

    private static JsonObject LoadUnitManifest(string packageDir)
    {
        return JsonNode.Parse(File.ReadAllText(Path.Combine(packageDir, UnitPackageFormat.ManifestFileName)))!.AsObject();
    }

    private static void SaveUnitManifest(string packageDir, JsonObject manifest)
    {
        File.WriteAllText(Path.Combine(packageDir, UnitPackageFormat.ManifestFileName), manifest.ToJsonString(Indented));
    }

    private static JsonObject FindUnitEntry(JsonObject manifest, string arrayName, string name)
    {
        return Assert.Single(
            manifest[arrayName]!.AsArray().OfType<JsonObject>(),
            entry => entry["name"]?.GetValue<string>() == name);
    }

    private static string ResolvePath(string packageDir, string relativePath)
    {
        return Path.Combine(packageDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
