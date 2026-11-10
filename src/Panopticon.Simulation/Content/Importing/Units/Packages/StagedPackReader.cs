#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Reads a loose pack directory containing one or more H4 unit package directories, resolves each
/// through existing resolvers, and publishes through <see cref="UnitContentPublisher"/> under its own
/// <see cref="AssetStore"/> scope, in pack order.
/// </summary>
public sealed class StagedPackReader
{
    private readonly string _packRoot;

    public StagedPackReader(string packRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packRoot);
        _packRoot = packRoot;
    }

    public string PackRoot => _packRoot;

    /// <summary>
    /// Discovers all H4 package directories contained within the loose pack directory.
    /// If the pack directory itself contains manifest.json, it is treated as a single-package pack.
    /// Otherwise, checks for pack.json packages ordering or enumerates subdirectories ordered ordinally.
    /// </summary>
    public IReadOnlyList<string> DiscoverPackageDirectories()
    {
        if (!Directory.Exists(_packRoot))
        {
            return [];
        }

        if (File.Exists(Path.Combine(_packRoot, UnitPackageFormat.ManifestFileName)))
        {
            return [_packRoot];
        }

        var packConfigPath = Path.Combine(_packRoot, "pack.json");
        if (File.Exists(packConfigPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(packConfigPath));
                if (doc.RootElement.TryGetProperty("packages", out var packagesElem) && packagesElem.ValueKind == JsonValueKind.Array)
                {
                    var declared = new List<string>();
                    foreach (var elem in packagesElem.EnumerateArray())
                    {
                        var rel = elem.GetString();
                        if (!string.IsNullOrWhiteSpace(rel))
                        {
                            var full = Path.GetFullPath(Path.Combine(_packRoot, rel));
                            if (File.Exists(Path.Combine(full, UnitPackageFormat.ManifestFileName)))
                            {
                                declared.Add(full);
                            }
                        }
                    }

                    if (declared.Count > 0)
                    {
                        return declared;
                    }
                }
            }
            catch
            {
                // Fall back to directory enumeration
            }
        }

        return Directory.GetDirectories(_packRoot)
            .Where(dir => File.Exists(Path.Combine(dir, UnitPackageFormat.ManifestFileName)))
            .OrderBy(dir => Path.GetFileName(dir), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Opens each package in the pack through <see cref="H4UnitPackageReader"/>, the only adapter path.
    /// </summary>
    public IReadOnlyList<H4UnitPackageReader> OpenPackageReaders()
    {
        var dirs = DiscoverPackageDirectories();
        return dirs.Select(d => new H4UnitPackageReader(d)).ToList();
    }

    /// <summary>
    /// Reads, resolves, materializes, and publishes each package in the pack in order into its own
    /// AssetStore scope. On any error, all published scopes from this pack are rolled back and
    /// ScopeDepth remains unchanged.
    /// </summary>
    public StagedPackLoadResult LoadAndPublish(
        AssetStore store,
        SageGame game = SageGame.CncGenerals,
        UnitCapabilityRegistry? registry = null,
        IUnitVisualInstaller? visualInstaller = null,
        IReadOnlyCollection<ContentId>? overrides = null,
        IReadOnlyCollection<string>? rootNames = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        var diagnostics = new List<ImportDiagnostic>();

        if (!Directory.Exists(_packRoot))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportStage.Read,
                UnitPackageDiagnosticCodes.FileMissing,
                $"Pack directory '{_packRoot}' does not exist."));
            return new StagedPackLoadResult(false, diagnostics, null);
        }

        var packageDirs = DiscoverPackageDirectories();
        if (packageDirs.Count == 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportStage.Read,
                UnitPackageDiagnosticCodes.FileMissing,
                $"No H4 unit packages found in pack directory '{_packRoot}'."));
            return new StagedPackLoadResult(false, diagnostics, null);
        }

        var packOverrides = new HashSet<ContentId>(overrides ?? []);
        ReadPackJsonOverrides(_packRoot, packOverrides);

        var publisher = new UnitContentPublisher(store);
        var reg = registry ?? GeneralsUnitAdapter.CreateRegistry();
        var initialDepth = store.ScopeDepth;
        var publishedList = new List<PublishedUnitContent>();

        foreach (var packageDir in packageDirs)
        {
            var reader = new H4UnitPackageReader(packageDir);
            var set = reader.Read(rootNames ?? []);
            diagnostics.AddRange(set.Diagnostics);

            if (set.Diagnostics.Any(d => d.IsError))
            {
                Rollback(publisher, publishedList, initialDepth, store);
                return new StagedPackLoadResult(false, diagnostics, null);
            }

            var resolution = UnitImportResolution.Resolve(set, reg);
            diagnostics.AddRange(resolution.Diagnostics);

            if (!resolution.Succeeded)
            {
                Rollback(publisher, publishedList, initialDepth, store);
                return new StagedPackLoadResult(false, diagnostics, null);
            }

            var context = new UnitMaterializationContext(
                new UnitConversionPolicy(game),
                publisher.Lookup,
                reader.Natives,
                store);

            var materialized = UnitRuntimeMaterializer.Materialize(resolution, context);
            diagnostics.AddRange(materialized.Diagnostics);

            if (!materialized.Succeeded)
            {
                Rollback(publisher, publishedList, initialDepth, store);
                return new StagedPackLoadResult(false, diagnostics, null);
            }

            var packageOverrides = new HashSet<ContentId>(packOverrides);
            ReadManifestOverrides(packageDir, reader.Source, packageOverrides);

            // If a wildcard override is declared, include all materialized assets from this package
            if (packageOverrides.Any(o => o.Name == "*"))
            {
                foreach (var id in materialized.Assets.Keys)
                {
                    packageOverrides.Add(id);
                }
            }

            var pubResult = publisher.Publish(materialized, visualInstaller, packageOverrides);
            diagnostics.AddRange(pubResult.Diagnostics);

            if (!pubResult.Succeeded || pubResult.Published is null)
            {
                Rollback(publisher, publishedList, initialDepth, store);
                return new StagedPackLoadResult(false, diagnostics, null);
            }

            publishedList.Add(pubResult.Published);
        }

        return new StagedPackLoadResult(true, diagnostics, new StagedPackPublication(_packRoot, publishedList));
    }

    private static void Rollback(UnitContentPublisher publisher, List<PublishedUnitContent> publishedList, int initialDepth, AssetStore store)
    {
        for (var i = publishedList.Count - 1; i >= 0; i--)
        {
            try
            {
                publisher.Unload(publishedList[i]);
            }
            catch
            {
                // In case of mid-unload errors, ensure we don't throw from cleanup
            }
        }

        publishedList.Clear();

        while (store.ScopeDepth > initialDepth)
        {
            store.PopScope();
        }
    }

    private static void ReadPackJsonOverrides(string packRoot, HashSet<ContentId> overrides)
    {
        var packJsonPath = Path.Combine(packRoot, "pack.json");
        if (!File.Exists(packJsonPath))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packJsonPath));
            if (!doc.RootElement.TryGetProperty("overrides", out var prop) || prop.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var defaultSource = ContentSource.Generals;
            foreach (var elem in prop.EnumerateArray())
            {
                if (elem.ValueKind == JsonValueKind.String)
                {
                    var name = elem.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        overrides.Add(new ContentId(defaultSource, ContentKind.Unit, name));
                    }
                }
                else if (elem.ValueKind == JsonValueKind.Object)
                {
                    var name = elem.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                    var kind = ContentKind.Unit;
                    if (elem.TryGetProperty("kind", out var kProp) && StrictEnumParser.TryParse<ContentKind>(kProp, out var parsedKind, out _))
                    {
                        kind = parsedKind.Value;
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        overrides.Add(new ContentId(defaultSource, kind, name));
                    }
                }
            }
        }
        catch
        {
            // Optional configuration
        }
    }

    private static void ReadManifestOverrides(string packageDir, ContentSource source, HashSet<ContentId> overrides)
    {
        var manifestPath = Path.Combine(packageDir, UnitPackageFormat.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var (manifest, _) = UnitPackageManifestReader.ParseAndValidate(json);
            if (manifest?.Overrides is { Count: > 0 } manifestOverrides)
            {
                foreach (var overrideId in manifestOverrides)
                {
                    overrides.Add(overrideId);
                }
            }
        }
        catch
        {
            // Fall through if manifest read failed; reader will report it
        }
    }
}
