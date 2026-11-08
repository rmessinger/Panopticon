#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Tools.H5Migrate;

/// <summary>Edits a converted unit package in place and keeps the manifest digests consistent with the edited files.</summary>
internal static class UnitPackageTamper
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void EditRecordPayload(string packageDir, string recordName, Action<JsonObject> edit)
    {
        var manifest = LoadManifest(packageDir);
        var entry = FindNamed(manifest, "records", recordName);
        var fullPath = ResolvePath(packageDir, entry);
        var payload = JsonNode.Parse(File.ReadAllBytes(fullPath))!.AsObject();
        var before = payload.ToJsonString();

        edit(payload);

        Assert.NotEqual(before, payload.ToJsonString());
        Rewrite(entry, fullPath, Encoding.UTF8.GetBytes(payload.ToJsonString(Indented)));
        SaveManifest(packageDir, manifest);
    }

    public static void EditRetainedBlock(string packageDir, string recordName, Func<string, string> edit)
    {
        var manifest = LoadManifest(packageDir);
        var entry = Assert.Single(
            manifest["retainedPayloads"]!.AsArray().OfType<JsonObject>(),
            retained => Path.GetFileName(retained["path"]!.GetValue<string>()) == $"{recordName}.ini");
        var fullPath = ResolvePath(packageDir, entry);
        var text = File.ReadAllText(fullPath);
        var edited = edit(text);

        Assert.NotEqual(text, edited);
        Rewrite(entry, fullPath, Encoding.UTF8.GetBytes(edited));
        SaveManifest(packageDir, manifest);
    }

    public static void DropRecord(string packageDir, string recordName)
    {
        var manifest = LoadManifest(packageDir);
        manifest["records"]!.AsArray().Remove(FindNamed(manifest, "records", recordName));
        SaveManifest(packageDir, manifest);
    }

    public static void DuplicateRecord(string packageDir, string recordName)
    {
        var manifest = LoadManifest(packageDir);
        manifest["records"]!.AsArray().Add(FindNamed(manifest, "records", recordName).DeepClone());
        SaveManifest(packageDir, manifest);
    }

    private static JsonObject LoadManifest(string packageDir)
    {
        var text = File.ReadAllText(Path.Combine(packageDir, UnitPackageFormat.ManifestFileName));
        return JsonNode.Parse(text)!.AsObject();
    }

    private static void SaveManifest(string packageDir, JsonObject manifest)
    {
        File.WriteAllText(Path.Combine(packageDir, UnitPackageFormat.ManifestFileName), manifest.ToJsonString(Indented));
    }

    private static JsonObject FindNamed(JsonObject manifest, string arrayName, string name)
    {
        return Assert.Single(
            manifest[arrayName]!.AsArray().OfType<JsonObject>(),
            entry => entry["name"]?.GetValue<string>() == name);
    }

    private static string ResolvePath(string packageDir, JsonObject entry)
    {
        return Path.Combine(packageDir, entry["path"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));
    }

    private static void Rewrite(JsonObject entry, string fullPath, byte[] bytes)
    {
        File.WriteAllBytes(fullPath, bytes);
        entry["byteLength"] = bytes.Length;
        entry["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
