using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.IO;
using Panopticon.Tools.ReplaySketch.Model.Capture;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Card WI-H2a-17 (extended by WI-M2-03, WI-H6-03): publishes a capture profile's content imports into a
/// running game through the shared H2a/H4 layer (adapter, resolution, validation, materialization,
/// scoped publication), so imported units spawn beside native ones. Every diagnostic is reported;
/// any error leaves the game without partial content and fails the run.
/// </summary>
public static class UnitContentImportRunner
{
    public const string Ee2PathVariable = "EMPIRE_EARTH_II_PATH";

    public static bool Import(Game game, CaptureContentImport import, ITestHarnessReporter reporter)
    {
        return Import(game, import, reporter, null);
    }

    public static bool Import(
        Game game,
        CaptureContentImport import,
        ITestHarnessReporter reporter,
        HarnessPackageImportDescriptor? packageDescriptor)
    {
        if (packageDescriptor is not null)
        {
            return ImportPackage(game, packageDescriptor, reporter);
        }

        if (TryExtractPackageDescriptor(import, out var extracted) && extracted is not null)
        {
            return ImportPackage(game, extracted, reporter);
        }

        var root = Environment.GetEnvironmentVariable(Ee2PathVariable);
        if (string.IsNullOrEmpty(root))
        {
            reporter.Error($"Unmet prerequisite: {Ee2PathVariable} must point at an Empire Earth II install for content import '{import.Source}'.");
            return false;
        }

        using var database = new ZipFileSystem(Path.Combine(root, "zips/db.zip"));
        using var graphics = new ZipFileSystem(Path.Combine(root, "zips/graphics.zip"));
        using var textures = new ZipFileSystem(Path.Combine(root, "zips/textures.zip"));
        var visualFiles = new CompositeFileSystem(graphics, textures);

        // Explicit row keys publish through the full db/Units inventory; bounded profiles keep
        // their explicit DDF paths. Patch archives beside db.zip are reported, never composed.
        var sourceFiles = import.RowKeys.Count > 0
            ? Ee2SourceFiles.FromDatabase(
                database,
                graphics,
                Directory.EnumerateFiles(Path.Combine(root, "zips"), "db_*.zip").Select(path => Path.GetFileName(path)!).ToArray())
            : new Ee2SourceFiles(database, graphics, import.DdfPaths, Ee2SourceFiles.DefaultUnitTablePath);

        var adapter = new Ee2UnitAdapter(sourceFiles, new Ee2ImportPolicy(
            import.Epoch,
            import.Region,
            import.ExcludedMechanics,
            RpsDamageTypes: import.RpsDamageTypes));
        var set = import.RowKeys.Count > 0 ? adapter.ReadRows(import.RowKeys) : adapter.Read(import.UnitTypes);
        var resolution = UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());
        Report(resolution.Diagnostics, reporter);
        if (!resolution.Succeeded)
        {
            return false;
        }

        var publisher = new UnitContentPublisher(game.AssetStore);

