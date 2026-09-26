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

    public float BindScale { get; }

    public Matrix4x4 InverseBindMatrix { get; }

    internal ModelBone(
        int index,
        string name,
        ModelBone parent,
        in Vector3 translation,
        in Quaternion rotation,
        in Matrix4x4 inverseBindMatrix)
        : this(index, name, parent, translation, rotation, inverseBindMatrix, bindScale: 1.0f)
    {
    }

    internal ModelBone(
        int index,
        string name,
        ModelBone parent,
        in Vector3 translation,
        in Quaternion rotation,
        in Matrix4x4 inverseBindMatrix,
        float bindScale)
    {
        Index = index;
        Name = name;
        Parent = parent;

        BindScale = bindScale;
        Transform = new Transform(translation, rotation, bindScale);
        InverseBindMatrix = inverseBindMatrix;
    }
}
