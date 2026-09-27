using System;
using System.Numerics;

namespace Panopticon.Graphics.Animation;

/// <summary>
/// Per-instance animation state for sub-object visibility and material-slot property overrides.
/// AnimationInstance writes sampled values here; render code reads them when building the
/// render list and per-draw constant buffers. No override flag means "use the material default".
/// </summary>
internal sealed class ModelPropertyAnimationState
{
    private readonly bool[] _subObjectVisibilities;
    private readonly ModelMaterialSlotState[][] _slots;

    public ModelPropertyAnimationState(Model model)
    {
        _subObjectVisibilities = new bool[model.SubObjects.Length];

        _slots = new ModelMaterialSlotState[model.SubObjects.Length][];
        for (var i = 0; i < model.SubObjects.Length; i++)
        {
            _subObjectVisibilities[i] = true;

            _slots[i] = new ModelMaterialSlotState[(model.SubObjects[i].RenderObject as ModelMesh)?.MaterialCount ?? 0];
        }
    }

    public void Reset()
    {
        for (var i = 0; i < _subObjectVisibilities.Length; i++)
        {
            _subObjectVisibilities[i] = true;
        }

        for (var i = 0; i < _slots.Length; i++)
        {
            Array.Clear(_slots[i], 0, _slots[i].Length);
        }
    }

    public bool IsSubObjectVisible(int subObjectIndex)
    {
        return _subObjectVisibilities[subObjectIndex];
    }

    public void SetSubObjectVisible(int subObjectIndex, bool visible)
    {
        _subObjectVisibilities[subObjectIndex] = visible;
    }

    /// <summary>True when the slot has an animated alpha; sampled alpha then drives the blend queue.</summary>
    public bool TryGetAlpha(int subObjectIndex, int materialIndex, out float alpha)
    {
        ref var slot = ref GetSlot(subObjectIndex, materialIndex);

        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.Alpha) != 0)
        {
            alpha = slot.Alpha;
            return true;
        }

        alpha = 1.0f;
        return false;
    }

    /// <summary>Copies the slot state when any override is active; false means "use material defaults".</summary>
    public bool TryGetSlotOverride(int subObjectIndex, int materialIndex, out ModelMaterialSlotState slot)
    {
        slot = GetSlot(subObjectIndex, materialIndex);

        return slot.OverrideFlags != 0;
    }

    public ref ModelMaterialSlotState GetSlot(int subObjectIndex, int materialIndex)
    {
        return ref _slots[subObjectIndex][materialIndex];
    }
}

/// <summary>Animated material values for one sub-object material slot; flag bits mark which fields carry overrides.</summary>
internal struct ModelMaterialSlotState
{
    public Vector3 Ambient;
    public Vector3 Diffuse;
    public Vector3 Specular;
    public Vector3 Emissive;
    public float Alpha;
    public float UvTranslateU;
    public float UvTranslateV;
    public float UvScaleU;
    public float UvScaleV;
    public float UvRotation;
    public uint OverrideFlags;
}

[Flags]
internal enum ModelMaterialOverrideFlags : uint
{
    None = 0,
    Ambient = 1,
    Diffuse = 2,
    Specular = 4,
    Emissive = 8,
    Alpha = 16,
    UvTranslate = 32,
    UvScale = 64,
    UvRotate = 128,
}
