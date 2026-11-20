using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.Content.Loaders;
using Panopticon.Data.Map;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Terrain;

public sealed class Bridge : DisposableBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly GameObject _bridgeObject;
    private readonly Model _model;
    private readonly ModelInstance _modelInstance;
    private readonly List<Tuple<ModelSubObject, Matrix4x4>> _meshes;
    private readonly Vector3 _startPositionWithHeight;
    private readonly Vector3 _endPositionWithHeight;
    private readonly AssetLoadContext _assetLoadContext;
    private readonly bool _isGeneratedBridge;
    private readonly ImportedModel _importedModel;
    private readonly Dictionary<ModelSubObject, ImportedMesh> _importedMeshes;
    private readonly float? _intrinsicGradeRadians;

    internal Bridge(
        IGameEngine gameEngine,
        MapObject mapObject,
        in Vector3 startPosition,
        in Vector3 endPosition)
    {
        var template = gameEngine.AssetLoadContext.AssetStore.BridgeTemplates.GetByName(mapObject.TypeName);
        if (template == null)
        {
            return;
        }

        var model = template.BridgeModelName.Value;
        if (model == null)
        {
            return;
        }

        _isGeneratedBridge = HasGeneratedProvenance(mapObject);

        // TODO: Cache this.
        var genericBridgeDefinition = gameEngine.AssetLoadContext.AssetStore.ObjectDefinitions.GetByName("GenericBridge");

        _assetLoadContext = gameEngine.AssetLoadContext;

        _bridgeObject = gameEngine.GameLogic.CreateObject(genericBridgeDefinition, null);
        _bridgeObject.SetMapObjectProperties(mapObject);

        _model = model;

        _modelInstance = AddDisposable(_model.CreateInstance(gameEngine.AssetLoadContext));

        _modelInstance.Update(TimeInterval.Zero);

        var startPositionWithHeight = startPosition;
        startPositionWithHeight.Z = gameEngine.Scene3D.Terrain.HeightMap.GetHeight(startPosition.X, startPosition.Y) + 1f;

        var endPositionWithHeight = endPosition;
        endPositionWithHeight.Z = gameEngine.Scene3D.Terrain.HeightMap.GetHeight(endPosition.X, endPosition.Y) + 1f;

        // Generated bridges import the CPU mesh once: the roadway pitch measurement and
        // the movement surface both consume the same triangle data (WP1029 rework).
        if (_isGeneratedBridge)
        {
            try
            {
                var importer = new W3dModelImporter(PathResolvers.W3d);
                _importedModel = importer.ImportModel(
                    model.Name,
                    new AssetImportContext(_assetLoadContext.FileSystem, _assetLoadContext.Language));

                if (_importedModel is null)
                {
                    throw new InvalidDataException($"the CPU import returned no model for '{model.Name}'.");
                }

                _importedMeshes = new Dictionary<ModelSubObject, ImportedMesh>();
                var aligned = Math.Min(_importedModel.SubObjects.Count, _model.SubObjects.Length);

                for (var index = 0; index < aligned; index++)
                {
                    if (_importedModel.SubObjects[index].RenderObject is ImportedMesh importedMesh)
                    {
                        _importedMeshes[_model.SubObjects[index]] = importedMesh;
                    }
                }

                // The authored roadway pitch the placement transform must not double-apply:
                // measured from the laid-out roadway at zero Y-rotation (median per-triangle
                // slope along the span axis), before the world matrix exists.
                var left = _model.SubObjects.First(subObject => subObject.Name.Contains("BRIDGE_LEFT"));
                var span = _model.SubObjects.First(subObject => subObject.Name.Contains("BRIDGE_SPAN"));
                var right = _model.SubObjects.First(subObject => subObject.Name.Contains("BRIDGE_RIGHT"));
                var pieces = new[]
                {
                    (left, _importedMeshes.GetValueOrDefault(left)),
                    (span, _importedMeshes.GetValueOrDefault(span)),
                    (right, _importedMeshes.GetValueOrDefault(right)),
                };
                Func<ModelSubObject, Matrix4x4> scaledBone = subObject =>
                    Matrix4x4.CreateScale(template.BridgeScale) * _modelInstance.RelativeBoneTransforms[subObject.Bone.Index];
                _intrinsicGradeRadians = BridgeSurface.MeasureIntrinsicGradeRadians(
                    pieces,
                    scaledBone,
                    startPositionWithHeight,
                    endPositionWithHeight);

                if (_intrinsicGradeRadians is null)
                {
                    Logger.Warn($"Bridge '{model.Name}': the authored roadway grade could not be measured; placement pitch correction skipped.");
                }
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, $"Bridge CPU import failed for '{model.Name}'; no pitch correction and no roadway surface.");
                _importedModel = null;
                _importedMeshes = null;
                _intrinsicGradeRadians = null;
            }
        }

        _meshes = CreateMeshes(gameEngine,
            template,
            startPosition,
            endPosition,
            startPositionWithHeight,
            endPositionWithHeight,
            _model,
            _modelInstance,
            out _startPositionWithHeight,
            out _endPositionWithHeight);
    }

    /// <summary>
    /// World-transformed mesh pieces of this bridge in layout order (left, spans, right).
    /// Exposed for the roadway surface extraction; diagnostics read the same pairs.
    /// </summary>
    internal IReadOnlyList<Tuple<ModelSubObject, Matrix4x4>> Meshes => _meshes;

    internal Vector3 StartPositionWithHeight => _startPositionWithHeight;

    internal Vector3 EndPositionWithHeight => _endPositionWithHeight;

    /// <summary>
    /// True when the originating MapObject carries the generated-bridge provenance
    /// property the package converter writes; only generated bridges register roadway
    /// surfaces, so legacy maps keep raw ground behavior everywhere.
    /// </summary>
    internal bool IsGeneratedBridge => _isGeneratedBridge;

    /// <summary>
    /// The measured authored roadway pitch (radians) this generated bridge's placement
    /// correction cancels, or null when no measurement was possible (WP1029 rework).
    /// </summary>
    internal float? IntrinsicGradeRadians => _intrinsicGradeRadians;

    /// <summary>
    /// Origin marker consumed by the runtime bridge surface scoping: the package
    /// converter writes this property onto generated bridge PlacedObjects,
    /// RuntimeBridge copies it onto the MapObject, and Scene3D registers roadway
    /// surfaces only for marked bridges.
    /// </summary>
    public const string GeneratedProvenanceKey = "generated";

    public const string GeneratedProvenanceValue = "bridge";


    /// <summary>
    /// Falsification toggle for the generated-bridge pitch correction (WP1029 rework):
    /// false restores the uncorrected placement (deck pitch doubled) so tests can prove
    /// the correction is what removes the north-exit gap. Test-only; always true in ships.
    /// </summary>
    internal static bool GeneratedBridgePitchCorrectionEnabled = true;

    /// <summary>
    /// The generated-bridge provenance check: the MapObject must carry the
    /// "generated" → "bridge" property the package converter writes onto bridge
    /// PlacedObjects. Legacy bridge MapObjects have no such property.
    /// </summary>
    internal static bool HasGeneratedProvenance(MapObject mapObject)
    {
        return mapObject.Properties.GetPropOrNull(GeneratedProvenanceKey)?.GetAsciiString() == GeneratedProvenanceValue;
    }

    /// <summary>
    /// Extracts the drivable roadway triangles of this bridge into a queryable surface, or
    /// null when the model has no CPU-importable roadway (imports are best-effort; bridge
    /// traversal then keeps the raw terrain behavior).
    /// </summary>
    internal BridgeSurface CreateSurface()
    {
        if (_meshes is null || _model is null || _importedModel is null || _importedMeshes is null)
        {
            return null;
        }

        // The materializer preserves imported sub-object order, so the imported meshes
        // align with Model.SubObjects by index; multiple laid-out pieces share one mesh.
        // The import itself was performed in the constructor (shared with the roadway
        // pitch measurement).
        return BridgeSurface.Create(_meshes, subObject => _importedMeshes.GetValueOrDefault(subObject), _startPositionWithHeight, _endPositionWithHeight);
    }

    private List<Tuple<ModelSubObject, Matrix4x4>> CreateMeshes(
        IGameEngine gameEngine,
        BridgeTemplate template,
        in Vector3 startPosition,
        in Vector3 endPosition,
        in Vector3 startPositionWithHeight,
        in Vector3 endPositionWithHeight,
        Model model,
        ModelInstance modelInstance,
        out Vector3 startAnchor,
        out Vector3 endAnchor)
    {

        var bridgeLeft = model.SubObjects.First(x => x.Name.Contains("BRIDGE_LEFT"));
        var bridgeSpan = model.SubObjects.First(x => x.Name.Contains("BRIDGE_SPAN"));
        var bridgeRight = model.SubObjects.First(x => x.Name.Contains("BRIDGE_RIGHT"));

        float GetLength(ModelSubObject subObject)
        {
            var transformedBounds = AxisAlignedBoundingBox.Transform(subObject.RenderObject.BoundingBox, modelInstance.RelativeBoneTransforms[subObject.Bone.Index]);
            return transformedBounds.Max.X - transformedBounds.Min.X;
        }

        // See how many spans we can fit in.
        var lengthLeft = GetLength(bridgeLeft) * template.BridgeScale;
        var lengthSpan = GetLength(bridgeSpan) * template.BridgeScale;
        var lengthRight = GetLength(bridgeRight) * template.BridgeScale;


        var horizontalDistance = Vector3.Distance(startPosition, endPosition);

        var distance = Vector3.Distance(startPositionWithHeight, endPositionWithHeight);

        var spanLength = distance - lengthLeft - lengthRight;

        // There are zero or more spans in the bridge, as space permits.
        var numSpans = (int)(spanLength / lengthSpan);

        var totalLength = lengthLeft + (lengthSpan * numSpans) + lengthRight;
        var scaleX = distance / totalLength;

        var rotationAroundZ = QuaternionUtility.CreateRotation(Vector3.UnitX, Vector3.Normalize(endPosition - startPosition));

        // Generated bridges (WP1029 rework): the axis alignment (rotationAroundZ) already
        // pitches the deck to the site slope - it maps +X onto the anchor direction
        // INCLUDING the height difference. The legacy Y-rotation then adds the site pitch
        // a second time in this engine's row-vector transform convention (measured on the
        // 700-unit fixture: the deck slope doubles from 0.050 to 0.100, floating the north
        // landing ~35 units above its approach). Generated bridges therefore take NO
        // additional Y pitch: their deck pitch equals the anchor-direction pitch, which
        // equals the terrain slope between the endpoints. Legacy bridges keep the
        // unmodified placement.
        var rotationAroundY = Matrix4x4.Identity;

        if (!_isGeneratedBridge || !GeneratedBridgePitchCorrectionEnabled)
        {
            rotationAroundY = Matrix4x4.CreateRotationY(-MathF.Atan((endPositionWithHeight.Z - startPositionWithHeight.Z) / horizontalDistance));
        }

        var worldMatrix =
            Matrix4x4.CreateScale(scaleX, 1, 1) *
            Matrix4x4.CreateScale(template.BridgeScale) *
            rotationAroundY *
            Matrix4x4.CreateFromQuaternion(rotationAroundZ) *
            Matrix4x4.CreateTranslation(startPositionWithHeight);

        var meshes = new List<Tuple<ModelSubObject, Matrix4x4>>();

        var currentOffset = 0f;
        Matrix4x4 GetLocalTranslation(ModelSubObject subObject)
        {
            var transformedBounds = AxisAlignedBoundingBox.Transform(subObject.RenderObject.BoundingBox, modelInstance.RelativeBoneTransforms[subObject.Bone.Index]);
            var w3dOffset = transformedBounds.Min.X;

            var result =
                modelInstance.RelativeBoneTransforms[subObject.Bone.Index] *
                Matrix4x4.CreateTranslation(new Vector3(currentOffset, 0, 0)) *
                Matrix4x4.CreateTranslation(new Vector3(-w3dOffset, 0, 0)) *
                worldMatrix;

            currentOffset += (transformedBounds.Max.X - transformedBounds.Min.X);

            return result;
        }

        meshes.Add(Tuple.Create(bridgeLeft, GetLocalTranslation(bridgeLeft)));

        for (var i = 0; i < numSpans; i++)
        {
            meshes.Add(Tuple.Create(bridgeSpan, GetLocalTranslation(bridgeSpan)));
        }

        meshes.Add(Tuple.Create(bridgeRight, GetLocalTranslation(bridgeRight)));

        var transformedLeftBounds = AxisAlignedBoundingBox.Transform(bridgeLeft.RenderObject.BoundingBox, modelInstance.RelativeBoneTransforms[bridgeLeft.Bone.Index]);

        new BridgeTowers(
            template,
            _bridgeObject.Owner,
            gameEngine,
            worldMatrix,
            0,
            transformedLeftBounds.Min.Y,
            totalLength / template.BridgeScale,
            transformedLeftBounds.Max.Y,
            rotationAroundZ);

        startAnchor = startPositionWithHeight;
        endAnchor = endPositionWithHeight;

        return meshes;
    }

    internal void BuildRenderList(RenderList renderList, Camera camera)
    {
        if (_model == null)
        {
            return;
        }

        foreach (var meshMatrix in _meshes)
        {
            // TODO: Don't do this.
            var index = Array.IndexOf(_model.SubObjects, meshMatrix.Item1);

            meshMatrix.Item1.RenderObject.BuildRenderListWithWorldMatrix(
                renderList,
                camera,
                _modelInstance,
                _modelInstance.MeshInstances[index],
                meshMatrix.Item1.Bone,
                meshMatrix.Item2,
                true);
        }
    }
}
