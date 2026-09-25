using System.Collections.Generic;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Graphics.Rendering;
using Panopticon.Mathematics;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal sealed class GlobalShaderResourceData : DisposableBase
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly GlobalShaderResources _globalShaderResources;
    private readonly StandardGraphicsResources _standardGraphicsResources;

    // One constants buffer per pass per frame rather than one rewritten between passes: on Vulkan a
    // second write to a uniform buffer is not guaranteed to be visible to draws recorded after it in
    // the same command list, so every pass ended up rendering with the first pass's matrix (the
    // shadow map's light-space view-projection), which is why the scene ignored the camera entirely.
    private readonly List<GlobalConstantsSlot> _globalConstantSlots = new();
    private int _currentSlot = -1;
    private int _nextSlot;

    private readonly ConstantBuffer<GlobalShaderResources.LightingConstantsVS> _globalLightingBufferVS;
    private readonly ConstantBuffer<GlobalShaderResources.LightingConstantsPS> _globalLightingBufferPS;

    /// <summary>
    /// Resource set holding the constants written by the most recent
    /// <see cref="UpdateGlobalConstantBuffers"/> call. Each pass gets its own, so recording a later
    /// pass never disturbs the draws already recorded for an earlier one.
    /// </summary>
    public ResourceSet GlobalConstantsResourceSet => _globalConstantSlots[_currentSlot < 0 ? AcquireSlot() : _currentSlot].ResourceSet;

    private ResourceSet _forwardPassResourceSet;
    private Texture _cachedCloudTexture;
    private Texture _cachedShadowMap;

    private TimeOfDay? _cachedTimeOfDay;

    public GlobalShaderResourceData(
        GraphicsDevice graphicsDevice,
        GlobalShaderResources globalShaderResources,
        StandardGraphicsResources standardGraphicsResources)
    {
        _graphicsDevice = graphicsDevice;
        _globalShaderResources = globalShaderResources;
        _standardGraphicsResources = standardGraphicsResources;

        _globalLightingBufferVS = AddDisposable(new ConstantBuffer<GlobalShaderResources.LightingConstantsVS>(graphicsDevice, "GlobalLightingConstantsVS"));
        SetGlobalLightingBufferVS(graphicsDevice);

        _globalLightingBufferPS = AddDisposable(new ConstantBuffer<GlobalShaderResources.LightingConstantsPS>(graphicsDevice, "GlobalLightingConstantsPS"));
    }

    /// <summary>
    /// Starts a new frame's constants: subsequent passes acquire slots from the start of the pool
    /// again, so the pool stays as small as the busiest frame.
    /// </summary>
    public void BeginFrame()
    {
        _nextSlot = 0;
        _currentSlot = -1;
    }

    private int AcquireSlot()
    {
        if (_nextSlot == _globalConstantSlots.Count)
        {
            var buffer = AddDisposable(new ConstantBuffer<GlobalShaderResources.GlobalConstants>(
                _graphicsDevice,
                $"GlobalConstants{_nextSlot}"));
            var resourceSet = AddDisposable(_graphicsDevice.ResourceFactory.CreateResourceSet(
                new ResourceSetDescription(_globalShaderResources.GlobalConstantsResourceLayout, buffer.Buffer)));
            _globalConstantSlots.Add(new GlobalConstantsSlot(buffer, resourceSet));
        }

        _currentSlot = _nextSlot++;
        return _currentSlot;
    }

    public ResourceSet GetForwardPassResourceSet(
        Texture cloudTexture,
        ConstantBuffer<GlobalShaderResources.ShadowConstantsPS> shadowConstantsPSBuffer,
        Texture shadowMap)
    {
        if (_cachedCloudTexture != cloudTexture || shadowMap != _cachedShadowMap)
        {
            RemoveAndDispose(ref _forwardPassResourceSet);
            _cachedCloudTexture = cloudTexture;
            _cachedShadowMap = shadowMap;

            _forwardPassResourceSet = AddDisposable(
                _graphicsDevice.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(
                        _globalShaderResources.ForwardPassResourceLayout,
                        _globalLightingBufferVS.Buffer,
                        _globalLightingBufferPS.Buffer,
                        cloudTexture,
                        _graphicsDevice.Aniso4xSampler,
                        shadowConstantsPSBuffer.Buffer,
                        shadowMap,
                        _globalShaderResources.ShadowSampler,
                        _globalShaderResources.RadiusCursorDecals.TextureArray,
                        _standardGraphicsResources.Aniso4xClampSampler,
                        _globalShaderResources.RadiusCursorDecals.DecalConstants,
                        _globalShaderResources.RadiusCursorDecals.DecalsBuffer)));
        }

        return _forwardPassResourceSet;
    }

    private void SetGlobalLightingBufferVS(GraphicsDevice graphicsDevice)
    {
        var cloudShadowView = Matrix4x4.CreateLookAt(
            Vector3.Zero,
            Vector3.Normalize(new Vector3(0, 0.2f, -1)),
            Vector3.UnitY);

        var cloudShadowProjection = Matrix4x4.CreateOrthographic(1, 1, 0, 1);

        var lightingConstantsVS = new GlobalShaderResources.LightingConstantsVS
        {
            CloudShadowMatrix = cloudShadowView * cloudShadowProjection
        };

        _globalLightingBufferVS.Value = lightingConstantsVS;
        _globalLightingBufferVS.Update(graphicsDevice);
    }

    public void UpdateGlobalConstantBuffers(
        CommandList commandList,
        RenderContext context,
        in Matrix4x4 viewProjection,
        in Vector4? clippingPlane1,
        in Vector4? clippingPlane2)
    {
        var slot = _globalConstantSlots[AcquireSlot()];
        var constants = slot.Buffer;

        constants.Value.CameraPosition = Matrix4x4Utility.Invert(context.Scene3D.Camera.View).Translation;

        constants.Value.TimeInSeconds = (float)context.GameTime.TotalTime.TotalSeconds;

        constants.Value.ViewProjection = viewProjection;

        constants.Value.ClippingPlane1 = clippingPlane1 ?? Vector4.Zero;
        constants.Value.ClippingPlane2 = clippingPlane2 ?? Vector4.Zero;

        constants.Value.HasClippingPlane1 = clippingPlane1 != null;
        constants.Value.HasClippingPlane2 = clippingPlane2 != null;

        constants.Value.ViewportSize = new Vector2(context.RenderTarget.Width, context.RenderTarget.Height);

        constants.Update(commandList);

        if (_cachedTimeOfDay != context.Scene3D.Lighting.TimeOfDay)
        {
            _globalLightingBufferPS.Value = context.Scene3D.Lighting.CurrentLightingConfiguration.LightsPS;
            _globalLightingBufferPS.Update(commandList);

            _cachedTimeOfDay = context.Scene3D.Lighting.TimeOfDay;
        }
    }

    private readonly record struct GlobalConstantsSlot(
        ConstantBuffer<GlobalShaderResources.GlobalConstants> Buffer,
        ResourceSet ResourceSet);
}
