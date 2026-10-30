using System;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Granny;
using Panopticon.FileFormats.Granny;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Guards the UV path end to end: every imported texture coordinate must equal the
/// source GR2 vertex's TextureCoordinates0 with v flipped (Granny stores DCC-convention
/// UVs, origin bottom-left, while texture rows run top-first) and no reordering. This
/// is the regression test behind the phase-4 texture investigation.
/// </summary>
public sealed class GrannyUvParityTests
{
    [Fact]
    [Trait("Category", "Interactive")]
    public void MusketeerUvsMatchSourceCoordinates()
    {
        AssertUvsMatchSource(@"Art\units\infantry_ranged\musketeer\musketeer_4age.gr2");
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void HorseUvsMatchSourceCoordinates()
    {
        AssertUvsMatchSource(@"Art\units\cavalry\light_cavalry\light_cavalry_age4_horse.gr2");
    }

    private static void AssertUvsMatchSource(string modelPath)
    {
        var importer = new GrannyModelImporter();
        using var fileSystem = Aoe3Install.CreateFileSystem();
        var context = new AssetImportContext(fileSystem, language: null);

        var model = importer.ImportModel(modelPath, context);
        Assert.NotNull(model);

        var entry = fileSystem.GetFile(modelPath)
            ?? throw new InvalidOperationException($"Missing '{modelPath}' in the AoE3 archives.");
        using var stream = entry.Open();
        using var buffered = new System.IO.MemoryStream();
        stream.CopyTo(buffered);
        buffered.Position = 0;
        var root = GrannyFile.Read(buffered, modelPath).Root;

        var meshObjects = root.GetObjects("Meshes");
        Assert.NotEmpty(meshObjects);

        var importedIndex = 0;
        foreach (var meshObject in meshObjects)
        {
            var vertexData = meshObject.GetObject("PrimaryVertexData");
            Assert.NotNull(vertexData);
            var sourceVertices = vertexData.GetObjects("Vertices");
            Assert.NotEmpty(sourceVertices);

            var subObject = model!.SubObjects[importedIndex];
            var mesh = Assert.IsType<ImportedMesh>(subObject.RenderObject);

            // Both proof models stay under the 16-bit vertex limit, so the converter
            // keeps source vertex order; UV index i must equal source vertex i.
            Assert.Equal(sourceVertices.Count, mesh.Vertices.Count);

            var hasAnyUv = false;
            for (var i = 0; i < sourceVertices.Count; i++)
            {
                var source = sourceVertices[i].GetArray("TextureCoordinates0");
                if (source is not float[] coordinates || coordinates.Length < 2)
                {
                    continue;
                }

                var expected = new Vector2(coordinates[0], 1f - coordinates[1]);
                hasAnyUv = true;

                foreach (var part in mesh.MeshParts)
                {
                    Assert.NotNull(part.TextureCoordinates0);
                    var actual = part.TextureCoordinates0![i];
                    Assert.Equal(expected.X, actual.X, 5);
                    Assert.Equal(expected.Y, actual.Y, 5);
                }
            }

            Assert.True(hasAnyUv, $"Mesh {importedIndex} of '{modelPath}' has no source UVs to compare.");
            importedIndex++;
        }
    }
}
