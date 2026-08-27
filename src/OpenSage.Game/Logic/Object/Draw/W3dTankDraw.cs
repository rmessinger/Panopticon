using System.Numerics;
using OpenSage.Client;
using OpenSage.Content;
using OpenSage.Data.Ini;
using OpenSage.Graphics;
using OpenSage.Graphics.ParticleSystems;

namespace OpenSage.Logic.Object;

public class W3dTankDraw : W3dModelDraw
{
    private readonly W3dTankDrawModuleData _data;
    private readonly FXParticleSystemTemplate _treadDebrisLeft;
    private readonly FXParticleSystemTemplate _treadDebrisRight;

    internal W3dTankDraw(W3dTankDrawModuleData data, Drawable drawable, IGameEngine gameEngine)
        : base(data, drawable, gameEngine)
    {
        _data = data;
        _treadDebrisLeft = data.TreadDebrisLeft?.Value ?? gameEngine.AssetLoadContext.AssetStore.FXParticleSystemTemplates.GetByName("TrackDebrisDirtLeft");
        _treadDebrisRight = data.TreadDebrisRight?.Value ?? gameEngine.AssetLoadContext.AssetStore.FXParticleSystemTemplates.GetByName("TrackDebrisDirtRight");
    }

    internal override void Update(in TimeInterval gameTime)
    {
        base.Update(gameTime);

        // Turret yaw/pitch is applied by W3dModelDraw.Update from the condition state's
        // Turret/TurretPitch bones. TODO: Tread debris and tread texture animation.
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();
    }
}

/// <summary>
/// Default Draw used by tanks. Hardcoded to call for the TrackDebrisDirtRight and
/// TrackDebrisDirtLeft particle system definitions.
/// </summary>
public class W3dTankDrawModuleData : W3dModelDrawModuleData
{
    internal static W3dTankDrawModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    internal static new readonly IniParseTable<W3dTankDrawModuleData> FieldParseTable = W3dModelDrawModuleData.FieldParseTable
        .Concat(new IniParseTable<W3dTankDrawModuleData>
        {
            { "TreadDebrisLeft", (parser, x) => x.TreadDebrisLeft = parser.ParseFXParticleSystemTemplateReference() },
            { "TreadDebrisRight", (parser, x) => x.TreadDebrisRight = parser.ParseFXParticleSystemTemplateReference() },

            { "TreadAnimationRate", (parser, x) => x.TreadAnimationRate = parser.ParseFloat() },
            { "TreadDriveSpeedFraction", (parser, x) => x.TreadDriveSpeedFraction = parser.ParseFloat() },
            { "TreadPivotSpeedFraction", (parser, x) => x.TreadPivotSpeedFraction = parser.ParseFloat() },
        });

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public LazyAssetReference<FXParticleSystemTemplate> TreadDebrisLeft { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public LazyAssetReference<FXParticleSystemTemplate> TreadDebrisRight { get; private set; }

    /// <summary>
    /// How fast to move the tread texture, in texture units per second.
    /// </summary>
    public float TreadAnimationRate { get; private set; }

    public float TreadDriveSpeedFraction { get; private set; }
    public float TreadPivotSpeedFraction { get; private set; }

    internal override DrawModule CreateDrawModule(Drawable drawable, IGameEngine gameEngine)
    {
        return new W3dTankDraw(this, drawable, gameEngine);
    }
}
