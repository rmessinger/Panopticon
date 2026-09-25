using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Client;
using Panopticon.Data.Ini;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

public sealed class W3dDefaultDraw : DrawModule
{
    public override IEnumerable<BitArray<ModelConditionFlag>> ModelConditionStates => Array.Empty<BitArray<ModelConditionFlag>>();

    internal override void BuildRenderList(
        RenderList renderList,
        Camera camera,
        bool castsShadow,
        MeshShaderResources.RenderItemConstantsPS renderItemConstantsPS,
        Dictionary<string, bool> shownSubObjects = null, Dictionary<string, bool> hiddenSubObjects = null)
    {
        // Nothing.
    }

    internal override (ModelInstance, ModelBone) FindBone(string boneName)
    {
        throw new System.NotImplementedException();
    }

    internal override string GetWeaponFireFXBone(WeaponSlot slot)
    {
        throw new System.NotImplementedException();
    }

    internal override string GetWeaponLaunchBone(WeaponSlot slot)
    {
        throw new System.NotImplementedException();
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();
    }

    internal override void SetWorldMatrix(in Matrix4x4 worldMatrix)
    {

    }

    internal override void Update(in TimeInterval time)
    {

    }
}

/// <summary>
/// All world objects should use a draw module. This module is used where an object should
/// never actually be drawn due either to the nature or type of the object or because its
/// drawing is handled by other logic, e.g. bridges.
/// </summary>
public sealed class W3dDefaultDrawModuleData : DrawModuleData
{
    internal static W3dDefaultDrawModuleData Parse(IniParser parser) => parser.ParseBlock(DefaultFieldParseTable);

    internal static readonly IniParseTable<W3dDefaultDrawModuleData> DefaultFieldParseTable = new IniParseTable<W3dDefaultDrawModuleData>();

    internal override DrawModule CreateDrawModule(Drawable drawable, IGameEngine gameEngine)
    {
        return new W3dDefaultDraw();
    }
}
