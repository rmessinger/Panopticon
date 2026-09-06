using System.Numerics;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.Object.Weapon;

internal static class ProjectileVisualAssertions
{
    internal static void AssertLeadingEdgePivot(GameObject projectile)
    {
        Assert.NotNull(projectile.Client);
        Assert.True(
            projectile.Client!.TryGetModelSpaceBounds(out var bounds),
            $"{projectile.Definition.Name} has no visible model bounds");

        var center = bounds.GetCenter();
        var leadingPoint = new Vector3(bounds.Max.X, center.Y, center.Z);
        var trailingPoint = new Vector3(bounds.Min.X, center.Y, center.Z);
        var modelWorldTransform = projectile.ModelTransform.Matrix * projectile.Transform.Matrix;
        var worldLeadingPoint = Vector3.Transform(leadingPoint, modelWorldTransform);
        var worldTrailingPoint = Vector3.Transform(trailingPoint, modelWorldTransform);
        var forward = Vector3.Normalize(projectile.TransformMatrix.GetXVector());

        Assert.True(
            projectile.ModelTransform.Translation.LengthSquared() > 1e-6f,
            $"{projectile.Definition.Name} retained its geometric-center pivot");
        Assert.True(
            Vector3.Distance(worldLeadingPoint, projectile.Translation) < 1e-3f,
            $"{projectile.Definition.Name} leading point does not coincide with its simulation position");
        Assert.True(
            Vector3.Dot(worldTrailingPoint - projectile.Translation, forward) < -1e-3f,
            $"{projectile.Definition.Name} model does not trail behind its leading point");
    }
}
