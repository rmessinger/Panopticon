using System;
using System.Linq;
using System.Xml.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Granny;
using Panopticon.FileFormats.Bar;
using Panopticon.Tests.Content;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public sealed class Aoe3MaterialTests
{
    private readonly ITestOutputHelper _output;

    public Aoe3MaterialTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static XDocument BuildMaterialDocument(string submaterialName, string materialdef, params XElement[] parameters)
    {
        return new XDocument(
            new XElement("material",
                new XElement("submaterial", new XAttribute("name", submaterialName),
                    new XElement("materialdef", new XAttribute("name", materialdef)),
                    new XElement("parameters", parameters))));
    }

    private static XElement Texture(string name, string path)
    {
        return new XElement("texture", new XAttribute("name", name), new XAttribute("override", path));
    }

    private static XElement Float(string name, string value)
    {
        return new XElement("float", new XAttribute("name", name), new XAttribute("override", value));
    }

    private static (MemoryFileSystem FileSystem, Aoe3MaterialConverter Converter, GrannyImportDiagnostics Diagnostics) CreateFixture(
        string materialKey, XDocument? materialDocument, params string[] texturePaths)
    {
        var fileSystem = new MemoryFileSystem();
        if (materialDocument is not null)
        {
            fileSystem.AddFile(materialKey, SyntheticXmbBuilder.Build(materialDocument));
        }

        foreach (var path in texturePaths)
        {
            fileSystem.AddFile(path, new byte[] { 1 });
        }

        var diagnostics = new GrannyImportDiagnostics();
        var context = new AssetImportContext(fileSystem, language: null);
        return (fileSystem, new Aoe3MaterialConverter(context, diagnostics), diagnostics);
    }

    [Fact]
    public void MapsSubmaterialToPbrWithAllBindings()
    {
        var document = BuildMaterialDocument("mata", "default",
            Texture("BaseColor", @"units\t\mata_basecolor"),
            Texture("Normals", @"units\t\mata_normals"),
            Texture("Masks", @"units\t\mata_masks"),
            Texture("Details", @"units\t\mata_details"),
            Float("AO Intensity", "0.2"));
        var (_, converter, _) = CreateFixture(
            @"Art\units\t\m.material.xmb",
            document,
            @"Art\units\t\mata_basecolor.ddt",
            @"Art\units\t\mata_normals.ddt",
            @"Art\units\t\mata_masks.ddt");

        var material = converter.Convert("mata", @"Art\units\t\m.material.xmb", "mShape", 1);

        var payload = material.PbrMetallicRoughness!;
        Assert.Equal(
            "Art/units/t/mata_basecolor.ddt",
            payload.BaseColorTexture!.ImageSource.VirtualPath);
        Assert.Equal(
            "Art/units/t/mata_normals.ddt",
            payload.NormalTexture!.ImageSource.VirtualPath);
        Assert.Equal(
            "Art/units/t/mata_masks.ddt",
            payload.MetallicRoughnessTexture!.ImageSource.VirtualPath);
        Assert.Equal(
            "Art/units/t/mata_masks.ddt",
            payload.OcclusionTexture!.ImageSource.VirtualPath);
        Assert.Equal(0.2f, payload.OcclusionStrength);
        Assert.Equal(1.0f, payload.MetallicFactor);
        Assert.Equal(1.0f, payload.RoughnessFactor);

        Assert.Equal("mata", material.PropertyValue("GrannyMaterialName"));
        Assert.Equal("default", material.PropertyValue("Aoe3Materialdef"));
        Assert.Equal(@"units\t\mata_details", material.PropertyValue("Aoe3DetailsTexture"));
        Assert.Equal(0.2f, material.FloatPropertyValue("Aoe3Slider_AO Intensity"));
    }

    [Fact]
    public void MissingMaterialFileDegradesToPlaceholderWithDiagnostic()
    {
        var (_, converter, diagnostics) = CreateFixture(@"Art\units\t\m.material.xmb", null);

        var material = converter.Convert("mata", @"Art\units\t\m.material.xmb", "mShape", 1);

        Assert.Null(material.PbrMetallicRoughness!.BaseColorTexture);
        Assert.Null(material.PbrMetallicRoughness.MetallicRoughnessTexture);
        Assert.Equal("mata", material.PropertyValue("GrannyMaterialName"));
        var warning = Assert.Single(diagnostics.Warnings);
        Assert.Contains("mShape", warning);
        Assert.Contains("m.material.xmb", warning);
    }

    [Fact]
    public void UnknownSubmaterialNameDegradesToPlaceholderWithDiagnostic()
    {
        var document = BuildMaterialDocument("matz", "default");
        var (_, converter, diagnostics) = CreateFixture(@"Art\units\t\m.material.xmb", document);

        var material = converter.Convert("mata", @"Art\units\t\m.material.xmb", "mShape", 1);

        Assert.Null(material.PbrMetallicRoughness!.BaseColorTexture);
        var warning = Assert.Single(diagnostics.Warnings);
        Assert.Contains("submaterial 'mata'", warning);
    }

    [Fact]
    public void MissingTextureOmitsBindingWithDiagnostic()
    {
        var document = BuildMaterialDocument("mata", "default",
            Texture("BaseColor", @"units\t\mata_basecolor"),
            Texture("Normals", @"units\t\mata_normals"),
            Texture("Masks", @"units\t\mata_masks"));
        var (_, converter, diagnostics) = CreateFixture(
            @"Art\units\t\m.material.xmb",
            document,
            @"Art\units\t\mata_basecolor.ddt",
            @"Art\units\t\mata_masks.ddt");

        var material = converter.Convert("mata", @"Art\units\t\m.material.xmb", "mShape", 1);

        Assert.NotNull(material.PbrMetallicRoughness!.BaseColorTexture);
        Assert.Null(material.PbrMetallicRoughness.NormalTexture);
        Assert.NotNull(material.PbrMetallicRoughness.MetallicRoughnessTexture);
        var warning = Assert.Single(diagnostics.Warnings);
        Assert.Contains("normal", warning);
        Assert.Contains("mata_normals", warning);
    }

    [Fact]
    public void RigidFlagSurvivesMaterialMapping()
    {
        var document = BuildMaterialDocument("mata", "default",
            Texture("BaseColor", @"units\t\mata_basecolor"),
            Texture("Normals", @"units\t\mata_normals"),
            Texture("Masks", @"units\t\mata_masks"));
        var (_, converter, _) = CreateFixture(
            @"Art\units\t\m.material.xmb",
            document,
            @"Art\units\t\mata_basecolor.ddt",
            @"Art\units\t\mata_normals.ddt",
            @"Art\units\t\mata_masks.ddt");

        var material = converter.Convert("mata", @"Art\units\t\m.material.xmb", "mShape", 3);

        var flag = material.Properties.Single(p => p.Name == "RigidFirstBoneBinding");
        Assert.True(flag.BoolValue);
    }

    [Fact]
    public void RangeSliderOverridesApplyAsFactors()
    {
        var document = BuildMaterialDocument("mata", "default",
            Texture("BaseColor", @"units\t\mata_basecolor"),
            Texture("Normals", @"units\t\mata_normals"),
            Texture("Masks", @"units\t\mata_masks"),
            Float("Roughness Max", "0.5"),
            Float("Metallic Max", "0.25"));
        var (_, converter, _) = CreateFixture(
            @"Art\units\t\m.material.xmb",
            document,
            @"Art\units\t\mata_basecolor.ddt",
            @"Art\units\t\mata_normals.ddt",
            @"Art\units\t\mata_masks.ddt");

        var material = converter.Convert("mata", @"Art\units\t\m.material.xmb", "mShape", 1);

        Assert.Equal(0.5f, material.PbrMetallicRoughness!.RoughnessFactor);
        Assert.Equal(0.25f, material.PbrMetallicRoughness.MetallicFactor);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void MusketeerMaterialsBindFourDdtTextures()
    {
        var fileSystem = Aoe3Install.CreateFileSystem();
        var context = new AssetImportContext(fileSystem, language: null);
        var importer = new GrannyModelImporter();
        var model = importer.ImportModel(@"Art\units\infantry_ranged\musketeer\musketeer_4age.gr2", context);

        Assert.NotNull(model);
        Assert.Empty(importer.Diagnostics.Warnings);
        var mesh = Assert.IsType<ImportedMesh>(model!.SubObjects[0].RenderObject);
        var payload = mesh.Materials[0].PbrMetallicRoughness!;
        Assert.EndsWith(
            "musketeer_4age_matA_BaseColor.ddt",
            payload.BaseColorTexture!.ImageSource.VirtualPath);
        Assert.EndsWith(
            "musketeer_4age_matA_Normals.ddt",
            payload.NormalTexture!.ImageSource.VirtualPath);
        Assert.EndsWith(
            "musketeer_4age_matA_Masks.ddt",
            payload.MetallicRoughnessTexture!.ImageSource.VirtualPath);
        Assert.Equal(
            payload.MetallicRoughnessTexture!.ImageSource.VirtualPath,
            payload.OcclusionTexture!.ImageSource.VirtualPath);
        Assert.Equal("mata", mesh.Materials[0].PropertyValue("GrannyMaterialName"));
        Assert.Equal("default", mesh.Materials[0].PropertyValue("Aoe3Materialdef"));
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void HorseMaterialsApplyAoIntensitySlider()
    {
        var fileSystem = Aoe3Install.CreateFileSystem();
        var context = new AssetImportContext(fileSystem, language: null);
        var importer = new GrannyModelImporter();
        var model = importer.ImportModel(
            @"Art\units\cavalry\light_cavalry\light_cavalry_age4_horse.gr2", context);

        Assert.NotNull(model);
        Assert.Empty(importer.Diagnostics.Warnings);
        var materials = model!.SubObjects
            .Select(s => Assert.IsType<ImportedMesh>(s.RenderObject))
            .SelectMany(m => m.Materials)
            .ToList();
        Assert.Equal(2, materials.Count);
        var mata = materials[0].PbrMetallicRoughness!;
        var matb = materials[1].PbrMetallicRoughness!;
        Assert.Equal(0.2f, mata.OcclusionStrength);
        Assert.Equal(1.0f, matb.OcclusionStrength);
        Assert.Equal("default_cutout", materials[0].PropertyValue("Aoe3Materialdef"));
        Assert.Equal("default_cutout", materials[1].PropertyValue("Aoe3Materialdef"));
        Assert.EndsWith("light_cavalry_age4_horse_mata_masks.ddt", mata.MetallicRoughnessTexture!.ImageSource.VirtualPath);
        Assert.EndsWith("light_cavalry_age4_horse_matb_masks.ddt", matb.MetallicRoughnessTexture!.ImageSource.VirtualPath);
        _output.WriteLine($"horse materials: {materials.Count}, warnings: {importer.Diagnostics.Warnings.Count}");
    }
}

file static class MaterialTestExtensions
{
    public static string? PropertyValue(this ImportedMaterial material, string name)
    {
        return material.Properties.FirstOrDefault(p => p.Name == name)?.TextureName;
    }

    public static float FloatPropertyValue(this ImportedMaterial material, string name)
    {
        return material.Properties.FirstOrDefault(p => p.Name == name)?.FloatValue ?? 0f;
    }
}
