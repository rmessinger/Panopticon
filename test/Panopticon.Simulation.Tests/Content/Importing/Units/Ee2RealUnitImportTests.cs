using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.IO;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Cards WI-H2a-15/16 against the operator install: the bounded maceman closure reads, resolves,
/// and keeps authored values. Requires EMPIRE_EARTH_II_PATH; no EE2 asset is committed.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2RealUnitImportTests(ITestOutputHelper output)
{
    /// <summary>Unsupported maceman mechanics the slice leaves out; each is reported as excluded.</summary>
    public static readonly string[] Exclusions =
    [
        "ownertype", "rps", "FootprintType", "SizeY", "mass", "popCount", "attributes", "stance",
        "Garrisonable", "Convertable", "ConvertAttack", "Cost", "UPGRADEREFS", "Attack.applyDamageTime",
    ];

    internal static Ee2SourceFiles OpenInstall()
    {
        var root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH")
            ?? throw new InvalidOperationException("Interactive test requires EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install.");

        return new Ee2SourceFiles(
            new ZipFileSystem(Path.Combine(root, "zips/db.zip")),
            new ZipFileSystem(Path.Combine(root, "zips/graphics.zip")),
            ["db/Units/human.ddf", "db/Units/heavy_infantry1.ddf"],
            Ee2SourceFiles.DefaultUnitTablePath);
    }

    private static readonly ContentId Root = new(ContentSource.Ee2, ContentKind.Unit, "HeavyInfantry1UpgradeEpoch1");

    private static UnitImportResolution Resolve(params string[] exclusions)
    {
        var set = new Ee2UnitAdapter(OpenInstall(), new Ee2ImportPolicy(1, "we", exclusions)).Read(["HeavyInfantry1"]);
        return UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());
    }

    [Fact]
    public void MacemanClosureResolvesWithAuthoredValues()
    {
        var resolution = Resolve(Exclusions);
        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));

        var unit = resolution.Get<UnitRecord>(Root);
        var attack = resolution.Get<AbilityRecord>(unit.ResolvedModules.Single(m => m.Tag == "Attack").Ability!.Value);
        var weapon = resolution.Get<WeaponRecord>(attack.Weapon.Value);
        var locomotor = resolution.Get<LocomotorRecord>(unit.Locomotors.Resolved.Single());
        var visual = resolution.Get<VisualStateRecord>(unit.Visual.Value);

        Assert.Equal(75, unit.MaxHealth.Value.Value);
        Assert.Equal(5, unit.VisionRange.Value.Value);
        Assert.Equal(37, unit.BuildTime.Value.Value);
        Assert.Equal(5, weapon.PrimaryDamage.Value.Value);
        Assert.Equal(0.8, weapon.AttackRange.Value.Value);
        Assert.Equal(1.5, weapon.ReloadTime.Value.Value);
        Assert.Equal(1.725, locomotor.Speed.Value.Value);
        Assert.Equal(720, locomotor.TurnRate.Value.Value);
        Assert.Equal("lhi1_maceman_we", visual.ModelKey.Value);
        Assert.Equal("lhi1_maceman_we#kf=Graphics/Humans/sh_maceman_attack01.kf", visual.StateAnimations["Attack"].Value);

        foreach (var d in resolution.Diagnostics.Where(d => d.Code == DiagnosticCodes.ExcludedByPolicy))
        {
            output.WriteLine(d.ToString());
        }
    }

    [Fact]
    public void WithoutExclusionsTheUnitIsBlocked()
    {
        var resolution = Resolve();

        Assert.False(resolution.Succeeded);
        Assert.Contains(resolution.Diagnostics, d => d.Code == DiagnosticCodes.UnsupportedMechanic && d.Message.Contains("ConvertAttack"));
    }

    [Fact]
    public void VisualAssetsVerifyAgainstTheGraphicsArchive()
    {
        var files = OpenInstall();
        var resolution = Resolve(Exclusions);
        var content = Panopticon.Content.Importing.Units.Materialization.UnitRuntimeMaterializer.Materialize(
            resolution,
            new Panopticon.Content.Importing.Units.Materialization.UnitMaterializationContext(
                new Panopticon.Content.Importing.Units.Materialization.UnitConversionPolicy(SageGame.CncGenerals, gameUnitsPerSourceDistance: 12)));
        var installer = new Panopticon.Content.Importing.Units.Materialization.ImportedVisualInstaller(
            new Panopticon.Content.Importing.Nif.NifModelImporter(),
            new Panopticon.Content.Importing.Nif.NifAnimationImporter(),
            new Panopticon.Content.Importing.AssetImportContext(files.Graphics, language: null));

        Assert.True(content.Succeeded, string.Join("\n", content.Diagnostics));
        Assert.Equal(5, content.VisualAssets.Count);
        Assert.Empty(installer.Verify(content.VisualAssets));
    }
}
