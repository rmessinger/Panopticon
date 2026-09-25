using System;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;

namespace Panopticon.Physics.Bepu;

/// <summary>
/// One dynamic body in a <see cref="BepuPhysicsWorld"/>. All quantities are
/// per-second. <see cref="ApplyForce"/> accumulates between timesteps and is
/// integrated as force times the next timestep.
/// </summary>
public sealed class BepuPhysicsBody : IPhysicsBody
{
    private readonly BepuPhysicsWorld _world;
    private readonly PhysicsShape _shapeDescription;
    private Vector3 _pendingForce;

    internal BepuPhysicsBody(BepuPhysicsWorld world, BodyHandle handle, PhysicsShape shapeDescription)
    {
        _world = world;
        Handle = handle;
        _shapeDescription = shapeDescription;
    }

    internal BodyHandle Handle { get; }

    private BodyReference Reference => _world.Simulation.Bodies[Handle];

    public Vector3 Position
    {
        get => Reference.Pose.Position;
        set
        {
            var reference = Reference;
            reference.Pose.Position = value;
        }
    }

    public Quaternion Rotation
    {
        get => Reference.Pose.Orientation;
        set
        {
            var reference = Reference;
            reference.Pose.Orientation = value;
        }
    }

    public Vector3 LinearVelocity
    {
        get => Reference.Velocity.Linear;
        set
        {
            var reference = Reference;
            reference.Awake = true;
            reference.Velocity.Linear = value;
        }
    }

    public Vector3 AngularVelocity
    {
        get => Reference.Velocity.Angular;
        set
        {
            var reference = Reference;
            reference.Awake = true;
            reference.Velocity.Angular = value;
        }
    }

    public float Mass
    {
        get
        {
            var inverseMass = Reference.LocalInertia.InverseMass;
            return inverseMass > 0.0f ? 1.0f / inverseMass : float.PositiveInfinity;
        }

        set
        {
            // Only analytic shapes recompute inertia; hull mass changes are not supported yet.
            var reference = Reference;
            switch (_shapeDescription)
            {
                case PhysicsShape.Sphere sphere:
                    reference.SetLocalInertia(new BepuPhysics.Collidables.Sphere(sphere.Radius).ComputeInertia(value));
                    break;
                case PhysicsShape.Box box:
                    reference.SetLocalInertia(
                        new BepuPhysics.Collidables.Box(box.HalfExtents.X * 2.0f, box.HalfExtents.Y * 2.0f, box.HalfExtents.Z * 2.0f).ComputeInertia(value));
                    break;
                case PhysicsShape.Cylinder cylinder:
                    reference.SetLocalInertia(new BepuPhysics.Collidables.Cylinder(cylinder.Radius, cylinder.Height).ComputeInertia(value));
                    break;
            }
        }
    }

    public bool IsAwake => Reference.Awake;

    public bool IsGrounded
    {
        get
        {
            var handler = new GroundedRayHandler();
            var origin = Position;
            var direction = -Vector3.UnitZ;
            var maximumT = 1.0f + 0.5f;
            _world.Simulation.RayCast(in origin, in direction, maximumT, ref handler);
            return handler.Hit;
        }
    }

    public void ApplyForce(Vector3 force)
    {
        _pendingForce += force;
    }

    public void ApplyImpulse(Vector3 impulse, Vector3 relativePosition)
    {
        var reference = Reference;
        reference.Awake = true;
        reference.ApplyImpulse(impulse, relativePosition);
    }

    internal void FlushPendingForce(float deltaTime)
    {
        if (_pendingForce != Vector3.Zero)
        {
            var reference = Reference;
            reference.Awake = true;
            reference.ApplyLinearImpulse(_pendingForce * deltaTime);
            _pendingForce = Vector3.Zero;
        }
    }

    private struct GroundedRayHandler : IRayHitHandler
    {
        public bool Hit;

        public bool AllowTest(CollidableReference collidable)
        {
            return true;
        }

        public bool AllowTest(CollidableReference collidable, int childIndex)
        {
            return true;
        }

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            Hit = true;
            maximumT = t;
        }
    }
}
