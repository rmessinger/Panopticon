using System;
using System.Linq;
using System.Numerics;
using Panopticon.Client;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Panopticon.Utilities;
using Xunit;

namespace Panopticon.Tests.Graphics.Animation;

// WI-C2-06: locks the C2 retargeting contract through the gameplay activation path. Unit coverage
// for missing/duplicate named targets, out-of-range direct-index omission, and per-hierarchy
// binding cache lives in ModelAnimationTests; this suite proves the end-to-end route:
// AnimationState + W3dModelDraw.UpdateConditionState -> ModelInstance.CreateAnimationInstance ->
// ModelAnimation.Bind -> AnimationInstance playback on the intended bones.

[Collection("SerialGameTests")]
public sealed class ModelAnimationBindingTests : IDisposable
{
    private Game? _game;

    [GameFact(SageGame.CncGenerals)]
    public void NamedAnimation_DrivesIntendedBones_ThroughGameplayActivation()
    {
        // Models materialize GPU resources; skip silently on hosts without a headless backend.
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var game = _game = CreateGame();
        var scene = game.Scene3D;

        var comancheDef = game.AssetStore.ObjectDefinitions.GetByName("AmericaVehicleComanche")
            ?? throw new InvalidOperationException("comanche not found");
        var comanche = scene.GameObjects.CreateObject(comancheDef, scene.Players[0]);

        Assert.IsAssignableFrom<Drawable>(comanche.Client);
        var drawable = (Drawable)comanche.Client!;
        W3dModelDraw? draw = null;
        foreach (var module in drawable.DrawModules)
        {
            if (module is W3dModelDraw modelDraw)
            {
                draw = modelDraw;
            }
        }

        Assert.NotNull(draw);

        // Query the real hierarchy so the named targets provably differ from bone order.
        var (probeInstance, _) = draw.FindBone("");
        var bones = probeInstance.Model.BoneHierarchy.Bones;
        Assert.True(bones.Length >= 4, $"expected a real skeleton, got {bones.Length} bones");
        var lastBoneName = bones[^1].Name;
        var middleBoneName = bones[bones.Length / 2].Name;
        Assert.False(string.Equals(lastBoneName, middleBoneName, StringComparison.OrdinalIgnoreCase));

        var targetRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var imported = new ImportedAnimation(
            "C2.Retarget",
            TimeSpan.FromSeconds(2.0),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName(lastBoneName),
                    ImportedAnimationChannelType.TranslationX,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.Zero, 0.0f),
                        ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(2.0), 4.0f),
                    }),
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName(middleBoneName),
                    ImportedAnimationChannelType.Quaternion,
                    new[]
                    {
                        ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity),
                        ImportedKeyframe.FromQuaternion(TimeSpan.FromSeconds(2.0), targetRotation),
                    }),
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName(lastBoneName),
                    ImportedAnimationChannelType.Visibility,
                    new[]
                    {
                        ImportedKeyframe.FromBool(TimeSpan.Zero, true),
                    }),
            });
        var animation = ImportedAnimationMaterializer.Materialize(imported);

        var animationState = new AnimationState();
        animationState.ConditionFlags.Add(new BitArray<ModelConditionFlag>(ModelConditionFlag.Moving));
        animationState.Animations.Add(new AnimationStateAnimation
        {
            Animation = new LazyAssetReference<Panopticon.Graphics.Animation.ModelAnimation>(animation),
            AnimationMode = AnimationMode.Once,
        });

        var moduleData = comancheDef.Draws.Values.Select(d => d.Data).OfType<W3dModelDrawModuleData>().Single();
        moduleData.AnimationStates.Add(animationState);

        draw.UpdateConditionState(new BitArray<ModelConditionFlag>(ModelConditionFlag.Moving), game.GameClient.Random);

        var (modelInstance, lastBone) = draw.FindBone(lastBoneName);
        Assert.NotNull(modelInstance);
        Assert.NotNull(lastBone);
        var lastIndex = lastBone.Index;
        var middleIndex = bones[bones.Length / 2].Index;
        Assert.Equal(bones.Length - 1, lastIndex);

        var instance = modelInstance.AnimationInstances.Single();
        Assert.True(instance.IsPlaying);

        var time = TimeSpan.Zero;
        var step = TimeSpan.FromSeconds(1.0);
        for (var i = 0; i < 3; i++)
        {
            time += step;
            modelInstance.Update(new TimeInterval(time, step));
        }

        Assert.False(instance.IsPlaying);
        var lastBoneInstance = modelInstance.ModelBoneInstances[lastIndex];
        var middleBoneInstance = modelInstance.ModelBoneInstances[middleIndex];
        Assert.Equal(4.0f, lastBoneInstance.AnimatedOffset.Translation.X, new ToleranceEqualityComparer(0.001f));
        Assert.Equal(targetRotation.X, middleBoneInstance.AnimatedOffset.Rotation.X, 5);
        Assert.Equal(targetRotation.Y, middleBoneInstance.AnimatedOffset.Rotation.Y, 5);
        Assert.Equal(targetRotation.Z, middleBoneInstance.AnimatedOffset.Rotation.Z, 5);
        Assert.Equal(targetRotation.W, middleBoneInstance.AnimatedOffset.Rotation.W, 5);
        Assert.True(lastBoneInstance.Visible);
    }

    private Game CreateGame()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

        Platform.Start();
        var game = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);

        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
            new(2, "FactionGLA", new ColorRgb(0, 0, 255), 0, PlayerOwner.EasyAi, "GLA"),
        };
        game.StartSkirmishOrMultiPlayerGame("maps/alpine assault/alpine assault.map", new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        return game;
    }

    public void Dispose()
    {
        _game?.Dispose();
    }
}
