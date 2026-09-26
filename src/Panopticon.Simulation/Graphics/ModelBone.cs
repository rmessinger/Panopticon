using System.Diagnostics;
using System.Numerics;

namespace Panopticon.Graphics;

[DebuggerDisplay("ModelBone '{Name}'")]
public sealed class ModelBone
{
    public int Index { get; }

    public string Name { get; }

    public ModelBone Parent { get; }

    public Vector3 BindScale { get; }

    public Matrix4x4 RestMatrix { get; }

    public Transform Transform { get; }

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
        : this(index, name, parent, translation, rotation, inverseBindMatrix, new Vector3(bindScale))
    {
    }

    internal ModelBone(
        int index,
        string name,
        ModelBone parent,
        in Vector3 translation,
        in Quaternion rotation,
        in Matrix4x4 inverseBindMatrix,
        in Vector3 bindScale)
    {
        Index = index;
        Name = name;
        Parent = parent;

        BindScale = bindScale;
        RestMatrix =
            Matrix4x4.CreateScale(bindScale) *
            Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(translation);
        Transform = new Transform(translation, rotation, IsUniformScale(bindScale) ? bindScale.X : 1.0f);
        InverseBindMatrix = inverseBindMatrix;
    }

    private static bool IsUniformScale(in Vector3 scale)
    {
        return scale.X == scale.Y && scale.X == scale.Z;
    }
}
