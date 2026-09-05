using System;
using System.Numerics;

namespace OpenSage.Physics.Legacy;

/// <summary>
/// World for the legacy backend. It is passive: bodies integrate from their owning
/// PhysicsBehavior update, so <see cref="Timestep"/> does nothing, and terrain is
/// queried through TerrainLogic rather than tracked here.
/// </summary>
public sealed class LegacyPhysicsWorld : IPhysicsWorld
{
    public IPhysicsBody CreateBody(in PhysicsBodyDescription description)
    {
        return new LegacyPhysicsBody
        {
            Position = description.Position,
            Rotation = description.Rotation,
            Mass = description.Mass,
        };
    }

    public void RemoveBody(IPhysicsBody body)
    {
    }

    public void SetTerrainChunk(int chunkX, int chunkY, ReadOnlySpan<TerrainPhysicsVertex> vertices, ReadOnlySpan<int> indices)
    {
    }

    public void Timestep(float deltaTime)
    {
    }
}
