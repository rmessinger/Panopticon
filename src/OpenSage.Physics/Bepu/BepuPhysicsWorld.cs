using System;
using System.Collections.Generic;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace OpenSage.Physics.Bepu;

/// <summary>
/// BepuPhysics 2.4 implementation of the physics seam. Bodies use per-second
/// units; forces accumulated through <see cref="IPhysicsBody.ApplyForce"/> are
/// integrated as force times the next timestep, matching per-step force
/// accumulators. Terrain chunks become one static mesh each, in world coordinates.
/// </summary>
public sealed class BepuPhysicsWorld : IPhysicsWorld, IDisposable
{
    private const float SpeculativeMargin = 2.0f;
    private const float SleepThreshold = 0.3f;
    private const float FrictionCoefficient = 0.3f;
    private const float MaximumRecoveryVelocity = 30.0f;
    private const float SpringFrequency = 30.0f;
    private const float SpringDampingRatio = 1.0f;

    private readonly BufferPool _bufferPool;
    private readonly Simulation _simulation;
    private readonly List<BepuPhysicsBody> _bodies = [];
    private readonly Dictionary<(int ChunkX, int ChunkY), StaticHandle> _terrainChunks = [];

    public BepuPhysicsWorld(Vector3 gravityPerSecondSquared)
    {
        _bufferPool = new BufferPool();
        _simulation = Simulation.Create(
            _bufferPool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks(gravityPerSecondSquared),
            new SolveDescription(velocityIterationCount: 8, substepCount: 1, fallbackBatchThreshold: 64));
    }

    internal Simulation Simulation => _simulation;

    public IPhysicsBody CreateBody(in PhysicsBodyDescription description)
    {
        var pose = new RigidPose(description.Position, description.Rotation);
        BepuPhysicsBody body;

        switch (description.Shape)
        {
            case PhysicsShape.Sphere sphere:
                {
                    var shape = new BepuPhysics.Collidables.Sphere(sphere.Radius);
                    var bodyDescription = BodyDescription.CreateConvexDynamic(pose, description.Mass, _simulation.Shapes, in shape);
                    body = AddBody(bodyDescription, description);
                    break;
                }

            case PhysicsShape.Box box:
                {
                    // Bepu Box takes full extents; the seam description uses half extents.
                    var shape = new BepuPhysics.Collidables.Box(
                        box.HalfExtents.X * 2.0f,
                        box.HalfExtents.Y * 2.0f,
                        box.HalfExtents.Z * 2.0f);
                    var bodyDescription = BodyDescription.CreateConvexDynamic(pose, description.Mass, _simulation.Shapes, in shape);
                    body = AddBody(bodyDescription, description);
                    break;
                }

            case PhysicsShape.Cylinder cylinder:
                {
                    var shape = new BepuPhysics.Collidables.Cylinder(cylinder.Radius, cylinder.Height);
                    var bodyDescription = BodyDescription.CreateConvexDynamic(pose, description.Mass, _simulation.Shapes, in shape);
                    body = AddBody(bodyDescription, description);
                    break;
                }

            case PhysicsShape.ConvexHull hull:
                {
                    var points = new Vector3[hull.Points.Count];
                    for (var i = 0; i < points.Length; i++)
                    {
                        points[i] = hull.Points[i];
                    }

                    var shape = new ConvexHull(points.AsSpan(), _bufferPool, out _);
                    var shapeIndex = _simulation.Shapes.Add(shape);
                    var inertia = shape.ComputeInertia(description.Mass);
                    var bodyDescription = BodyDescription.CreateDynamic(
                        pose,
                        description.IsStatic ? new BodyInertia() : inertia,
                        new CollidableDescription(shapeIndex, SpeculativeMargin),
                        new BodyActivityDescription(SleepThreshold));
                    body = AddBody(bodyDescription, description);
                    break;
                }

            case PhysicsShape.TriangleList triangleList:
                {
                    if (!description.IsStatic)
                    {
                        throw new NotSupportedException("Dynamic triangle-list bodies are not supported; use a convex hull.");
                    }

                    var vertices = new TerrainPhysicsVertex[triangleList.Vertices.Count];
                    for (var i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = triangleList.Vertices[i];
                    }

                    var indices = new int[triangleList.Indices.Count];
                    for (var i = 0; i < indices.Length; i++)
                    {
                        indices[i] = triangleList.Indices[i];
                    }

                    var mesh = CreateMesh(vertices, indices);
                    var shapeIndex = _simulation.Shapes.Add(mesh);
                    var bodyDescription = BodyDescription.CreateKinematic(
                        pose,
                        new CollidableDescription(shapeIndex, SpeculativeMargin),
                        new BodyActivityDescription(SleepThreshold));
                    body = AddBody(bodyDescription, description);
                    break;
                }

            default:
                throw new NotSupportedException($"Unsupported shape {description.Shape?.GetType().Name}");
        }

        return body;
    }

