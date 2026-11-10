#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Panopticon.Content;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data.Ini;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Tests.Content.Importing.Units;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Importing.FaultInjection;

internal static class H7FaultInjectionFixtures
{
    public const string TamperedScope = "h7-tampered-scope";

    public const string PrimaryObjectType = "SyntheticStructure";

    public const string SecondaryObjectType = "SyntheticDepot";

    public static void WithTempRoot(Action<string> body)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonH7FaultInjection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            body(tempRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    public static MapConversion ConvertMap(string tempRoot)
    {
        const int lattice = 129;
        const int border = 6;

        var heights = new ushort[lattice, lattice];

        for (var y = 0; y < lattice; y++)
        {
            for (var x = 0; x < lattice; x++)
            {
                heights[x, y] = (ushort)(24 + (((x * 3) + (y * 5)) % 17));
            }
        }

        var mapFile = MapFile.CreateFromHeights(heights, border);

        var primary = new MapObject(new Vector3(120, 140, 25), 0.75f, RoadType.None, PrimaryObjectType);
        primary.Properties.AddReal("AuthoredElevationOffset", 12.5f);
        mapFile.ObjectsList.AddObject(primary);
        mapFile.ObjectsList.AddObject(new MapObject(new Vector3(60, 80, 25), 0.25f, RoadType.None, SecondaryObjectType));

        var mapPath = Path.Combine(tempRoot, "SyntheticFaultInjection.map");
        using (var stream = File.Create(mapPath))
        {
            mapFile.WriteTo(stream);
        }

        var packageDir = Path.Combine(tempRoot, "original-map-package");
        var exitCode = RunCli(["convert-map", "--input", mapPath, "--out", packageDir], out _, out var stderr);
        Assert.True(exitCode == 0, $"convert-map failed: {stderr}");

        return new MapConversion(mapPath, packageDir, ParseMapFile(mapPath));
    }

    public static UnitConversion ConvertRoster(string tempRoot)
    {
        var installRoot = H7SyntheticIniRoster.WriteTo(Path.Combine(tempRoot, "install"));
        var packageDir = Path.Combine(tempRoot, "original-unit-package");

        var exitCode = RunCli(
            ["convert-ini", "--game-path", installRoot, "--out", packageDir, .. H7SyntheticIniRoster.RosterRoots],
            out _,
            out var stderr);
        Assert.True(exitCode == 0, $"convert-ini failed (exit {exitCode}): {stderr}");

        var (adapter, resolution) = ResolveRoster(installRoot);
        return new UnitConversion(installRoot, packageDir, adapter.Natives, resolution, new IniRetainedPayloadProvider(new DiskFileSystem(installRoot)));
    }

    public static int RunCli(IReadOnlyList<string> arguments, out string standardOut, out string errorOut)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = H5MigrateCli.Run(arguments.ToArray(), stdout, stderr, CancellationToken.None);

        standardOut = stdout.ToString();
        errorOut = stderr.ToString();
        return exitCode;
    }

    private static (GeneralsUnitAdapter Adapter, UnitImportResolution Resolution) ResolveRoster(string installRoot)
    {
        using var fileSystem = new CompositeFileSystem(new DiskFileSystem(installRoot), new BigFileSystem(installRoot));
        var staging = GeneralsIniLoader.Load(fileSystem);
        var adapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);

        var roots = new List<string> { H7SyntheticIniRoster.DefaultTemplateName };
        roots.AddRange(H7SyntheticIniRoster.RosterRoots);

        var set = adapter.Read(roots);
        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        Assert.True(
            resolution.Succeeded,
            string.Join("; ", resolution.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

        return (adapter, resolution);
    }

    private static MapFile ParseMapFile(string mapPath)
    {
        using var stream = File.OpenRead(mapPath);
        using var mapStream = MapFile.Decompress(stream);
        return MapFile.FromStream(mapStream);
    }

    public sealed record MapConversion(string MapPath, string PackageDir, MapFile SourceMap);

    public sealed record UnitConversion(
        string InstallRoot,
        string PackageDir,
        ILegacyNativeTable? Natives,
        UnitImportResolution Resolution,
        IUnitPackageRetainedPayloadProvider Provider);
}
