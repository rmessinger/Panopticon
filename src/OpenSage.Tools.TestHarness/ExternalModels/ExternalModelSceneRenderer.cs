#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using OpenSage.Graphics.Animation;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Settings;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.TestHarness.ExternalModels;

/// <summary>
/// C5-08 render stage: one deterministic frame at the requested animation sample time with
/// the request's preview corrections applied after import, plus atomic PNG publication.
/// Report-only requests complete without allocating a render target or writing an image.
/// </summary>
internal sealed class ExternalModelSceneRenderer : IExternalModelRenderStage
{
    public ExternalModelRenderOutcome Render(ExternalModelRenderInputs inputs)
    {
        if (inputs.Request.OutputImagePath is not { } imagePath)
        {
            return new ExternalModelRenderOutcome(true, [], PngByteLength: null, PngContentHash: null);
        }

        var request = inputs.Request;
        var viewport = new OpenSage.Mathematics.Size(request.Viewport.Width, request.Viewport.Height);
        var worldMatrix = ComputeWorldMatrix(request.PreviewOptions);
        var camera = new StandaloneCaptureCamera(
            request.PreviewOptions.CameraOrbit,
            request.PreviewOptions.CameraElevation,
            request.PreviewOptions.CameraDistance,
            request.PreviewOptions.CameraFieldOfView);
        var options = new StandaloneCaptureOptions(
            Camera: camera,
            Lighting: CreateLighting(request.PreviewOptions.Lighting),
            Background: CreateBackground(request.PreviewOptions.Background));

        Action<OpenSage.Graphics.ModelInstance>? configureInstance = inputs.Animation is null
            ? null
            : instance => PlayToTime(instance, inputs.Animation, request.PreviewOptions.AnimationSampleTime);

        var warmUpPath = Path.Combine(Path.GetTempPath(), "external-model-warmup-" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            // First-execution pipeline uploads are not byte-stable; discard one identical
            // frame so the published capture is reproducible.
            StandaloneModelCapture.Capture(
                inputs.Game,
                inputs.RuntimeModel,
                inputs.LoadContext,
                warmUpPath,
                viewport,
                configureInstance,
                worldMatrix,
                options);

            ExternalModelArtifactWriter.Publish(imagePath, stagingPath =>
                StandaloneModelCapture.Capture(
                    inputs.Game,
                    inputs.RuntimeModel,
                    inputs.LoadContext,
                    stagingPath,
                    viewport,
                    configureInstance,
                    worldMatrix,
                    options));
        }
        finally
        {
            try
            {
                File.Delete(warmUpPath);
            }
            catch (IOException)
            {
            }
        }

        var byteLength = new FileInfo(imagePath).Length;
        var contentHash = ExternalModelArtifactWriter.ComputeFileHash(imagePath);

        return new ExternalModelRenderOutcome(true, [], byteLength, contentHash);
    }

    internal static Matrix4x4 ComputeWorldMatrix(ExternalModelPreviewOptions preview)
    {
        // Post-import preview transform only: converted vertices, skeleton data, and the
        // C4 basis are never modified.
        return Matrix4x4.CreateRotationZ(preview.ModelYaw) *
            Matrix4x4.CreateRotationX(preview.ModelPitch) *
            Matrix4x4.CreateRotationY(preview.ModelRoll) *
            Matrix4x4.CreateTranslation(preview.ModelTranslation);
    }

    internal static void PlayToTime(
        OpenSage.Graphics.ModelInstance instance,
        ModelAnimation animation,
        float seconds)
    {
        var animationInstance = instance.CreateAnimationInstance(
            animation,
            AnimationMode.Once,
            AnimationFlags.StartFrameFirst,
            gameObject: null!,
            random: new ConstantRandom());

        animationInstance.Play();

        const int steps = 8;
        var delta = TimeSpan.FromSeconds(seconds / steps);
        for (var i = 0; i < steps; i++)
        {
            animationInstance.Update(new OpenSage.TimeInterval(TimeSpan.Zero, delta));
        }
    }

    private static OpenSage.Settings.WorldLighting? CreateLighting(ExternalPreviewLighting lighting)
    {
        if (lighting == ExternalPreviewLighting.Neutral)
        {
            return null;
        }

        var keyLight = new OpenSage.Graphics.Shaders.GlobalShaderResources.Light
        {
            Ambient = new Vector3(0.45f, 0.45f, 0.45f),
            Direction = Vector3.Normalize(new Vector3(-0.3f, 0.2f, -0.8f)),
            Color = new Vector3(0.9f, 0.9f, 0.95f),
        };
        var fillLight = new OpenSage.Graphics.Shaders.GlobalShaderResources.LightingConfiguration
        {
            Light0 = keyLight,
            Light1 = new OpenSage.Graphics.Shaders.GlobalShaderResources.Light
            {
                Ambient = new Vector3(0.2f, 0.2f, 0.2f),
                Direction = Vector3.Normalize(new Vector3(0.4f, -0.3f, 0.6f)),
                Color = new Vector3(0.5f, 0.5f, 0.55f),
            },
        };

        var configurations = new Dictionary<OpenSage.Data.Map.TimeOfDay, LightSettings>
        {
            [OpenSage.Data.Map.TimeOfDay.Morning] = new LightSettings(fillLight, fillLight),
        };

        return new OpenSage.Settings.WorldLighting(configurations, OpenSage.Data.Map.TimeOfDay.Morning);
    }

    private static ColorRgbaF? CreateBackground(ExternalPreviewBackground background)
    {
        return background switch
        {
            ExternalPreviewBackground.Dark => new ColorRgbaF(0.08f, 0.08f, 0.1f, 1f),
            _ => (ColorRgbaF?)null,
        };
    }

    private sealed class ConstantRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int lo, int hi) => lo;

        public float NextSingle(float lo, float hi) => lo;
    }
}
