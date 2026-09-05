using System;
using System.Numerics;

namespace OpenSage.Physics;

public interface IPhysicsWorld
{
    IPhysicsBody CreateBody(in PhysicsBodyDescription description);

    void RemoveBody(IPhysicsBody body);

    void SetTerrainChunk(int chunkX, int chunkY, ReadOnlySpan<TerrainPhysicsVertex> vertices, ReadOnlySpan<int> indices);

    void Timestep(float deltaTime);
}
