using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using OpenSage.Content.Loaders;
using OpenSage.Graphics.Animation;
using OpenSage.Graphics.Cameras;
using OpenSage.Graphics.Rendering;
using OpenSage.Graphics.Shaders;
using OpenSage.Mathematics;
using Veldrid;

namespace OpenSage.Graphics;

public sealed class ModelInstance : DisposableBase
{
    private readonly GraphicsDevice _graphicsDevice;

    internal readonly ModelMeshInstance[] MeshInstances;

    /// <summary>
    /// Bone transforms relative to root bone.
    /// </summary>
    internal readonly Matrix4x4[] RelativeBoneTransforms;

    /// <summary>
    /// Bone transforms in world space.
    /// </summary>
    internal readonly Matrix4x4[] AbsoluteBoneTransforms;

    private Matrix4x4 _worldMatrix;

    /// <summary>
    /// Calculated bone visibilities. Child bones will be hidden
    /// if their parent bones are hidden.
    /// </summary>
    internal readonly bool[] BoneVisibilities;

    /// <summary>
    /// Calculated bone visibilities for the current frame. Child bones will be hidden
    /// if their parent bones are hidden.
    /// </summary>
    internal readonly bool[] BoneFrameVisibilities;

    private readonly Matrix4x4[] _skinningBones;

    private readonly DeviceBuffer _skinningBuffer;

    public readonly Model Model;

    public readonly ModelBoneInstance[] ModelBoneInstances;

    public readonly List<AnimationInstance> AnimationInstances;

    public readonly DeviceBuffer SkinningBuffer;

    // TODO: Use this.
    public ColorRgba HouseColor;

    public readonly bool[] UnknownBools;

    internal readonly ConstantBuffer<MeshShaderResources.RenderItemConstantsPS> RenderItemConstantsBufferPS;

    internal ModelInstance(Model model, AssetLoadContext loadContext)
    {
        Model = model;

        _graphicsDevice = loadContext.GraphicsDevice;

        ModelBoneInstances = new ModelBoneInstance[model.BoneHierarchy.Bones.Length];
        for (var i = 0; i < model.BoneHierarchy.Bones.Length; i++)
        {
            ModelBoneInstances[i] = new ModelBoneInstance(model.BoneHierarchy.Bones[i]);
        }

        RelativeBoneTransforms = new Matrix4x4[model.BoneHierarchy.Bones.Length];
        AbsoluteBoneTransforms = new Matrix4x4[model.BoneHierarchy.Bones.Length];

        // Seed the bind pose so bone transforms are valid before the first render (logic-side
        // queries like weapon launch bones read AbsoluteBoneTransforms, and headless runs
        // never render). Bones are ordered parents-before-children, matching Update().
        for (var i = 0; i < model.BoneHierarchy.Bones.Length; i++)
        {
            var bone = model.BoneHierarchy.Bones[i];
            var parentTransform = bone.Parent != null
                ? RelativeBoneTransforms[bone.Parent.Index]
                : Matrix4x4.Identity;
            RelativeBoneTransforms[i] = ModelBoneInstances[i].Matrix * parentTransform;
            AbsoluteBoneTransforms[i] = RelativeBoneTransforms[i];
        }

        BoneVisibilities = new bool[model.BoneHierarchy.Bones.Length];
        BoneFrameVisibilities = new bool[model.BoneHierarchy.Bones.Length];

        for (var i = 0; i < model.BoneHierarchy.Bones.Length; i++)
        {
            BoneVisibilities[i] = true;
            BoneFrameVisibilities[i] = true;
        }

        if (model.HasSkinnedMeshes)
        {
            _skinningBuffer = SkinningBuffer = AddDisposable(_graphicsDevice.ResourceFactory.CreateBuffer(
                new BufferDescription(
                    (uint)(64 * model.BoneHierarchy.Bones.Length),
                    BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic,
                    64,
                    true)));

            _skinningBones = new Matrix4x4[model.BoneHierarchy.Bones.Length];
        }
        else
        {
            SkinningBuffer = loadContext.StandardGraphicsResources.GetNullStructuredBuffer(64);
        }

        AnimationInstances = new List<AnimationInstance>();

        RenderItemConstantsBufferPS = AddDisposable(new ConstantBuffer<MeshShaderResources.RenderItemConstantsPS>(_graphicsDevice, "RenderItemConstantsPS"));

        RenderItemConstantsBufferPS.Value = new MeshShaderResources.RenderItemConstantsPS
        {
            HouseColor = Vector3.One,
            Opacity = 1.0f,
            TintColor = Vector3.One
        };
        RenderItemConstantsBufferPS.Update(_graphicsDevice);

        MeshInstances = new ModelMeshInstance[model.SubObjects.Length];

        for (var i = 0; i < model.SubObjects.Length; i++)
        {
            var renderObject = model.SubObjects[i].RenderObject;

            if (!(renderObject is ModelMesh mesh))
            {
                continue;
            }

            MeshInstances[i] = AddDisposable(
                new ModelMeshInstance(
                    mesh,
                    this,
                    loadContext));
        }

        UnknownBools = new bool[model.SubObjects.Length];
    }

