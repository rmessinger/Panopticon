#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Preview;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// Render attributes of the generated-bridge level deck: every vertex carries a unit normal, copied faces
/// keep their source attributes, cut vertices interpolate them inside their source range, caps face along the
/// route with a planar mapping, and a rigid rotation of the route rotates the normals.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedBridgeLayoutAttributeTests
{
    private const double DatumZ = 1101.25d;

    private const string LeftName = "BRIDGE_LEFT";

    private const string SpanName = "BRIDGE_SPAN";

    private const string RightName = "BRIDGE_RIGHT";

    private const float NormalTolerance = 1e-3f;

    private const float RotationTexcoordTolerance = 1e-4f;

    private static readonly Vector2 RouteStart = new(550f, 300f);

    private static readonly Vector2 RouteEnd = new(550f, 1000f);

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_EveryVertexHasUnitNormalAndTextureCoordinates()
    {
        WithLoadedGame(game =>
        {
            var layout = BuildInstalled(game);

            Assert.Equal(3 * layout.Faces.Count, layout.VertexAttributes.Count);

            foreach (var attributes in layout.VertexAttributes)
            {
                Assert.True(IsFinite(attributes.Normal), "every normal must be finite.");
                Assert.InRange(attributes.Normal.Length(), 1f - NormalTolerance, 1f + NormalTolerance);
            }

            for (var face = 0; face < layout.Faces.Count; face++)
            {
                if (layout.FaceKinds[face] != GeneratedBridgeFaceKind.Original)
                {
                    continue;
                }

                for (var corner = 0; corner < 3; corner++)
                {
                    var uv = layout.VertexAttributes[(3 * face) + corner].Uv0;
                    Assert.NotNull(uv);
                    Assert.True(float.IsFinite(uv!.Value.X) && float.IsFinite(uv.Value.Y), "every original texture coordinate must be finite.");
                }
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_RoadwayFacesPointUp()
    {
        WithLoadedGame(game =>
        {
            var layout = BuildInstalled(game);
            var roadwayFaces = Enumerable.Range(0, layout.Faces.Count)
                .Where(face => layout.Faces[face].PieceName.Contains(LeftName, StringComparison.OrdinalIgnoreCase)
                    && layout.Faces[face].SourceFaceIndex is >= BridgeSurface.RoadwayFirstFaceIndex and <= BridgeSurface.RoadwayLastFaceIndex)
                .ToList();

            Assert.NotEmpty(roadwayFaces);

            foreach (var face in roadwayFaces)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var normal = layout.VertexAttributes[(3 * face) + corner].Normal;
                    Assert.InRange(normal.X, -NormalTolerance, NormalTolerance);
                    Assert.InRange(normal.Y, -NormalTolerance, NormalTolerance);
                    Assert.InRange(normal.Z, 1f - NormalTolerance, 1f + NormalTolerance);
                }
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_CutVerticesStayInsideTheirSourceTextureRange()
    {
        WithLoadedGame(game =>
        {
            var layout = BuildInstalled(game);

            for (var face = 0; face < layout.Faces.Count; face++)
            {
                if (layout.FaceKinds[face] != GeneratedBridgeFaceKind.Clipped)
                {
                    continue;
                }

                var clipped = layout.Faces[face];
                var source = Enumerable.Range(0, layout.Faces.Count)
                    .First(candidate => layout.FaceKinds[candidate] == GeneratedBridgeFaceKind.Original
                        && layout.Faces[candidate].PieceName == clipped.PieceName
                        && layout.Faces[candidate].SourceFaceIndex == clipped.SourceFaceIndex);
                var sourceUvs = Enumerable.Range(0, 3).Select(corner => layout.VertexAttributes[(3 * source) + corner].Uv0!.Value).ToList();
                var minX = sourceUvs.Min(uv => uv.X);
                var maxX = sourceUvs.Max(uv => uv.X);
                var minY = sourceUvs.Min(uv => uv.Y);
                var maxY = sourceUvs.Max(uv => uv.Y);

                for (var corner = 0; corner < 3; corner++)
                {
                    var uv = layout.VertexAttributes[(3 * face) + corner].Uv0!.Value;
                    Assert.InRange(uv.X, minX - 1e-5f, maxX + 1e-5f);
                    Assert.InRange(uv.Y, minY - 1e-5f, maxY + 1e-5f);
                }
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_CapsFaceAlongTheRoute()
    {
        WithLoadedGame(game =>
        {
            var layout = BuildInstalled(game);
            var capFaces = Enumerable.Range(0, layout.Faces.Count).Where(face => layout.FaceKinds[face] == GeneratedBridgeFaceKind.Cap).ToList();

            Assert.Equal(27, capFaces.Count);

            foreach (var face in capFaces)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var attributes = layout.VertexAttributes[(3 * face) + corner];
                    var vertex = face * 3 + corner;
                    var position = CornerPosition(layout, face, corner);
                    var lateral = layout.Route.Project(new Vector2(position.X, position.Y)).N;

                    Assert.InRange(attributes.Normal.X, -NormalTolerance, NormalTolerance);
                    Assert.InRange(attributes.Normal.Y, 1f - NormalTolerance, 1f + NormalTolerance);
                    Assert.InRange(attributes.Normal.Z, -NormalTolerance, NormalTolerance);
                    Assert.NotNull(attributes.Uv0);
                    Assert.InRange(attributes.Uv0!.Value.X, (float)(lateral / 10d) - NormalTolerance, (float)(lateral / 10d) + NormalTolerance);
                    Assert.InRange(attributes.Uv0.Value.Y, (position.Z / 10f) - NormalTolerance, (position.Z / 10f) + NormalTolerance);
                    Assert.Null(attributes.Uv1);
                    Assert.True(vertex >= 0);
                }
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_RotatedRoute_RotatesTheNormals()
    {
        WithLoadedGame(game =>
        {
            var bridge = game.Scene3D.Bridges[0];
            var input = bridge.CreateLayoutInput(RouteStart, RouteEnd, DatumZ);
            Assert.NotNull(input);

            var layoutA = GeneratedBridgeLayoutBuilder.TryBuild(input!, out var reasonA);
            Assert.Null(reasonA);
            Assert.NotNull(layoutA);

            var centre = new Vector3(550f, 650f, 0f);
            var rotation =
                Matrix4x4.CreateTranslation(-centre) *
                Matrix4x4.CreateRotationZ(MathF.PI / 6f) *
                Matrix4x4.CreateTranslation(centre);

            var rotatedInput = input! with
            {
                Route = new BridgeRoute(ToXy(Vector3.Transform(new Vector3(RouteStart, 0f), rotation)), ToXy(Vector3.Transform(new Vector3(RouteEnd, 0f), rotation))),
                AuthoredWorldMatrix = input.AuthoredWorldMatrix * rotation,
                RawFaces = input.RawFaces.Select(face => face with
                {
                    A = Vector3.Transform(face.A, rotation),
                    B = Vector3.Transform(face.B, rotation),
                    C = Vector3.Transform(face.C, rotation),
                }).ToList(),
                PieceMatrices = input.PieceMatrices.Select(matrix => matrix * rotation).ToList(),
            };

            var layoutB = GeneratedBridgeLayoutBuilder.TryBuild(rotatedInput, out var reasonB);
            Assert.Null(reasonB);
            Assert.NotNull(layoutB);

            var a = layoutA!;
            var b = layoutB!;
            Assert.Equal(a.VertexAttributes.Count, b.VertexAttributes.Count);

            for (var index = 0; index < a.VertexAttributes.Count; index++)
            {
                var expectedNormal = Vector3.Normalize(Vector3.TransformNormal(a.VertexAttributes[index].Normal, rotation));
                Assert.True(Vector3.Distance(expectedNormal, b.VertexAttributes[index].Normal) <= NormalTolerance, $"normal {index} did not rotate with the route.");
                AssertTexcoordEqual(a.VertexAttributes[index].Uv0, b.VertexAttributes[index].Uv0, index);
                AssertTexcoordEqual(a.VertexAttributes[index].Uv1, b.VertexAttributes[index].Uv1, index);
            }
        });
    }

    /// <summary>
    /// Synthetic source triangle with distinct attributes per corner, cut by the plane through its two
    /// edges: a kept corner keeps its exact attributes, and a crossing interpolates them at the parameter
    /// recomputed from the Y coordinates.
    /// </summary>
    [Fact]
    public void ClipWithSources_CrossingInterpolatesAttributes_AndKeptCornerKeepsSource()
    {
        var normals = new[] { new Vector3(0f, 0f, 1f), Vector3.Normalize(new Vector3(0.6f, 0f, 0.8f)), Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f)) };
        var positions = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 10f, 0f), new Vector3(10f, 0f, 0f) };
        var uv0 = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) };
        var uv1 = new[] { new Vector2(0.5f, 0.5f), new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.25f) };

        var mesh = new ImportedMesh(
            Enumerable.Range(0, 3).Select(index => new ImportedVertex(positions[index], normals[index])).ToList(),
            [0, 1, 2],
            default,
            new BoundingSphere(Vector3.Zero, 0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: [new ImportedMaterial("FixedFunction", ImportedCullMode.None, true, ImportedDepthComparison.Less, false, ImportedBlendFactor.One, ImportedBlendFactor.Zero, ImportedBlendFactor.Zero, null, Array.Empty<ImportedMaterialProperty>())],
            meshParts: [new ImportedMeshPart(uv0, uv1, 0, 3, false, 0)]);

        var corners = Enumerable.Range(0, 3).Select(index => new GeneratedBridgeVertexAttributes(
            Vector3.Normalize(mesh.Vertices[index].Normal),
            mesh.MeshParts[0].TextureCoordinates0![index],
            mesh.MeshParts[0].TextureCoordinates1![index])).ToList();

        const double yClip = 5d;
        var polygon = positions.Select(position => (X: (double)position.X, Y: (double)position.Y, Z: (double)position.Z)).ToList();
        var crossings = new List<(double X, double Z)>();
        var sourced = GeneratedBridgeTrimGeometry.ClipBelowPlaneWithSources(polygon, yClip, crossings);
        var plain = GeneratedBridgeTrimGeometry.ClipBelowPlane(polygon, yClip, new List<(double X, double Z)>());

        Assert.Equal(plain, sourced.Select(vertex => vertex.Position).ToList());

        foreach (var vertex in sourced)
        {
            if (vertex.From == vertex.To)
            {
                var kept = corners[vertex.From];
                Assert.Equal(0d, vertex.T);
                Assert.True(Vector3.Distance(kept.Normal, corners[vertex.From].Normal) == 0f);
                continue;
            }

            var p = polygon[vertex.From];
            var q = polygon[vertex.To];
            var tRecomputed = (yClip - p.Y) / (q.Y - p.Y);
            Assert.InRange(vertex.T, tRecomputed - 1e-6, tRecomputed + 1e-6);

            var a = corners[vertex.From];
            var b = corners[vertex.To];
            var expectedNormal = Vector3.Normalize((a.Normal * (1f - (float)tRecomputed)) + (b.Normal * (float)tRecomputed));
            var expectedUv0 = a.Uv0!.Value + ((b.Uv0!.Value - a.Uv0.Value) * (float)tRecomputed);
            var expectedUv1 = a.Uv1!.Value + ((b.Uv1!.Value - a.Uv1.Value) * (float)tRecomputed);
            var actual = GeneratedBridgeVertexAttributes.Lerp(a, b, vertex.T);

            Assert.True(Vector3.Distance(expectedNormal, actual.Normal) <= 1e-6f, "crossing normal must interpolate and renormalise.");
            Assert.True(Vector2.Distance(expectedUv0, actual.Uv0!.Value) <= 1e-6f, "crossing Uv0 must interpolate linearly.");
            Assert.True(Vector2.Distance(expectedUv1, actual.Uv1!.Value) <= 1e-6f, "crossing Uv1 must interpolate linearly.");
        }
    }

    private static GeneratedBridgeLayout BuildInstalled(Game game)
    {
        var layout = game.Scene3D.Bridges[0].TryBuildGeneratedLayout(RouteStart, RouteEnd, DatumZ, out var reason);
        Assert.Null(reason);
        Assert.NotNull(layout);
        return layout!;
    }

    private static Vector3 CornerPosition(GeneratedBridgeLayout layout, int face, int corner)
    {
        var raw = layout.Faces[face];
        return corner switch
        {
            0 => raw.A,
            1 => raw.B,
            _ => raw.C,
        };
    }

    private static void AssertTexcoordEqual(Vector2? expected, Vector2? actual, int index)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);

        if (expected.HasValue && actual.HasValue)
        {
            Assert.True(Vector2.Distance(expected.Value, actual.Value) <= RotationTexcoordTolerance, $"texture coordinate {index} differs after rotation.");
        }
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector2 ToXy(Vector3 value) => new(value.X, value.Y);

    private static void WithLoadedGame(Action<Game> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-level-layout-attributes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var package = Path.Combine(scratchRoot, "package");

            Convert(payloadDir, package, withRules: true);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            game.LoadMap(package, [], GameType.SinglePlayer);
            Assert.NotEmpty(game.Scene3D.Bridges);
            body(game);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Fixture helpers (copied from GeneratedBridgeLayoutTests).
    // ------------------------------------------------------------------

    private static string StagePayload(string payloadDir)
    {
        var diagnostics = new GeoDiagnosticBag();
        var writer = GeneratedMapPayloadWriter.Create(payloadDir, [], diagnostics);
        Assert.NotNull(writer);
        Assert.False(diagnostics.HasErrors);

        var palette = new MaterialPalette(["grass", "dirt"]);
        var descriptors = new List<PayloadChunkDescriptor>();

        for (var cy = 0; cy < 2; cy++)
        {
            for (var cx = 0; cx < 2; cx++)
            {
                var heights = new float[ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis];

                for (var sy = 0; sy < ChunkLayout.StoredPointsPerChunkAxis; sy++)
                {
                    for (var sx = 0; sx < ChunkLayout.StoredPointsPerChunkAxis; sx++)
                    {
                        heights[(sy * ChunkLayout.StoredPointsPerChunkAxis) + sx] =
                            PayloadHeight((cx * ChunkLayout.IntervalsPerChunk) + sx - 1, (cy * ChunkLayout.IntervalsPerChunk) + sy - 1);
                    }
                }

                var terrain = writer.WriteTerrain(new TerrainChunk(cx, cy, heights));
                var materials = writer.WriteMaterial(new MaterialChunk(cx, cy, palette, new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis]));
                var masks = writer.WriteMask(new TerrainMaskChunk(cx, cy, new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis]));
                Assert.NotNull(terrain);
                Assert.NotNull(materials);
                Assert.NotNull(masks);

                descriptors.Add(new PayloadChunkDescriptor(cx, cy, terrain!, materials!, masks!, null));
            }
        }

        // One straight two-point bridge line crossing the gorge, in payload game units.
        var bridgeEndpoints = new (double X, double Y)[] { (550d, 300d), (550d, 1000d) };
        var features = new List<GeneratedFeature>
        {
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(bridgeEndpoints[0].X, bridgeEndpoints[0].Y)), new GeoPosition(new Vector2d(bridgeEndpoints[1].X, bridgeEndpoints[1].Y))]),
                "vectors",
                "bridge-a",
                "vectors/bridge-a#0",
                -1),
        };

        var transform = new GeospatialMapTransform(
            [CoordinateSystemId.Wgs84],
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            new Vector2d(500000d, 6000000d),
            metersPerGameUnit: 1d,
            verticalDatum: "EGM96",
            verticalOriginMeters: 0d,
            verticalExaggeration: 1d);
        var grid = new PayloadGridDefinition(
            2,
            2,
            transform,
            new GeoPolygon(new GeoRing(
            [
                new GeoPosition(new Vector2d(500000d, 6000000d)),
                new GeoPosition(new Vector2d(512560d, 6000000d)),
                new GeoPosition(new Vector2d(512560d, 6012560d)),
                new GeoPosition(new Vector2d(500000d, 6012560d)),
                new GeoPosition(new Vector2d(500000d, 6000000d)),
            ])),
            null,
            new SlopeThresholds(45d, 15d));
        var metadata = new GeneratedMapPayloadMetadata(grid, palette, features, new string('7', 64), [], descriptors);
        var payloadSha256Hex = writer.WriteMetadata(metadata);
        Assert.NotNull(payloadSha256Hex);

        var report = new GeneratedMapPayloadReport(
            [new PayloadDiagnostic("GEO-VAL-001", GeoStage.Validate, GeoSeverity.Info, "Control points matched.")],
            new PayloadTerrainGenerationSummary(SamplingSummary(), 0, 0, 0),
            new PayloadMaskGenerationSummary(45d, 15d, 1d, 4, 0, 0, 0, 0, 0, 0, 0, 0),
            new PayloadFeatureGenerationSummary(
                features.Count,
                [.. Enum.GetValues<GeneratedFeatureKind>().Select(kind => new PayloadFeatureKindCount(kind, features.Count(feature => feature.Kind == kind)))]),
            [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Inventory complete.")],
            payloadSha256Hex);
        Assert.True(writer.WriteReport(report));
        Assert.True(writer.TryCommit());

        return payloadDir;
    }

    private static float PayloadHeight(long latticeX, long latticeY)
    {
        // Plane terrain with a north-south gorge strip the bridge crosses.
        var gorge = Math.Abs(latticeY - 64) < 4 ? -80f : 0f;

        return 1000f + (1.25f * latticeX) + (0.5f * latticeY) + gorge;
    }

    private static PayloadSamplingSummary SamplingSummary()
    {
        return new PayloadSamplingSummary(
            0,
            0,
            0,
            0,
            [.. Enum.GetValues<SampleKernel>().Select(kernel => new PayloadKernelSampleCount(kernel, 0))],
            new PayloadObservedRanges(
                gsdRatioX: new PayloadObservedRange(0.98d, 1.02d),
                gsdRatioY: new PayloadObservedRange(0.97d, 1.03d)));
    }

    private static void Convert(string payloadDir, string packageDir, bool withRules)
    {
        var diagnostics = new GeoDiagnosticBag();
        using var reader = GeneratedMapPayloadReader.Open(payloadDir, diagnostics);
        Assert.NotNull(reader);
        Assert.False(diagnostics.HasErrors);

        var textureMap = PreviewTextureMap.FromJson(
            """{"grass":"GrassType1","dirt":"DirtMediumType1"}""",
            new FixedTerrainTextureCatalog(["GrassType1", "DirtMediumType1"]),
            new GeoDiagnosticBag());
        Assert.NotNull(textureMap);

        var result = GeneratedMapPackageConverter.Convert(
            reader!,
            "r3-g8-level-layout-attributes",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors")] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
