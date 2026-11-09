#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

/// <summary>Test helper for creating synthetic unit packages on disk.</summary>
public sealed class UnitPackageTestBuilder : IDisposable
{
    private readonly string _packageDir;
    private readonly List<(string Name, ContentKind Kind, string RelPath, string Content)> _records = [];
    private readonly List<(string RelPath, string Dialect, int DialectVersion, string Content, UnitPackageProvenance? Prov)> _retained = [];
    private readonly List<UnitPackageRootRecordEntry> _roots = [];
    private string _containerKind = UnitPackageFormat.ContainerKind;
    private int _schemaVersion = UnitPackageFormat.CurrentSchemaVersion;
    private UnitPackageSourceIdentity _source = new("generals", false);
    private UnitPackageProvenance? _provenance = new("generals", "package");
    private readonly Dictionary<string, object> _extraManifestProperties = [];

    public string PackageDir => _packageDir;

    public UnitPackageTestBuilder()
    {
        _packageDir = Path.Combine(Path.GetTempPath(), "unit_pkg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_packageDir);
    }

    public UnitPackageTestBuilder WithSource(string id, bool caseSensitive)
    {
        _source = new UnitPackageSourceIdentity(id, caseSensitive);
        return this;
    }

    public UnitPackageTestBuilder WithContainerKind(string kind)
    {
        _containerKind = kind;
        return this;
    }

    public UnitPackageTestBuilder WithSchemaVersion(int version)
    {
        _schemaVersion = version;
        return this;
    }

    public UnitPackageTestBuilder WithProvenance(UnitPackageProvenance? provenance)
    {
        _provenance = provenance;
        return this;
    }

    public UnitPackageTestBuilder WithExtraManifestProperty(string name, object value)
    {
        _extraManifestProperties[name] = value;
        return this;
    }

    public UnitPackageTestBuilder AddRoot(string name, ContentKind kind = ContentKind.Unit)
    {
        _roots.Add(new UnitPackageRootRecordEntry(name, kind));
        return this;
    }

    public UnitPackageTestBuilder AddRecord(string name, ContentKind kind, string relPath, string jsonContent)
    {
        _records.Add((name, kind, relPath, jsonContent));
        return this;
    }

    public UnitPackageTestBuilder AddRetainedPayload(
        string relPath,
        string dialect,
        int dialectVersion,
        string iniText,
        UnitPackageProvenance? provenance = null)
    {
        _retained.Add((relPath, dialect, dialectVersion, iniText, provenance));
        return this;
    }

    public string Build()
    {
        var recordEntries = new List<object>();

        foreach (var (name, kind, relPath, content) in _records)
        {
            var fullPath = Path.Combine(_packageDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(fullPath, bytes);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            recordEntries.Add(new
            {
                name,
                kind = kind.ToString(),
                path = relPath,
                byteLength = bytes.Length,
                sha256,
            });
        }

        var retainedEntries = new List<object>();

        foreach (var (relPath, dialect, dialectVersion, content, prov) in _retained)
        {
            var fullPath = Path.Combine(_packageDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(fullPath, bytes);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            retainedEntries.Add(new
            {
                path = relPath,
                dialect,
                dialectVersion,
                byteLength = bytes.Length,
                sha256,
                provenance = prov is null ? null : new
                {
                    sourceId = prov.SourceId,
                    filePath = prov.FilePath,
                    line = prov.Line,
                    archiveLayer = prov.ArchiveLayer,
                },
            });
        }

        var manifestObj = new Dictionary<string, object>
        {
            ["containerKind"] = _containerKind,
            ["schemaVersion"] = _schemaVersion,
            ["source"] = new
            {
                id = _source.Id,
                caseSensitiveNames = _source.CaseSensitiveNames,
            },
            ["roots"] = _roots.ConvertAll(r => (object)new { name = r.Name, kind = r.Kind.ToString() }),
            ["records"] = recordEntries,
            ["retainedPayloads"] = retainedEntries,
        };

        if (_provenance is not null)
        {
            manifestObj["provenance"] = new
            {
                sourceId = _provenance.SourceId,
                filePath = _provenance.FilePath,
                line = _provenance.Line,
                archiveLayer = _provenance.ArchiveLayer,
            };
        }

        foreach (var (k, v) in _extraManifestProperties)
        {
            manifestObj[k] = v;
        }

        var manifestJson = JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_packageDir, UnitPackageFormat.ManifestFileName), manifestJson);

        return _packageDir;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_packageDir))
            {
                Directory.Delete(_packageDir, recursive: true);
            }
        }
        catch
        {
            // Ignore temp dir cleanup errors
        }
    }
}
