using System.Collections.Generic;
using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.DataStructures;

public interface ICollidable
{
    Collider RoughCollider { get; }
    List<Collider> Colliders { get; }
    Vector3 Translation { get; }

    bool CollidesWith(ICollidable other, bool twoDimensional);
}
