using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Panopticon.Content;
using Panopticon.Data;
using Panopticon.Data.Ini;
using Panopticon.Data.Map;
using Panopticon.Data.Tga;
using Panopticon.IO;
using Panopticon.Tools.ReplaySketch.Services;
using Veldrid.ImageSharp;

namespace Panopticon.Tools.TestHarness;

public static class TerrainTextureBakeRunner
{
    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var root = TerrainTexturePack.DefaultRoot;
        reporter.Info($"Offline terrain texture pack: {root}");
        try
        {
            using var files = installation.CreateFileSystem();
            var assets = new AssetStore(installation.Game.Game, files, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
            assets.PushScope();
            var terrainIni = files.GetFile("Data/INI/Terrain.ini")
                ?? throw new FileNotFoundException("TerrainTextureBake requires Data/INI/Terrain.ini.");
            new IniParser(terrainIni, assets, installation.Game.Game, new IniDataContext(), Encoding.ASCII).ParseFile();
            HashSet<string>? requiredTextures = null;
            string? mapPath = null;
            if (!string.IsNullOrWhiteSpace(config.ScenarioPath))
            {
                mapPath = config.ScenarioPath.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
                    ? config.ScenarioPath
                    : (File.Exists(config.ScenarioPath) ? CaptureProfileLoader.LoadCaptureFromFile(config.ScenarioPath)
                        : CaptureProfileLoader.LoadEmbeddedCapture(config.ScenarioPath)).MapPath;
                var mapEntry = files.GetFile(mapPath) ?? throw new FileNotFoundException($"Map was not found: {mapPath}");
                var map = MapFile.FromFileSystemEntry(mapEntry);
                requiredTextures = map.BlendTileData.Textures.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var sources = new List<(FileSystemEntry Entry, int Size)>();
            var missing = new List<string>();
            foreach (var texture in assets.TerrainTextures.Where(t => !string.IsNullOrWhiteSpace(t.Texture)
                && (requiredTextures == null || requiredTextures.Contains(t.Name)))
                .DistinctBy(t => t.Texture, StringComparer.OrdinalIgnoreCase).OrderBy(t => t.Texture, StringComparer.Ordinal))
            {
                var path = Path.Combine("Art", "Terrain", texture.Texture);
                var entry = files.GetFile(path);
                if (entry == null)
                {
                    missing.Add(path);
                    reporter.Info($"Skipping unavailable declared terrain image: {path}");
                    continue;
                }

                try
                {
                    sources.Add((entry, TgaFile.GetSquareTextureSize(entry)));
                }
                catch (InvalidOperationException)
                {
                    reporter.Info($"Skipping non-square terrain image: {entry.FilePath}");
                }
            }

            if (sources.Count == 0)
            {
                reporter.Error("No square terrain TGA images were found.");
                return 1;
            }

            var sizes = requiredTextures == null ? sources.Select(s => s.Size).Distinct().Order().ToArray()
                : new[] { sources.Max(s => s.Size) };
            reporter.Info($"Declared terrain images: {sources.Count}; array sizes: {string.Join(", ", sizes)}.");
            var pack = new TerrainTexturePack(root);
            var created = 0;
            var reused = 0;
            foreach (var (entry, originalSize) in sources)
            {
                foreach (var size in sizes.Where(size => size >= originalSize))
                {
                    if (pack.Bake(entry, size))
                    {
                        created++;
                    }
                    else
                    {
                        reused++;
                    }
                }

                reporter.Info($"Prepared: {entry.FilePath}");
            }

            var report = new { Algorithm = PrefilteredTerrainMipBuilder.AlgorithmVersion, MapPath = mapPath, SourceCount = sources.Count, Sizes = sizes, Created = created, Reused = reused, MissingSourceImages = missing };
            var reportPath = Path.Combine(root, $"bake-report-{DateTime.UtcNow:yyyyMMdd_HHmmss}-{Guid.NewGuid():N}.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            reporter.Info($"Terrain bake complete: {created} created, {reused} reused. Report: {reportPath}");
            return 0;
        }
        catch (Exception ex)
        {
            reporter.Error($"Terrain bake failed; existing and partial outputs preserved: {ex.Message}");
            return 1;
        }
    }
}
