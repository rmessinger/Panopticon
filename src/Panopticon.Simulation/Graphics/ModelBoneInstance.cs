using System.Diagnostics;
using System.Numerics;

namespace Panopticon.Graphics;

[DebuggerDisplay("ModelBoneInstance '{Name}'")]
public sealed class ModelBoneInstance
{
    private readonly ModelBone _modelBone;
    private bool _isDirty;
    private bool _absoluteLocalActive;
    private Vector3 _absoluteLocalTranslation;
    private Quaternion _absoluteLocalRotation;
    private Vector3 _absoluteLocalScale;
    private bool _visible;
    public string Name { get { return _modelBone.Name; } }

    /// <summary>
    /// Animated transform, relative to original bone transform.
    /// </summary>
    public Transform AnimatedOffset { get; }

    /// <summary>
    /// Is this bone visible? This property can be animated.
    /// </summary>
    public bool Visible
    {
        get => _visible;
        internal set
        {
            _visible = value;
            _isDirty = true;
        }
    }

    internal bool IsDirty => _isDirty || AnimatedOffset.IsDirty;

    internal bool AbsoluteLocalActive => _absoluteLocalActive;

    internal Vector3 AbsoluteLocalTranslation
    {
        get
        {
            EnsureAbsoluteLocalActive();
            return _absoluteLocalTranslation;
        }

        set
        {
            EnsureAbsoluteLocalActive();
            _absoluteLocalTranslation = value;
            _isDirty = true;
        }
    }

    internal Quaternion AbsoluteLocalRotation
    {
        get
        {
            EnsureAbsoluteLocalActive();
            return _absoluteLocalRotation;
        }

        set
        {
            EnsureAbsoluteLocalActive();
            _absoluteLocalRotation = value;
            _isDirty = true;
        }
    }

    internal Vector3 AbsoluteLocalScale
    {
        get
        {
            EnsureAbsoluteLocalActive();
            return _absoluteLocalScale;
        }

        set
        {
            EnsureAbsoluteLocalActive();
            _absoluteLocalScale = value;
            _isDirty = true;
        }
    }

    public Matrix4x4 Matrix =>
        _absoluteLocalActive
            ? Matrix4x4.CreateScale(_absoluteLocalScale) *
              Matrix4x4.CreateFromQuaternion(_absoluteLocalRotation) *
              Matrix4x4.CreateTranslation(_absoluteLocalTranslation)
            : AnimatedOffset.Matrix * _modelBone.RestMatrix;

    internal ModelBoneInstance(ModelBone modelBone)
    {
        _modelBone = modelBone;

        AnimatedOffset = Transform.CreateIdentity();

        Visible = true;

        _isDirty = true;
    }

    internal void ResetDirty()
    {
        _isDirty = false;
    }

    internal void ClearAbsoluteLocal()
    {
        if (_absoluteLocalActive)
        {
            _isDirty = true;
        }

        _absoluteLocalActive = false;
        _absoluteLocalTranslation = Vector3.Zero;
        _absoluteLocalRotation = Quaternion.Identity;
        _absoluteLocalScale = Vector3.One;
    }

    private void EnsureAbsoluteLocalActive()
    {
        if (_absoluteLocalActive)
        {
            return;
        }

        _absoluteLocalTranslation = _modelBone.Transform.Translation;
        _absoluteLocalRotation = _modelBone.Transform.Rotation;
        _absoluteLocalScale = _modelBone.BindScale;
        _absoluteLocalActive = true;
        _isDirty = true;
    }
}
