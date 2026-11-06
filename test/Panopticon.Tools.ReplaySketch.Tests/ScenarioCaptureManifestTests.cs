using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Panopticon.Tools.TestHarness;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class ScenarioCaptureManifestTests
{
    private const string Payload = """
        {
          "features": {
            "items": [
              {
                "kind": "passability", "sourceId": "s", "featureId": "f1", "partId": "p1",
                "geometry": {
                  "type": "polygon",
                  "outerRing": { "positions": [ {"x": 0, "y": 0}, {"x": 10, "y": 0}, {"x": 10, "y": 10}, {"x": 0, "y": 10}, {"x": 0, "y": 0} ] },
                  "innerRings": [ { "positions": [ {"x": 2, "y": 2}, {"x": 4, "y": 2}, {"x": 3, "y": 4} ] } ]
                }
              },
              {
                "kind": "buildability", "sourceId": "s", "featureId": "f1", "partId": "p1",
                "geometry": {
                  "type": "polygon",
                  "outerRing": { "positions": [ {"x": 0, "y": 0}, {"x": 10, "y": 0}, {"x": 10, "y": 10}, {"x": 0, "y": 10}, {"x": 0, "y": 0} ] },
                  "innerRings": [ { "positions": [ {"x": 2, "y": 2}, {"x": 4, "y": 2}, {"x": 3, "y": 4} ] } ]
                }
              },
              {
                "kind": "annotation", "sourceId": "s", "featureId": "note", "partId": "n",
                "geometry": {
                  "type": "polygon",
                  "outerRing": { "positions": [ {"x": 50, "y": 50}, {"x": 60, "y": 50}, {"x": 60, "y": 60} ] },
                  "innerRings": []
                }
              },
              {
                "kind": "buildability", "sourceId": "s", "featureId": "f2", "partId": "p1",
                "geometry": {
                  "type": "polygon",
                  "outerRing": { "positions": [ {"x": 20, "y": 0}, {"x": 30, "y": 0}, {"x": 30, "y": 5} ] },
                  "innerRings": []
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public void CameraBlock_SerializesRowMajorWithExpectedShape()
    {
        var view = new Matrix4x4(
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16);
        var frame = new FrameRecord(
            Index: 3,
            AbsoluteLogicFrame: 42,
            File: "frame_0003.png",
            Actors: [],
            Projectiles: [],
            Camera: new CameraFrameRecord(
                ScenarioCaptureRunner.ToRowMajor(view),
                ScenarioCaptureRunner.ToRowMajor(Matrix4x4.Identity),
                new ViewportRecord(0, 0, 1024, 768, 0, 1)));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(frame, ScenarioCaptureRunner.ManifestJsonOptions));
        var camera = document.RootElement.GetProperty("Camera");

        Assert.Equal(new[] { "View", "Projection", "Viewport" }, camera.EnumerateObject().Select(p => p.Name));
        Assert.Equal(Enumerable.Range(1, 16).Select(i => (float)i), ReadFloats(camera.GetProperty("View")));
        Assert.Equal(
            new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 },
            ReadFloats(camera.GetProperty("Projection")));

        var viewport = camera.GetProperty("Viewport");
        Assert.Equal(
            new[] { "X", "Y", "Width", "Height", "MinDepth", "MaxDepth" },
            viewport.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1024f, viewport.GetProperty("Width").GetSingle());
        Assert.Equal(1f, viewport.GetProperty("MaxDepth").GetSingle());
    }

    [Fact]
    public void FrameRecord_WithoutCamera_OmitsCameraField()
    {
        var frame = new FrameRecord(0, 0, "frame_0000.png", [], []);

        var json = JsonSerializer.Serialize(frame, ScenarioCaptureRunner.ManifestJsonOptions);

        Assert.DoesNotContain("Camera", json);
    }

    [Fact]
    public void GroundVertexSidecar_DedupesByIdentityLiftsHolesAndExcludesAnnotations()
    {
        var root = CreateTempRoot();
        try
        {
            var payloadDirectory = Path.Combine(root, "payload");
            var outputDirectory = Path.Combine(root, "out");
            Directory.CreateDirectory(payloadDirectory);
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(payloadDirectory, "payload.json"), Payload);

            ScenarioCaptureRunner.WriteGroundVertexSidecar(
                outputDirectory,
                "sedan-footprints",
                payloadDirectory,
                p => p.X + 2f * p.Y);

            var payloadPath = Path.GetFullPath(Path.Combine(payloadDirectory, "payload.json"));
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "ground-vertices.json")));
            var sidecar = document.RootElement;

            Assert.Equal("sedan-footprints", sidecar.GetProperty("scenario").GetString());
            Assert.Equal("Terrain.GetGroundHeight per vertex, runtime map", sidecar.GetProperty("method").GetString());
            var manifest = sidecar.GetProperty("payloadManifest");
            Assert.Equal(payloadPath, manifest.GetProperty("path").GetString());
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(payloadPath))),
                manifest.GetProperty("sha256").GetString());

            var polygons = sidecar.GetProperty("polygons").EnumerateArray().ToList();
            Assert.Equal(2, polygons.Count);

            Assert.Equal("s|f1|p1", polygons[0].GetProperty("key").GetString());
            Assert.Equal(
                new[] { new[] { 0d, 0, 0 }, new[] { 10d, 0, 10 }, new[] { 10d, 10, 30 }, new[] { 0d, 10, 20 } },
                ReadPoints(polygons[0].GetProperty("outer")));
            Assert.Equal(
                new[] { new[] { new[] { 2d, 2, 6 }, new[] { 4d, 2, 8 }, new[] { 3d, 4, 11 } } },
                ReadPolygonHoles(polygons[0].GetProperty("holes")));

            Assert.Equal("s|f2|p1", polygons[1].GetProperty("key").GetString());
            Assert.Empty(polygons[1].GetProperty("holes").EnumerateArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GroundVertexSidecar_MissingPayload_FailsClosedWithoutWriting()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDirectory = Path.Combine(root, "out");
            Directory.CreateDirectory(outputDirectory);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ScenarioCaptureRunner.WriteGroundVertexSidecar(
                    outputDirectory,
                    "sedan-footprints",
                    Path.Combine(root, "missing"),
                    p => 0f));

            Assert.Contains("readable payload", ex.Message);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "ground-vertices.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GroundVertexSidecar_VertexOutsideMap_FailsClosed()
    {
        var root = CreateTempRoot();
        try
        {
            var payloadDirectory = Path.Combine(root, "payload");
            var outputDirectory = Path.Combine(root, "out");
            Directory.CreateDirectory(payloadDirectory);
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(payloadDirectory, "payload.json"), Payload);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ScenarioCaptureRunner.WriteGroundVertexSidecar(
                    outputDirectory,
                    "sedan-footprints",
                    payloadDirectory,
                    _ => null));

            Assert.Contains("outside the runtime map", ex.Message);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "ground-vertices.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"panopticon_sidecar_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static List<float> ReadFloats(JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetSingle()).ToList();

    private static double[][] ReadPoints(JsonElement ring) =>
        ring.EnumerateArray()
            .Select(point => point.EnumerateArray().Select(c => c.GetDouble()).ToArray())
            .ToArray();

    private static double[][][] ReadPolygonHoles(JsonElement holes) =>
        holes.EnumerateArray().Select(ReadPoints).ToArray();
}
