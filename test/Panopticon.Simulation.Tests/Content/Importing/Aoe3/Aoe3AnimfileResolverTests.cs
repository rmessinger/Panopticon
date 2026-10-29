using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Granny;
using Panopticon.Tests.Content;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public sealed class Aoe3AnimfileResolverTests
{
    private readonly ITestOutputHelper _output;

    public Aoe3AnimfileResolverTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static AssetImportContext CreateFixture()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(@"Data\protoy.xml.XMB", SyntheticXmbBuilder.Build(BuildProtoy()));
        fileSystem.AddFile(@"Art\units\test.xml.XMB", SyntheticXmbBuilder.Build(BuildTestAnimfile()));
        fileSystem.AddFile(@"Art\units\test_hats.xml.XMB", SyntheticXmbBuilder.Build(BuildHatsAnimfile()));
        fileSystem.AddFile(@"Art\units\ship.xml.XMB", SyntheticXmbBuilder.Build(BuildShipAnimfile()));
        return new AssetImportContext(fileSystem, language: null);
    }

    private static XDocument BuildProtoy()
    {
        return new XDocument(
            new XElement("proto",
                BuildUnit("TestUnit", @"units\test.xml"),
                BuildUnit("TestShip", @"units\ship.xml")));
    }

    private static XElement BuildUnit(string name, string animfile)
    {
        return new XElement("unit", new XAttribute("name", name),
            new XElement("animfile", animfile));
    }

    private static XDocument BuildTestAnimfile()
    {
        return new XDocument(
            new XElement("animfile",
                new XElement("definebone", "bone_hat"),
                new XElement("attachment", "hats",
                    new XElement("include", @"units\test_hats.xml")),
                new XElement("component", "ModelComp",
                    BuildTechLogic(
                        BuildModelVariant("none", @"units\models\model_a", includeHats: true),
                        BuildModelVariant("techA", @"units\models\model_b", includeHats: true),
                        BuildModelVariant("techB", @"units\models\model_c", includeHats: false))),
                BuildAnim("Walk",
                    new XElement("assetreference", new XAttribute("type", "GrannyAnim"),
                        new XElement("weight", "10"),
                        new XElement("file", @"anims\walk_slow")),
                    new XElement("assetreference", new XAttribute("type", "GrannyAnim"),
                        new XElement("weight", "40"),
                        new XElement("file", @"anims\walk_fast"))),
                BuildAnim("Jog",
                    new XElement("assetreference", new XAttribute("type", "GrannyAnim"),
                        new XElement("weight", "5"),
                        new XElement("file", @"anims\jog_a")),
                    new XElement("assetreference", new XAttribute("type", "GrannyAnim"),
                        new XElement("weight", "5"),
                        new XElement("file", @"anims\jog_b")))));
    }

    private static XElement BuildTechLogic(params XElement[] variants)
    {
        var logic = new XElement("logic", new XAttribute("type", "Tech"));
        foreach (var variant in variants)
        {
            logic.Add(variant);
        }

        return logic;
    }

    private static XElement BuildModelVariant(string name, string file, bool includeHats)
    {
        var variant = new XElement(name,
            new XElement("assetreference", new XAttribute("type", "GrannyModel"),
                new XElement("file", file)));
        if (includeHats)
        {
            variant.Add(new XElement("attach",
                new XAttribute("a", "hats"),
                new XAttribute("frombone", "bone_hat"),
                new XAttribute("tobone", "HEAD")));
        }

        return variant;
    }

    private static XElement BuildAnim(string name, params XElement[] assetReferences)
    {
        var anim = new XElement("anim", name);
        foreach (var assetReference in assetReferences)
        {
            anim.Add(assetReference);
        }

        return anim;
    }

    private static XDocument BuildHatsAnimfile()
    {
        return new XDocument(
            new XElement("animfile",
                new XElement("component", "hats",
                    BuildTechLogic(
                        BuildModelVariant("none", @"units\models\hat_basic", includeHats: false),
                        BuildModelVariant("techA", @"units\models\hat_fancy", includeHats: false)))));
    }

    private static XDocument BuildShipAnimfile()
    {
        // Mirrors the frigate: a Destruction logic nested inside the Tech none branch with
        // phase children p1 (damaged) and p99 (undamaged) and no none child of its own.
        return new XDocument(
            new XElement("animfile",
                new XElement("component", "TestShip",
                    BuildTechLogic(
                        new XElement("none",
                            new XElement("logic", new XAttribute("type", "Destruction"),
                                new XElement("p1",
                                    new XElement("assetreference", new XAttribute("type", "GrannyModel"),
                                        new XElement("file", @"units\models\ship_damaged"))),
                                new XElement("p99",
                                    new XElement("assetreference", new XAttribute("type", "GrannyModel"),
                                        new XElement("file", @"units\models\ship_ok")))))))));
    }

    [Fact]
    public void DefaultTechsResolveNoneBranch()
    {
        var resolver = new Aoe3AnimfileResolver(CreateFixture());

        var visual = resolver.Resolve("TestUnit");

        Assert.Equal(@"Art\units\models\model_a.gr2", visual.ModelPath);
        var hats = Assert.Single(visual.Attachments);
        Assert.Equal("hats", hats.Name);
        Assert.Equal("bone_hat", hats.FromBone);
        Assert.Equal("HEAD", hats.ToBone);
        Assert.Equal(@"Art\units\models\hat_basic.gr2", hats.ModelPath);
        Assert.Equal(@"Art\anims\walk_fast.gr2", visual.ClipsByAnimName["Walk"]);
        Assert.Equal(@"Art\anims\jog_a.gr2", visual.ClipsByAnimName["Jog"]);
        Assert.Equal(new[] { "bone_hat" }, visual.DefinedBones);
    }

    [Fact]
    public void ActiveTechSelectsVariant()
    {
        var resolver = new Aoe3AnimfileResolver(CreateFixture());

        var visual = resolver.Resolve("TestUnit", ["techA"]);

        Assert.Equal(@"Art\units\models\model_b.gr2", visual.ModelPath);
        Assert.Equal(@"Art\units\models\hat_fancy.gr2", visual.Attachments.Single(a => a.Name == "hats").ModelPath);
    }

    [Fact]
    public void AmbiguousTechTieThrows()
    {
        var resolver = new Aoe3AnimfileResolver(CreateFixture());

        var error = Assert.Throws<InvalidDataException>(
            () => resolver.Resolve("TestUnit", ["techA", "techB"]));

        Assert.Contains("TestUnit", error.Message);
        Assert.Contains("techA", error.Message);
        Assert.Contains("techB", error.Message);
    }

    [Fact]
    public void ResolutionIsDeterministic()
    {
        var resolver = new Aoe3AnimfileResolver(CreateFixture());

        var first = resolver.Resolve("TestUnit", ["techA"]);
        var second = resolver.Resolve("TestUnit", ["techA"]);

        Assert.Equal(first.ModelPath, second.ModelPath);
        Assert.Equal(first.ClipsByAnimName.Count, second.ClipsByAnimName.Count);
        Assert.All(first.ClipsByAnimName, pair => Assert.Equal(pair.Value, second.ClipsByAnimName[pair.Key]));
        Assert.All(first.Attachments, a => Assert.Equal(a.ModelPath, second.Attachments.Single(x => x.Name == a.Name).ModelPath));
    }

    [Fact]
    public void MissingUnitThrows()
    {
        var resolver = new Aoe3AnimfileResolver(CreateFixture());

        var error = Assert.Throws<InvalidDataException>(() => resolver.Resolve("Nobody"));

        Assert.Contains("Nobody", error.Message);
        Assert.Contains("unit", error.Message);
    }

    [Fact]
    public void MissingAnimfileThrows()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(@"Data\protoy.xml.XMB", SyntheticXmbBuilder.Build(BuildProtoy()));
        var resolver = new Aoe3AnimfileResolver(new AssetImportContext(fileSystem, language: null));

        var error = Assert.Throws<InvalidDataException>(() => resolver.Resolve("TestUnit"));

        Assert.Contains(@"Art\units\test.xml.XMB", error.Message);
        Assert.Contains(@"Art\units\test.xml", error.Message);
    }

    [Fact]
    public void DestructionLogicPicksUndamagedPhase()
    {
        var resolver = new Aoe3AnimfileResolver(CreateFixture());

        var visual = resolver.Resolve("TestShip");

        Assert.Equal(@"Art\units\models\ship_ok.gr2", visual.ModelPath);
    }

    [Fact]
    public void UnitNamedComponentIsFallbackWithoutModelComp()
    {
        // The ship animfile has no ModelComp; its component is named after the unit.
        var resolver = new Aoe3AnimfileResolver(CreateFixture());

        var visual = resolver.Resolve("TestShip");

        Assert.NotNull(visual.ModelPath);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void MusketeerAge4ResolvesGuardModel()
    {
        var resolver = new Aoe3AnimfileResolver(new AssetImportContext(Aoe3Install.CreateFileSystem(), language: null));

        var visual = resolver.Resolve("Musketeer", ["guardmusketeers"]);

        Assert.Equal(@"Art\units\infantry_ranged\musketeer\musketeer_4age.gr2", visual.ModelPath);
        var hats = visual.Attachments.Single(a => a.Name == "hats");
        Assert.Equal("bone_hat", hats.FromBone);
        Assert.Equal("HEAD", hats.ToBone);
        Assert.Equal(@"Art\units\infantry_ranged\musketeer\musketeer_4age_hat.gr2", hats.ModelPath);
        Assert.Equal(@"Art\animation_library\range\march_rifleman.gr2", visual.ClipsByAnimName["Volley_walk"]);
        Assert.Equal(@"Art\animation_library\range\volley\volley_standing_fire.gr2", visual.ClipsByAnimName["Volley_standing_attack"]);
        _output.WriteLine($"Musketeer age 4: {visual.ClipsByAnimName.Count} clips, {visual.Attachments.Count} attachments, {visual.DefinedBones.Count} defined bones.");
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void MusketeerDefaultResolvesAge2()
    {
        var resolver = new Aoe3AnimfileResolver(new AssetImportContext(Aoe3Install.CreateFileSystem(), language: null));

        var visual = resolver.Resolve("Musketeer");

        Assert.Equal(@"Art\units\infantry_ranged\musketeer\musketeer_2age.gr2", visual.ModelPath);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void HussarGuardResolvesAge4Horse()
    {
        var resolver = new Aoe3AnimfileResolver(new AssetImportContext(Aoe3Install.CreateFileSystem(), language: null));

        var visual = resolver.Resolve("Hussar", ["guardhussars"]);

        Assert.Equal(@"Art\units\cavalry\light_cavalry\light_cavalry_age4_horse.gr2", visual.ModelPath);
        var rider = visual.Attachments.Single(a => a.Name == "RiderAttachment");
        Assert.Equal("hotspot", rider.FromBone);
        Assert.Equal("ATTACHPOINT", rider.ToBone);
        Assert.NotNull(rider.ModelPath);
        _output.WriteLine($"Hussar rider model: {rider.ModelPath}");
        Assert.Equal(@"Art\animation_library\cavalry\horse_idle_A.gr2", visual.ClipsByAnimName["Idle"]);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void FrigateDefaultsToUndamagedModel()
    {
        var resolver = new Aoe3AnimfileResolver(new AssetImportContext(Aoe3Install.CreateFileSystem(), language: null));

        var visual = resolver.Resolve("Frigate");

        Assert.Equal(@"Art\units\naval\frigate\frigate.gr2", visual.ModelPath);
        Assert.Equal(@"Art\units\naval\frigate\frigate_idle.gr2", visual.ClipsByAnimName["Idle"]);
        _output.WriteLine($"Frigate: {visual.DefinedBones.Count} defined bones, {visual.Attachments.Count} attachments.");
    }
}
