using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Content.Importing.Maps;
using Panopticon.Graphics.Rendering;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Maps;

public sealed class GeneratedBuildingPickerTests
{
    [Fact]
    public void PickInsideSquareFootprintReturnsBuilding()
    {
        var building = Building("A", Square(0, 0, 10, 10));

        Assert.Same(building, GeneratedBuildingPicker.Pick(Layer(building), 5, 5));
    }

    [Fact]
    public void PickInsideHoleReturnsNextBuildingWhenOverlapping()
    {
        var holed = Building("A", Polygon(Square(0, 0, 10, 10), Square(3, 3, 7, 7)));
        var solid = Building("B", Square(0, 0, 10, 10));

        Assert.Same(solid, GeneratedBuildingPicker.Pick(Layer(holed, solid), 5, 5));
    }

    [Fact]
    public void PickInsideHoleReturnsNullWhenNoOtherBuilding()
    {
        var holed = Building("A", Polygon(Square(0, 0, 10, 10), Square(3, 3, 7, 7)));

        Assert.Null(GeneratedBuildingPicker.Pick(Layer(holed), 5, 5));
    }

    [Fact]
    public void PickOutsideAllFootprintsReturnsNull()
    {
        var building = Building("A", Square(0, 0, 10, 10));

        Assert.Null(GeneratedBuildingPicker.Pick(Layer(building), 20, 5));
    }

    [Fact]
    public void PickOnOuterEdgeCountsAsInside()
    {
        var building = Building("A", Square(0, 0, 10, 10));

        Assert.Same(building, GeneratedBuildingPicker.Pick(Layer(building), 0, 5));
        Assert.Same(building, GeneratedBuildingPicker.Pick(Layer(building), 10, 10));
    }

    [Fact]
    public void PickMultiPartBuildingViaSecondPart()
    {
        var first = new BuildingPart("A.0", Square(0, 0, 4, 4), 0, 0);
        var second = new BuildingPart("A.1", new VectorMultiGeometry([Square(20, 20, 24, 24)]), 0, 0);
        var building = Building("A", [first, second]);

        Assert.Same(building, GeneratedBuildingPicker.Pick(Layer(building), 22, 22));
        Assert.Null(GeneratedBuildingPicker.Pick(Layer(building), 10, 10));
    }

    [Fact]
    public void PickOverlappingBuildingsReturnsFirstInSetOrder()
    {
        var first = Building("A", Square(0, 0, 10, 10));
        var second = Building("B", Square(5, 5, 15, 15));

        Assert.Same(first, GeneratedBuildingPicker.Pick(Layer(first, second), 7, 7));
        Assert.Same(second, GeneratedBuildingPicker.Pick(Layer(second, first), 7, 7));
    }

    [Fact]
    public void PickNonPolygonPartNeverContains()
    {
        var line = new VectorLineStringGeometry([new VectorPosition(0, 0, null), new VectorPosition(10, 10, null)]);
        var building = Building("A", line);

        Assert.Null(GeneratedBuildingPicker.Pick(Layer(building), 5, 5));
    }

    [Fact]
    public void PaletteVariantsAreDistinct()
    {
        var colours = new HashSet<Vector3>();
        for (var variant = 0; variant < 8; variant++)
        {
            colours.Add(GeneratedBuildingPalette.ColorFor(variant));
        }

        Assert.Equal(8, colours.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void PaletteOutOfRangeVariantThrows(int variant)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeneratedBuildingPalette.ColorFor(variant));
    }

    [Fact]
    public void SegmentBoundsCrossingFrustumIntersectWhileCentreIsOutside()
    {
        var frustum = new BoundingFrustum(Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 2f,
            1f,
            0.1f,
            1_000_000f));

        var crossing = new AxisAlignedBoundingBox(new Vector3(-1f, -1f, -10f), new Vector3(20f, 1f, -5f));
        Assert.False(frustum.Contains(crossing.GetCenter()));
        Assert.True(frustum.Intersects(crossing));

        var outside = new AxisAlignedBoundingBox(new Vector3(30f, -1f, -10f), new Vector3(40f, 1f, -5f));
        Assert.False(frustum.Intersects(outside));
    }

    private static VectorPolygonGeometry Square(double x0, double y0, double x1, double y1) =>
        new(new VectorRing(
            [
                new VectorPosition(x0, y0, null),
                new VectorPosition(x1, y0, null),
                new VectorPosition(x1, y1, null),
                new VectorPosition(x0, y1, null),
                new VectorPosition(x0, y0, null),
            ]),
            Array.Empty<VectorRing>());

    private static VectorPolygonGeometry Polygon(VectorPolygonGeometry outer, VectorPolygonGeometry hole) =>
        new(outer.OuterRing, [hole.OuterRing]);

    private static GeneratedBuilding Building(string id, VectorGeometry footprint) =>
        Building(id, [new BuildingPart(id + ".0", footprint, 0, 0)]);

    private static GeneratedBuilding Building(string id, IReadOnlyList<BuildingPart> parts) =>
        new(
            sourceId: id,
            featureId: "feature-" + id,
            attributes: new Dictionary<string, string>(),
            heightMeters: 10,
            heightSource: "SourceAttribute",
            heightGameUnits: 10,
            roofRule: "Flat",
            roofFallbackReason: null,
            materialVariant: 0,
            seatMethod: "Flat",
            supportMinZ: 0,
            supportMaxZ: 0,
            sinkGameUnits: 0,
            baseZ: 0,
            eaveZ: 10,
            parts: parts);

    private static BuildingLayerSet Layer(params GeneratedBuilding[] buildings) =>
        new(buildings, Array.Empty<BuildingMeshChunk>());
}
