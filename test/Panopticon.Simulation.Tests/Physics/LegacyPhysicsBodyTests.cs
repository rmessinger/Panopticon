using System;
using System.Numerics;
using Panopticon.Mathematics;
using Panopticon.Physics;
using Panopticon.Physics.Legacy;
using Xunit;

namespace Panopticon.Tests.Physics;

public class PhysicsUnitConversionTests
{
    [Theory]
    [InlineData(0.0f, 30f, 0.0f)]
    [InlineData(-0.0711f, 30f, -63.99f)]
    [InlineData(-0.0711f, 5f, -1.7775f)]
    public void GravityPerFrameSquaredConvertsToPerSecondSquared(float perFrameSquared, float framesPerSecond, float expected)
    {
        var perSecondSquared = PhysicsUnitConversion.PerFrameSquaredToPerSecondSquared(perFrameSquared, framesPerSecond);
        Assert.Equal(expected, perSecondSquared, 2);
    }

    [Fact]
    public void FallDamageSpeedIsComputableFromRealGravityMagnitude()
    {
        // v = sqrt(2*g*h): the legacy per-frame gravity (-0.0711) times 900 gives the
        // real -64 dist/sec^2 magnitude, so a 40-unit fall yields a sane impact speed
        // instead of the NaN the per-frame constant produces on its own.
        var gravity = PhysicsUnitConversion.PerFrameSquaredToPerSecondSquared(-0.0711f, 30f);
        var speed = MathF.Sqrt(2.0f * MathF.Abs(gravity) * 40.0f);
        Assert.InRange(speed, 50.0f, 80.0f);
    }
}

public class LegacyPhysicsBodyTests
{
    [Fact]
    public void ApplyForceDividesByMass()
    {
        var body = new LegacyPhysicsBody { Mass = 5.0f };

        body.ApplyForce(new Vector3(45, 40, 35));

        Assert.Equal(new Vector3(9, 8, 7), body.Acceleration);
        Assert.Equal(Vector3.Zero, body.Velocity);
    }

    [Fact]
    public void IntegrateVelocityAddsAccelerationAndClampsTinyValues()
    {
        var body = new LegacyPhysicsBody { Mass = 1.0f };
        body.ApplyForce(new Vector3(1, 2, 3));

        body.IntegrateVelocity();

        Assert.Equal(new Vector3(1, 2, 3), body.Velocity);

        body.Acceleration = new Vector3(0.0005f, 0, 0);
        body.Velocity = Vector3.Zero;

        body.IntegrateVelocity();

        Assert.Equal(Vector3.Zero, body.Velocity);
    }

    [Fact]
    public void IntegrateRotationRotatesAroundZByYawRate()
    {
        var body = new LegacyPhysicsBody { YawRate = MathF.PI / 2.0f };
        var matrix = Matrix4x4.Identity;

        body.IntegrateRotation(ref matrix, pitchRollYawFactor: 1.0f, centerOfMassOffset: 0.0f);

        var xAxis = matrix.GetXVector();
        Assert.Equal(0.0f, Vector3.Dot(xAxis, Vector3.UnitX), 5);
        Assert.Equal(1.0f, xAxis.Length(), 5);
    }

    [Fact]
    public void CenterOfMassOffsetDampensPitchTowardsVertical()
    {
        var noseDown = new LegacyPhysicsBody { PitchRate = 1.0f };
        var matrixDown = Matrix4x4.Identity;
        noseDown.IntegrateRotation(ref matrixDown, 1.0f, 1.0f);
        var pitchedDown = matrixDown.GetXVector().Z;

        var noseUp = new LegacyPhysicsBody { PitchRate = 1.0f };
        var matrixUp = Matrix4x4.Identity;
        noseUp.IntegrateRotation(ref matrixUp, 1.0f, -1.0f);
        var pitchedUp = matrixUp.GetXVector().Z;

        // Positive offset pitches the X vector down; negative pitches it up.
        Assert.True(pitchedDown < 0.0f);
        Assert.True(pitchedUp > 0.0f);
    }

    [Fact]
    public void ApplyImpulseChangesVelocityWithoutTorque()
    {
        var body = new LegacyPhysicsBody { Mass = 2.0f };

        body.ApplyImpulse(new Vector3(10, 0, 0), new Vector3(0, 1, 0));

        Assert.Equal(new Vector3(5, 0, 0), body.Velocity);
        Assert.Equal(0.0f, body.RollRate);
        Assert.Equal(0.0f, body.PitchRate);
    }
}
