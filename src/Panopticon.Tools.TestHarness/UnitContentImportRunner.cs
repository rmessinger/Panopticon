using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.IO;
using Panopticon.Tools.ReplaySketch.Model.Capture;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Card WI-H2a-17 (extended by WI-M2-03): publishes a capture profile's content imports into a
/// running game through the shared H2a layer (adapter, resolution, validation, materialization,
/// scoped publication), so imported units spawn beside native ones. Every diagnostic is reported;
/// any error leaves the game without partial content and fails the run.
/// </summary>
public static class UnitContentImportRunner
{
    public const string Ee2PathVariable = "EMPIRE_EARTH_II_PATH";

    public static bool Import(Game game, CaptureContentImport import, ITestHarnessReporter reporter)
    {
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
                Directory.EnumerateFiles(Path.Combine(root, "zips"), "db_*.zip").Select(Path.GetFileName).ToArray())
            : new Ee2SourceFiles(database, graphics, import.DdfPaths, Ee2SourceFiles.DefaultUnitTablePath);

        var adapter = new Ee2UnitAdapter(sourceFiles, new Ee2ImportPolicy(import.Epoch, import.Region, import.ExcludedMechanics));
        var set = import.RowKeys.Count > 0 ? adapter.ReadRows(import.RowKeys) : adapter.Read(import.UnitTypes);
        var resolution = UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());
        Report(resolution.Diagnostics, reporter);
        if (!resolution.Succeeded)
        {
            return false;
        }

        var publisher = new UnitContentPublisher(game.AssetStore);
        var policy = new UnitConversionPolicy(game.SageGame, gameUnitsPerSourceDistance: import.GameUnitsPerSourceDistance);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(policy, publisher.Lookup, visualStore: game.AssetStore));
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

        var installer = new ImportedVisualInstaller(new NifModelImporter(), new NifAnimationImporter(), sourceContext, game.AssetStore.LoadContext);
        var publication = publisher.Publish(content, installer);
        Report(publication.Diagnostics, reporter);
        if (!publication.Succeeded)
        {
            return false;
        }

        reporter.Info($"Published {publication.Published!.Ids.Count} {import.Source} assets: {string.Join(", ", publication.Published.Ids.Where(id => id.Kind == ContentKind.Unit).Select(id => id.Name))}");
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
