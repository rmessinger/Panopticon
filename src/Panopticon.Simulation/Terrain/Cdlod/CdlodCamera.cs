using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Terrain.Cdlod;

public readonly record struct CdlodCamera(Vector3 Position, BoundingFrustum Frustum, float ProjectedScale);
