using System.Numerics;
using Panopticon.Content.Importing.Gltf;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class GltfMetricIdempotenceTests
{
    [Fact]
    public void DefaultUnitsPerMeter_IsMetricIdentity()
    {
        Assert.Equal(1f, GltfImportOptions.Default.UnitsPerMeter);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.3048f)]
    [InlineData(3.28084f)]
    public void MetricSource_NodeOffsetKeepsItsMeterValue(float meters)
    {
        var bone = Assert.Single(PlanFor(meters, GltfImportOptions.Default).Skeleton.Bones);

        Assert.Equal(meters, bone.BindTranslation.X, 6);
        Assert.Equal(0f, bone.BindTranslation.Y, 6);
        Assert.Equal(0f, bone.BindTranslation.Z, 6);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(3.28084f)]
    public void ExplicitUnitsPerMeterOne_MatchesDefaultPlan(float meters)
    {
        var defaultBone = Assert.Single(PlanFor(meters, GltfImportOptions.Default).Skeleton.Bones);
        var explicitBone = Assert.Single(PlanFor(meters, new GltfImportOptions(unitsPerMeter: 1f)).Skeleton.Bones);

        Assert.Equal(defaultBone.BindTranslation, explicitBone.BindTranslation);
        Assert.Equal(defaultBone.BindRotation, explicitBone.BindRotation);
    }

    private static GltfScenePlan PlanFor(float meters, GltfImportOptions options)
    {
        var model = SharpGLTF.Schema2.ModelRoot.CreateModel();
        model.UseScene(0);
        var node = model.LogicalScenes[0].CreateNode("metric");
        node.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: null,
            rotation: null,
            translation: new Vector3(0, 0, meters));

        return GltfSceneConverter.CreatePlan("model.glb", model, options, hasDeclaredDefaultScene: true);
    }
}
