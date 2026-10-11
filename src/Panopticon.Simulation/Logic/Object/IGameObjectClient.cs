using System.Collections.Generic;
using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

/// <summary>
/// Logic-facing surface of an object's client-side counterpart, implemented by
/// <c>Panopticon.Client.Drawable</c>. Logic may invoke these members as one-way commands and
/// asset-derived queries; logic must not read render-only state, so no member exposes a
/// presentation type.
/// </summary>
public interface IGameObjectClient
{
    uint ClientObjectId { get; set; }

    bool ShadowsEnabled { get; set; }

    void LogicTick(in TimeInterval gameTime);

    void Destroy();

    void TriggerSelection();

    void SetAnimationDuration(LogicFrameSpan frames);

    void SetSupplyBoxesRemaining(float boxPercentage);

    void SetTerrainDecal(ObjectDecalType decalType);

    void ShowSubObject(string subObject);

    void HideSubObject(string subObject);

    void StopSway();

    void SetDebrisModel(string modelName);

    /// <summary>Bind-pose transform of the named bone, from the model asset.</summary>
    bool TryGetBoneTransform(string boneName, out Transform boneTransform);

    /// <summary>Current root-relative pose of the named bone; seeded to the bind pose, so the
    /// value is valid without rendering.</summary>
    bool TryGetBonePose(string boneName, out Matrix4x4 pose);

    Matrix4x4? GetWeaponLaunchBoneTransform(WeaponSlot slot, int weaponIndex);

    Matrix4x4? GetWeaponFireFXBoneTransform(WeaponSlot slot, int weaponIndex);

    bool TryGetModelSpaceBounds(out AxisAlignedBoundingBox bounds);

    /// <summary>Bounds of each visible sub-object in model space, for surface-approximate queries
    /// where the merged box overstates protrusions.</summary>
    bool TryGetModelSpacePartBounds(out IReadOnlyList<AxisAlignedBoundingBox> partBounds);

    /// <summary>Debug-diagnostics only: draw client modules in the inspector.</summary>
    void DrawInspector();
}

/// <summary>Decal types shown under an object by <see cref="IGameObjectClient.SetTerrainDecal"/>.
/// Parsed from INI and persisted in save files.</summary>
public enum ObjectDecalType
{
    HordeInfantry = 1, // exhorde.dds
    NationalismInfantry = 2, // exhorde_up.dds
    HordeVehicle = 3, // exhordeb.dds
    NationalismVehicle = 4, // exhordeb_up.dds
    Crate = 5, // exjunkcrate.dds
    HordeWithFanaticismUpgrade = 6,
    ChemSuit = 7,
    None = 8,
    ShadowTexture = 9,
}
