using System.Numerics;

namespace Panopticon.Physics;

public interface IPhysicsBody
{
    Vector3 Position { get; set; }

    Quaternion Rotation { get; set; }

    Vector3 LinearVelocity { get; set; }

    Vector3 AngularVelocity { get; set; }

    float Mass { get; set; }

    bool IsAwake { get; }

    bool IsGrounded { get; }

    void ApplyForce(Vector3 force);

    void ApplyImpulse(Vector3 impulse, Vector3 relativePosition);
}