    public void Update(in TimeInterval gameTime)
    {
        // TODO: Don't update animations if model isn't visible.

        // Update animations.
        foreach (var animationInstance in AnimationInstances)
        {
            animationInstance.Update(gameTime);
        }

        // Check if any model bone transform has changed.
        var isAnyModelBoneInstanceDirty = false;
        foreach (var modelBoneInstance in ModelBoneInstances)
        {
            if (modelBoneInstance.IsDirty)
            {
                isAnyModelBoneInstanceDirty = true;
                break;
            }
        }

        if (isAnyModelBoneInstanceDirty)
        {
            // Calculate (animated) bone transforms relative to root bone.
            for (var i = 0; i < Model.BoneHierarchy.Bones.Length; i++)
            {
                var bone = Model.BoneHierarchy.Bones[i];

                var parentTransform = bone.Parent != null
                    ? RelativeBoneTransforms[bone.Parent.Index]
                    : Matrix4x4.Identity;

                RelativeBoneTransforms[i] = ModelBoneInstances[i].Matrix * parentTransform;
            }
        }

        // Refresh per-frame bone visibility unconditionally. Render visibility is
        // driven by BoneVisibilities[] (e.g. muzzle-flash toggling in W3dModelDraw)
        // and ModelBoneInstance.Visible, neither of which dirties a bone transform.
        // Gating this on isAnyModelBoneInstanceDirty left stale visibility in effect:
        // a muzzle flash revealed while firing stayed rendered after firing stopped
        // whenever no bone transform changed that frame.
        for (var i = 0; i < Model.BoneHierarchy.Bones.Length; i++)
        {
            var bone = Model.BoneHierarchy.Bones[i];

            var parentVisible = bone.Parent == null || (BoneVisibilities[bone.Parent.Index] && BoneFrameVisibilities[bone.Parent.Index]);

            BoneFrameVisibilities[i] = BoneVisibilities[i] && parentVisible && ModelBoneInstances[i].Visible;
        }

        foreach (var modelBoneInstance in ModelBoneInstances)
        {
            modelBoneInstance.ResetDirty();
        }

        if (!Model.HasSkinnedMeshes)
        {
            return;
        }

        for (var i = 0; i < Model.BoneHierarchy.Bones.Length; i++)
        {
            _skinningBones[i] = RelativeBoneTransforms[i];
        }

        _graphicsDevice.UpdateBuffer(_skinningBuffer, 0, _skinningBones);
    }

    public ref readonly Matrix4x4 GetWorldMatrix() => ref _worldMatrix;

    public void SetWorldMatrix(in Matrix4x4 worldMatrix)
    {
        _worldMatrix = worldMatrix;

        for (var i = 0; i < Model.BoneHierarchy.Bones.Length; i++)
        {
            AbsoluteBoneTransforms[i] = RelativeBoneTransforms[i] * worldMatrix;
        }
    }

    // A visibility dictionary entry applies to a sub-object when it is keyed by either the
    // sub-object's own name or its bone's name. With invert, the entry's meaning flips
    // (shown-dictionary entries with value false hide the sub-object).
    private static bool IsHidden(Dictionary<string, bool> visibility, ModelSubObject subObject, bool invert = false)
    {
        if (visibility == null)
        {
            return false;
        }

        var value = visibility.TryGetValue(subObject.Name, out var byName)
            || (subObject.Bone?.Name != null && visibility.TryGetValue(subObject.Bone.Name, out byName));
        if (!value)
        {
            return false;
        }

        return invert ? !byName : byName;
    }

    internal void BuildRenderList(
        RenderList renderList,
        Camera camera,
        bool castsShadow,
        MeshShaderResources.RenderItemConstantsPS renderItemConstantsPS,
        Dictionary<string, bool> shownSubObjects = null,
        Dictionary<string, bool> hiddenSubObjects = null)
    {
        if (RenderItemConstantsBufferPS.Value != renderItemConstantsPS)
        {
            RenderItemConstantsBufferPS.Value = renderItemConstantsPS;
            RenderItemConstantsBufferPS.Update(_graphicsDevice);
        }

        for (var i = 0; i < Model.SubObjects.Length; i++)
        {
            var subObject = Model.SubObjects[i];

            // Sub-object visibility is controlled by name: forced-hidden (WeaponHideShowBone
            // projectile feedback, e.g. the SCUD's stowed missile) or forced-shown
            // (SubObjectsUpgrade) entries override the model default. Entries are keyed by
            // bone name, so match the sub-object's bone as well as its own name.
            if (IsHidden(hiddenSubObjects, subObject) || IsHidden(shownSubObjects, subObject, invert: true))
            {
                continue;
            }

            subObject.RenderObject.BuildRenderList(
                renderList,
                camera,
                this,
                MeshInstances[i],
                subObject.Bone,
                _worldMatrix,
                castsShadow,
                renderItemConstantsPS);
        }
    }

    internal void DrawInspector()
    {
        ImGui.LabelText("Model", Model.Name ?? "<null>");

        if (ImGui.TreeNodeEx("Animations", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var animationInstance in AnimationInstances)
            {
                animationInstance.DrawInspector();
            }
            ImGui.TreePop();
        }
    }
}