    private BepuPhysicsBody AddBody(in BodyDescription bodyDescription, in PhysicsBodyDescription description)
    {
        var handle = _simulation.Bodies.Add(bodyDescription);
        var body = new BepuPhysicsBody(this, handle, description.Shape);
        _bodies.Add(body);
        return body;
    }

    public void RemoveBody(IPhysicsBody body)
    {
        if (body is not BepuPhysicsBody bepuBody)
        {
            return;
        }

        _bodies.Remove(bepuBody);
        _simulation.Bodies.Remove(bepuBody.Handle);
    }

    public void SetTerrainChunk(int chunkX, int chunkY, ReadOnlySpan<TerrainPhysicsVertex> vertices, ReadOnlySpan<int> indices)
    {
        if (_terrainChunks.TryGetValue((chunkX, chunkY), out var existingHandle))
        {
            var reference = _simulation.Statics[existingHandle];
            var shapeIndex = reference.Shape;
            _simulation.Statics.Remove(existingHandle);
            DisposeShape(shapeIndex);
        }

        var mesh = CreateMesh(vertices, indices);
        var newShapeIndex = _simulation.Shapes.Add(mesh);
        _terrainChunks[(chunkX, chunkY)] = _simulation.Statics.Add(
            new StaticDescription(RigidPose.Identity, newShapeIndex));
    }

    private Mesh CreateMesh(ReadOnlySpan<TerrainPhysicsVertex> vertices, ReadOnlySpan<int> indices)
    {
        var vertexArray = new Vector3[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertexArray[i] = vertices[i].Position;
        }

        return CreateMesh(vertexArray.AsSpan(), indices);
    }

    private Mesh CreateMesh(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices)
    {
        _bufferPool.Take<Triangle>(indices.Length / 3, out var triangles);
        for (var i = 0; i < indices.Length; i += 3)
        {
            // Bepu's mesh winding is opposite the engine's terrain winding (verified
            // empirically: this order puts triangle normals up in +Z).
            triangles[i / 3] = new Triangle(
                vertices[indices[i]],
                vertices[indices[i + 2]],
                vertices[indices[i + 1]]);
        }

        var scale = Vector3.One;
        return new Mesh(triangles, in scale, _bufferPool);
    }

    private void DisposeShape(TypedIndex shapeIndex)
    {
        if (shapeIndex.Exists)
        {
            _simulation.Shapes.GetShape<Mesh>(shapeIndex.Index).Dispose(_bufferPool);
            _simulation.Shapes.Remove(shapeIndex);
        }
    }

    public void Timestep(float deltaTime)
    {
        foreach (var body in _bodies)
        {
            body.FlushPendingForce(deltaTime);
        }

        _simulation.Timestep(deltaTime);
    }

    public void Dispose()
    {
        _simulation.Dispose();
        _bufferPool.Clear();
        GC.SuppressFinalize(this);
    }

    private struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        // Light angular damping keeps sliding boxes from tumbling endlessly;
        // matches the feel of the legacy translation-only integrator.
        private const float AngularDampingPerStep = 0.98f;

        private Vector3 _gravity;
        private Vector3Wide _gravityWide;
        private Vector<float> _dtWide;
        private Vector<float> _angularDampingWide;

        public PoseIntegratorCallbacks(Vector3 gravityPerSecondSquared)
        {
            _gravity = gravityPerSecondSquared;
            _gravityWide = default;
            _dtWide = default;
            _angularDampingWide = default;
        }

        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentum;

        public bool AllowSubstepsForUnconstrainedBodies => false;

        public bool IntegrateVelocityForKinematics => false;

        public void Initialize(Simulation simulation)
        {
        }

        public void PrepareForIntegration(float dt)
        {
            _gravityWide = Vector3Wide.Broadcast(_gravity);
            _dtWide = new Vector<float>(dt);
            _angularDampingWide = new Vector<float>(AngularDampingPerStep);
        }

        public void IntegrateVelocity(
            System.Numerics.Vector<int> bodyIndices,
            Vector3Wide position,
            QuaternionWide orientation,
            BodyInertiaWide localInertia,
            System.Numerics.Vector<int> bundledBodyIndices,
            int count,
            System.Numerics.Vector<float> dt,
            ref BodyVelocityWide velocities)
        {
            velocities.Linear += _gravityWide * dt;
            velocities.Angular *= _angularDampingWide;
        }
    }

    private struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation)
        {
        }

        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        {
            return true;
        }

        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
        {
            return true;
        }

        public bool ConfigureContactManifold<TManifold>(
            int workerIndex,
            CollidablePair pair,
            ref TManifold manifold,
            out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial.FrictionCoefficient = FrictionCoefficient;
            pairMaterial.MaximumRecoveryVelocity = MaximumRecoveryVelocity;
            pairMaterial.SpringSettings = new SpringSettings(SpringFrequency, SpringDampingRatio);
            return true;
        }

        public bool ConfigureContactManifold(
            int workerIndex,
            CollidablePair pair,
            int childIndexA,
            int childIndexB,
            ref ConvexContactManifold manifold)
        {
            return true;
        }

        public void Dispose()
        {
        }
    }
}
