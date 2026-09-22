using System;
using System.Numerics;
using OpenSage.Mathematics;

namespace OpenSage.Physics.Legacy;

/// <summary>
/// The reference physics backend: translation-only Euler integration with hand-set
/// rotation rates, extracted verbatim from PhysicsBehavior. Velocities and forces are
/// per logic frame, not per second. Position and rotation live in the owning object's
/// transform; the adapter syncs <see cref="Position"/> and <see cref="Rotation"/> as
/// shadows.
/// </summary>
public sealed class LegacyPhysicsBody : IPhysicsBody
{
    internal const float InvalidVelocityMagnitude = -1.0f;

    public Vector3 Velocity;
    public Vector3 Acceleration;
    public Vector3 PreviousAcceleration;
    public float YawRate;
    public float PitchRate;
    public float RollRate;
    public float VelocityMagnitude = InvalidVelocityMagnitude;

    public Vector3 Position { get; set; }

    public Quaternion Rotation { get; set; }

    public float Mass { get; set; } = 1.0f;

    public bool IsAwake => true;

    public bool IsGrounded { get; set; }

    public Vector3 LinearVelocity
    {
        get => Velocity;
        set => Velocity = value;
    }

    /// <summary>
    /// Rotation rates per logic frame about the object's X (roll), Y (pitch), and Z (yaw) axes.
    /// </summary>
    public Vector3 AngularVelocity
    {
        get => new(RollRate, PitchRate, YawRate);
        set
        {
            RollRate = value.X;
            PitchRate = value.Y;
            YawRate = value.Z;
        }
    }

    public void ApplyForce(Vector3 force)
    {
        // F = ma  -->  a = F/M (divide force by mass)
        Acceleration += force * (1.0f / Mass);
    }

    public void ApplyImpulse(Vector3 impulse, Vector3 relativePosition)
    {
        // The legacy backend has no angular dynamics; offset impulses act at the center of mass.
        Velocity += impulse * (1.0f / Mass);
    }

    public void IntegrateVelocity()
    {
        // Integrate acceleration into velocity.
        Velocity += Acceleration;

        // When velocity gets tiny, just clamp to zero.
        const float threshold = 0.001f;
        if (MathF.Abs(Velocity.X) < threshold)
        {
            Velocity.X = 0.0f;
        }

        if (MathF.Abs(Velocity.Y) < threshold)
        {
            Velocity.Y = 0.0f;
        }

        if (MathF.Abs(Velocity.Z) < threshold)
        {
            Velocity.Z = 0.0f;
        }

        VelocityMagnitude = InvalidVelocityMagnitude;
    }

    public void IntegrateRotation(ref Matrix4x4 matrix, float pitchRollYawFactor, float centerOfMassOffset)
    {
        // You may be tempted to do something like this:
        //
        // 	Real rollAngle = -mtx.Get_X_Rotation();
        // 	Real pitchAngle = mtx.Get_Y_Rotation();
        // 	Real yawAngle = mtx.Get_Z_Rotation();
        // 	// do stuff to angles, then rebuild the mtx with 'em
        //
        // You must resist this temptation, because your code will be wrong!
        //
        // The problem is that you can't use these calls to later reconstruct
        // the matrix... because doing such a thing is highly order-dependent,
        // and furthermore, you'd have to use Euler angles (Not the Get_?_Rotation
        // calls) to be able to reconstruct 'em, and that's too slow to do for
        // every object every frame.
        //
        // The one exception is that it is OK to use Get_Z_Rotation() to get
        // the yaw angle.

        var yawRateToUse = YawRate * pitchRollYawFactor;
        var pitchRateToUse = PitchRate * pitchRollYawFactor;
        var rollRateToUse = RollRate * pitchRollYawFactor;

        // With a center of mass listing, pitchRate needs to dampen towards
        // straight down / straight up.
        var offset = centerOfMassOffset;

        // Magnitude sets initial rate. Here we care about sign.
        if (offset != 0.0f)
        {
            var xvec = matrix.GetXVector();
            var xy = MathF.Sqrt(xvec.X * xvec.X + xvec.Y * xvec.Y);
            var pitchAngle = MathF.Atan2(xvec.Z, xy);
            var remainingAngle = (offset > 0)
                ? (MathF.PI / 2) - pitchAngle
                : -(MathF.PI / 2) + pitchAngle;
            var s = MathF.Sin(remainingAngle);
            pitchRateToUse *= s;
        }

        // Update rotation.
        // Original comment:
        // @todo Rotation should use torques, and integrate just like forces (MSB)
        // note, we DON'T want to Pre-rotate (either inplace or not),
        // since we want to add our mods to the existing matrix.
        matrix.RotateX(rollRateToUse);
        matrix.RotateY(pitchRateToUse);
        matrix.RotateZ(yawRateToUse);
    }

    public void DampYawPitchRoll(float factor)
    {
        PitchRate *= factor;
        RollRate *= factor;
        YawRate *= factor;
    }

    /// <summary>
    /// Returns the current velocity magnitude in the forward direction.
    /// If velocity is opposite the direction vector, the returned value is negative.
    /// </summary>
    public float GetForwardSpeed2D(in Vector3 direction)
    {
        var vx = Velocity.X * direction.X;
        var vy = Velocity.Y * direction.Y;

        var dot = vx + vy;

        var speedSquared = vx * vx + vy * vy;

        var speed = MathF.Sqrt(speedSquared);

        return dot >= 0.0f
            ? speed
            : -speed;
    }

    /// <summary>
    /// Returns the current velocity magnitude in the forward direction.
    /// If velocity is opposite the direction vector, the returned value is negative.
    /// </summary>
    public float GetForwardSpeed3D(in Vector3 direction)
    {
        var vx = Velocity.X * direction.X;
        var vy = Velocity.Y * direction.Y;
        var vz = Velocity.Z * direction.Z;

        var dot = vx + vy + vz;

        var speed = MathF.Sqrt(vx * vx + vy * vy + vz * vz);

        return dot >= 0.0f
            ? speed
            : -speed;
    }

    public void ScrubVelocityZ(float desiredVelocity)
    {
        if (MathF.Abs(desiredVelocity) < 0.001f)
        {
            Velocity.Z = 0;
        }
        else
        {
            if ((desiredVelocity < 0 && Velocity.Z < desiredVelocity) || (desiredVelocity > 0 && Velocity.Z > desiredVelocity))
            {
                Velocity.Z = desiredVelocity;
            }
        }

        VelocityMagnitude = InvalidVelocityMagnitude;
    }

    public void ScrubVelocity2D(float desiredVelocity)
    {
        if (desiredVelocity < 0.001f)
        {
            Velocity.X = 0;
            Velocity.Y = 0;
        }
        else
        {
            var curVelocity = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Y * Velocity.Y);
            if (desiredVelocity > curVelocity)
            {
                return;
            }

            desiredVelocity /= curVelocity;
            Velocity.X *= desiredVelocity;
            Velocity.Y *= desiredVelocity;
        }

        VelocityMagnitude = InvalidVelocityMagnitude;
    }
}