        // Three scales stay distinct and are never conflated:
        // 1. Authored profile compatibility scale: scenario-chosen (12 in capture-mixed-source-ee2.json, converted by similarity: 12 * 0.3048 = 3.6576 m per source distance).
        // 2. Reference presentation anchor: 18.2505 * 0.3048 = 5.5627524 m per source distance (derived in Ee2ReferenceScale as 50 model units * 0.1 m * 3.6501).
        // 3. True physical source calibration: 5 m per tile (50 model units * 0.1 m/unit). Not substituted silently.
        var metersPerSourceDistance = import.GameUnitsPerSourceDistance > 0
            ? UnitConversionPolicy.ScaleEe2SourceDistance(import.GameUnitsPerSourceDistance)
            : (double?)null;
        var policy = new UnitConversionPolicy(game.SageGame, metersPerSourceDistance: metersPerSourceDistance);
        var damageEffects = import.DamageEffects.ToDictionary(
            pair => new ContentId(set.Source, ContentKind.Unit, pair.Key), pair => pair.Value);
        var impactEffectDamageTypes = import.ImpactEffectDamageTypes.ToDictionary(
            pair => new ContentId(set.Source, ContentKind.Unit, pair.Key), pair => pair.Value);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(
            policy, publisher.Lookup, visualStore: game.AssetStore, damageEffects: damageEffects,
            damageEffectLookup: name => game.AssetStore.DamageFXs.GetByName(name),
            impactEffectDamageTypes: impactEffectDamageTypes));
        Report(content.Diagnostics, reporter);
        if (!content.Succeeded)
        {
            return false;
        }

        foreach (var conversion in content.Conversions)
        {
            reporter.Info($"Conversion {conversion.Subject.Name}.{conversion.Field}: {conversion.Source} -> {conversion.Runtime:0.####} {conversion.RuntimeUnit}");
        }

        var sourceContext = new AssetImportContext(visualFiles, language: null);
        foreach (var modelKey in content.VisualAssets.Where(r => r.Kind == VisualAssetKind.Model).Select(r => r.SourceKey).Distinct())
        {
            if (new NifModelImporter().ImportModel(modelKey, sourceContext) is { } model)
            {
                Ee2TexcacheTextureInstaller.Install(game, visualFiles, Ee2NifProofScenario.CollectTextureNames(model));
            }
        }

        // The model scale is applied once, at install: the imported model's root bone binds carry
        // game units, so the runtime world matrix stays unscaled. Converted by similarity.
        var modelScale = import.GameUnitsPerSourceModelUnit is { } scale
            ? (float)UnitConversionPolicy.ScaleEe2SourceModelUnit(scale)
            : 1f;
        var installer = new ImportedVisualInstaller(
            new NifModelImporter(), new NifAnimationImporter(), sourceContext, game.AssetStore.LoadContext, modelScale);
        var publication = publisher.Publish(content, installer);
        Report(publication.Diagnostics, reporter);
        if (!publication.Succeeded)
        {
            return false;
        }

        reporter.Info($"Published {publication.Published!.Ids.Count} {import.Source} assets: {string.Join(", ", publication.Published.Ids.Where(id => id.Kind == ContentKind.Unit).Select(id => id.Name))}");
        return true;
    }

    public static bool TryExtractPackageDescriptor(CaptureContentImport import, out HarnessPackageImportDescriptor? descriptor)
    {
        descriptor = null;

        if (import.DdfPaths.Count > 0)
        {
            var first = import.DdfPaths[0];
            if (File.Exists(Path.Combine(first, UnitPackageFormat.ManifestFileName)) || Directory.Exists(first))
            {
                descriptor = new HarnessPackageImportDescriptor(
                    first,
                    "H4UnitPackage",
                    import.UnitTypes.Count > 0 ? import.UnitTypes : (import.RowKeys.Count > 0 ? import.RowKeys : null));
                return true;
            }
        }

        if (string.Equals(import.Source, "h4", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(import.Source, "package", StringComparison.OrdinalIgnoreCase))
        {
            var path = import.DdfPaths.FirstOrDefault() ?? import.RowKeys.FirstOrDefault() ?? string.Empty;
            descriptor = new HarnessPackageImportDescriptor(path, "H4UnitPackage", import.UnitTypes);
            return true;
        }

        return false;
    }

    public static bool ImportPackage(
        Game game,
        HarnessPackageImportDescriptor descriptor,
        ITestHarnessReporter reporter)
    {
        var packagePath = descriptor.PackagePath;
        if (!Directory.Exists(packagePath))
        {
            reporter.Error($"Package directory '{packagePath}' does not exist.");
            return false;
        }

        var manifestPath = Path.Combine(packagePath, UnitPackageFormat.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            reporter.Error($"Package manifest '{manifestPath}' does not exist.");
            return false;
        }

        if (!string.Equals(descriptor.Adapter, "H4UnitPackage", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(descriptor.Adapter, "H4Package", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(descriptor.Adapter, "Package", StringComparison.OrdinalIgnoreCase))
        {
            reporter.Error($"Unsupported package adapter '{descriptor.Adapter}'; expected 'H4UnitPackage'.");
            return false;
        }

        var reader = new H4UnitPackageReader(packagePath);
        var roots = descriptor.Roots ?? [];
        var set = reader.Read(roots);
        Report(set.Diagnostics, reporter);
        if (set.Diagnostics.Any(d => d.IsError))
        {
            return false;
        }

        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        Report(resolution.Diagnostics, reporter);
        if (!resolution.Succeeded)
        {
            return false;
        }

        var publisher = new UnitContentPublisher(game.AssetStore);
        var context = new UnitMaterializationContext(
            new UnitConversionPolicy(game.SageGame),
            publisher.Lookup,
            reader.Natives,
            game.AssetStore);

        var materialized = UnitRuntimeMaterializer.Materialize(resolution, context);
        Report(materialized.Diagnostics, reporter);
        if (!materialized.Succeeded)
        {
            return false;
        }

        var overrides = materialized.Assets.Keys.ToList();
        var publication = publisher.Publish(materialized, null, overrides);
        Report(publication.Diagnostics, reporter);
        if (!publication.Succeeded)
        {
            return false;
        }

        reporter.Info($"Published {publication.Published!.Ids.Count} {set.Source.Id} assets from H4 package '{packagePath}': {string.Join(", ", publication.Published.Ids.Where(id => id.Kind == ContentKind.Unit).Select(id => id.Name))}");
        return true;
    }

    private static void Report(System.Collections.Generic.IEnumerable<ImportDiagnostic> diagnostics, ITestHarnessReporter reporter)
    {
        foreach (var diagnostic in diagnostics.Where(d => d.Severity != ImportDiagnosticSeverity.Info))
        {
            if (diagnostic.IsError)
            {
                reporter.Error(diagnostic.ToString());
            }
            else
            {
                reporter.Info(diagnostic.ToString());
            }
        }
    }
}
