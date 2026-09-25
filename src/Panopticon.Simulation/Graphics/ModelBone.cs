using System.Diagnostics;
using System.Numerics;

namespace Panopticon.Graphics;

[DebuggerDisplay("ModelBone '{Name}'")]
public sealed class ModelBone
{
    public int Index { get; }

    public string Name { get; }

    public ModelBone Parent { get; }

    public Transform Transform { get; }

    public Matrix4x4 InverseBindMatrix { get; }

    internal ModelBone(
        int index,
        string name,
        ModelBone parent,
        in Vector3 translation,
        in Quaternion rotation,
        in Matrix4x4 inverseBindMatrix)
    {
        Index = index;
        Name = name;
        Parent = parent;

        Transform = new Transform(translation, rotation);
        InverseBindMatrix = inverseBindMatrix;
    }
}
