#nullable enable annotations
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
using Panopticon.Graphics.Shaders;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Terrain;

public sealed class Bridge : DisposableBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private GameObject _bridgeObject;
    private readonly Model _model;
    private readonly ModelInstance _modelInstance;
    private List<Tuple<ModelSubObject, Matrix4x4>> _meshes;
    private readonly List<Tuple<ModelSubObject, Matrix4x4>> _authoredPieces;
    private Vector3 _startPositionWithHeight;
    private Vector3 _endPositionWithHeight;
    private readonly IGameEngine _gameEngine;
    private readonly MapObject _mapObject;
    private readonly BridgeTemplate _template;
    private readonly Vector3 _startPosition;
    private readonly Vector3 _endPosition;
    private readonly bool _canPlace;
    private bool _placed;
    private readonly AssetLoadContext _assetLoadContext;
    private readonly bool _isGeneratedBridge;
    private readonly ImportedModel _importedModel;
    private readonly Dictionary<ModelSubObject, ImportedMesh> _importedMeshes;
    private readonly float? _intrinsicGradeRadians;
    private readonly string? _geometryFingerprint;
    private readonly string? _typeName;
    private readonly float _bridgeScale;

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
        _typeName = mapObject.TypeName;
        _bridgeScale = template.BridgeScale;

        _assetLoadContext = gameEngine.AssetLoadContext;

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

                // Supported-asset identity: canonical local-geometry fingerprint over the
                // imported subobjects (the corrective clearance cut supports only the
                // reviewed Concrete/CBBRIDGEST geometry).
                var fingerprintPairs = new List<(string FullName, ImportedMesh Mesh)>();

                for (var index = 0; index < aligned; index++)
                {
                    if (_importedMeshes.GetValueOrDefault(_model.SubObjects[index]) is { } fingerprintMesh)
                    {
                        fingerprintPairs.Add((_importedModel.SubObjects[index].FullName, fingerprintMesh));
                    }
                }

                _geometryFingerprint = BridgeSurface.ComputeGeometryFingerprint(fingerprintPairs);
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, $"Bridge CPU import failed for '{model.Name}'; no pitch correction and no roadway surface.");
                _importedModel = null;
                _importedMeshes = null;
                _intrinsicGradeRadians = null;
            }
        }

        // Authored pieces come from the constructor's pre-change heights, so path decisions read the original
        // placement. No towers exist until a placement runs.
        _authoredPieces = CreateMeshes(
            gameEngine,
            template,
            startPosition,
            endPosition,
            startPositionWithHeight,
            endPositionWithHeight,
            _model,
            _modelInstance,
            placeTowers: false,
            out var startAnchor,
            out var endAnchor);
        _startPositionWithHeight = startAnchor;
        _endPositionWithHeight = endAnchor;

        _gameEngine = gameEngine;
        _mapObject = mapObject;
        _template = template;
        _startPosition = startPosition;
        _endPosition = endPosition;
        _canPlace = true;
    }

    /// <summary>True once <see cref="PlaceAuthored"/> or <see cref="PlaceLevel"/> has run.</summary>
    internal bool IsPlaced => _placed;

    /// <summary>
    /// Lays the bridge out on its authored placement: the generic bridge object, the anchor heights, then the
    /// authored pieces and their towers. A no-op once placed.
    /// </summary>
    internal void PlaceAuthored()
    {
        if (_placed)
        {
            return;
        }

        _placed = true;

        if (!_canPlace)
        {
            return;
        }

        CreateBridgeObject();

        var startPositionWithHeight = _startPosition;
        startPositionWithHeight.Z = _gameEngine.Scene3D.Terrain.HeightMap.GetHeight(_startPosition.X, _startPosition.Y) + 1f;

        var endPositionWithHeight = _endPosition;
        endPositionWithHeight.Z = _gameEngine.Scene3D.Terrain.HeightMap.GetHeight(_endPosition.X, _endPosition.Y) + 1f;

        _meshes = CreateMeshes(
            _gameEngine,
            _template,
            _startPosition,
            _endPosition,
            startPositionWithHeight,
            endPositionWithHeight,
            _model,
            _modelInstance,
            placeTowers: true,
            out var startAnchor,
            out var endAnchor);
        _startPositionWithHeight = startAnchor;
        _endPositionWithHeight = endAnchor;
    }

    /// <summary>
    /// Lays the bridge out on a level deck: the deck model is built from the layout's triangles, the route
    /// endpoints sit at the layout datum, and the towers stand at the deck ends. A no-op once placed.
    /// </summary>
    internal void PlaceLevel(GeneratedBridgeLayout layout, GeneratedBridgeCorridorPlan corridorPlan)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(corridorPlan);

        if (_placed)
        {
            return;
        }

        _placed = true;

        if (!_canPlace)
        {
            return;
        }

        CreateBridgeObject();

        var route = layout.Route;
        var datumZ = (float)layout.DatumZ;
        _startPositionWithHeight = new Vector3(route.Start.X, route.Start.Y, datumZ);
        _endPositionWithHeight = new Vector3(route.End.X, route.End.Y, datumZ);
        LevelLayout = layout;
        CorridorPlan = corridorPlan;
        _meshes = new List<Tuple<ModelSubObject, Matrix4x4>>();
        DeckModel = AddDisposable(GeneratedBridgeDeckModel.Create(layout, PieceSourceMeshes, _assetLoadContext));

        var along = route.Along;
        var levelRotation = QuaternionUtility.CreateRotation(Vector3.UnitX, new Vector3(along.X, along.Y, 0f));
        var levelWorldMatrix = Matrix4x4.CreateFromQuaternion(levelRotation) * Matrix4x4.CreateTranslation(_startPositionWithHeight);

        var lateral = layout.Faces
            .SelectMany(face => new[] { face.A, face.B, face.C })
            .Select(vertex => route.Project(new Vector2(vertex.X, vertex.Y)).N)
            .ToList();

        // The level rotation sends local +Y to the route's negative lateral axis, so the tower lateral
        // range is the negated route range.
        new BridgeTowers(
            _template,
            _bridgeObject.Owner,
            _gameEngine,
            levelWorldMatrix,
            0f,
            (float)-lateral.Max(),
            (float)route.Length,
            (float)-lateral.Min(),
            levelRotation);
    }

    /// <summary>The level layout this bridge was placed on, or null for an authored placement.</summary>
    internal GeneratedBridgeLayout LevelLayout { get; private set; }

    /// <summary>The corridor plan of a level placement (approaches, benches and under-deck cut), or null otherwise.</summary>
    internal GeneratedBridgeCorridorPlan CorridorPlan { get; private set; }

    /// <summary>Route start and end in XY, as the map places them.</summary>
    internal Vector2 StartPoint => new(_startPosition.X, _startPosition.Y);

    internal Vector2 EndPoint => new(_endPosition.X, _endPosition.Y);

    internal MapObject MapObject => _mapObject;

    internal string TypeName => _typeName;

    /// <summary>
    /// True when the map object carries the path handoff property: the bridge is placed only after its path is
    /// decided, so the decision reads the pre-change heights.
    /// </summary>
    internal bool HasPathHandoff => _isGeneratedBridge && GeneratedBridgePathHandoff.IsPresent(_mapObject);

    /// <summary>
    /// Falsification toggle for the generated-bridge level path: false sends every marked bridge down the
    /// authored fallback so tests can compare the two paths. Test-only; always true in ships.
    /// </summary>
    internal static bool GeneratedPathPlanEnabled = true;

    /// <summary>Rendered level deck of a level placement, or null otherwise.</summary>
    internal GeneratedBridgeDeckModel DeckModel { get; private set; }

    /// <summary>
    /// Imported meshes of the BRIDGE_LEFT, BRIDGE_SPAN and BRIDGE_RIGHT sub-objects keyed by full name;
    /// empty when the CPU import did not run.
    /// </summary>
    internal IReadOnlyDictionary<string, ImportedMesh> PieceSourceMeshes
    {
        get
        {
            var meshes = new Dictionary<string, ImportedMesh>();

            if (_model == null || _importedMeshes == null)
            {
                return meshes;
            }

            foreach (var token in new[] { "BRIDGE_LEFT", "BRIDGE_SPAN", "BRIDGE_RIGHT" })
            {
                var subObject = _model.SubObjects.First(candidate => candidate.Name.Contains(token));

                if (_importedMeshes.GetValueOrDefault(subObject) is { } mesh)
                {
                    meshes[subObject.FullName] = mesh;
                }
            }

            return meshes;
        }
    }

    private void CreateBridgeObject()
    {
        // TODO: Cache this.
        var genericBridgeDefinition = _gameEngine.AssetLoadContext.AssetStore.ObjectDefinitions.GetByName("GenericBridge");
        _bridgeObject = _gameEngine.GameLogic.CreateObject(genericBridgeDefinition, null);
        _bridgeObject.SetMapObjectProperties(_mapObject);
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

    /// <summary>Canonical local-geometry fingerprint of the imported model, or null when
    /// the CPU import failed (r3-g8 corrective supported-asset gate).</summary>
    internal string? GeometryFingerprint => _geometryFingerprint;

    /// <summary>World placement matrix of the laid-out pieces, set by <see cref="CreateMeshes"/>.</summary>
    internal Matrix4x4 AuthoredWorldMatrix { get; private set; }

    /// <summary>
    /// All rendered world triangles of the laid-out bridge with their source metadata
    /// (piece name, copy index, source face index, mesh part/material index) — unclassified;
    /// the clearance classifier and the test oracle independently reduce these to roadway
    /// membership (r3-g8 corrective).
    /// </summary>
    internal IReadOnlyList<BridgeSurface.BridgeRawFace> GetRawWorldFaces()
    {
        return RawFacesOf(_meshes);
    }

    private IReadOnlyList<BridgeSurface.BridgeRawFace> RawFacesOf(IEnumerable<Tuple<ModelSubObject, Matrix4x4>> pieces)
    {
        var faces = new List<BridgeSurface.BridgeRawFace>();
        var copyCounter = new Dictionary<string, int>();

        if (pieces == null)
        {
            return faces;
        }

        foreach (var piece in pieces)
        {
            if (_importedMeshes.GetValueOrDefault(piece.Item1) is not { } mesh)
            {
                continue;
            }

            var pieceName = piece.Item1.FullName;
            var copyIndex = copyCounter.GetValueOrDefault(pieceName);
            copyCounter[pieceName] = copyIndex + 1;

            for (var face = 0; face + 2 < mesh.Indices.Count; face += 3)
            {
                var a = Vector3.Transform(mesh.Vertices[mesh.Indices[face]].Position, piece.Item2);
                var b = Vector3.Transform(mesh.Vertices[mesh.Indices[face + 1]].Position, piece.Item2);
                var c = Vector3.Transform(mesh.Vertices[mesh.Indices[face + 2]].Position, piece.Item2);

                var partIndex = 0;

                for (var p = 0; p < mesh.MeshParts.Count; p++)
                {
                    if (face >= mesh.MeshParts[p].StartIndex && face < mesh.MeshParts[p].StartIndex + mesh.MeshParts[p].IndexCount)
                    {
                        partIndex = p;
                        break;
                    }
                }

                var materialIndex = mesh.MeshParts.Count > 0 ? mesh.MeshParts[partIndex].MaterialIndex : 0;

                faces.Add(new BridgeSurface.BridgeRawFace(pieceName, copyIndex, face / 3, partIndex, materialIndex, a, b, c));
            }
        }

        return faces;
    }

    /// <summary>
    /// Layout input of this bridge over the route from start to end (route-local builder input), or null
    /// when the bridge has no CPU-imported roadway model or the route is invalid.
    /// </summary>
    internal GeneratedBridgeLayoutInput CreateLayoutInput(in Vector2 start, in Vector2 end, double datumZ)
    {
        if (_model == null || _modelInstance == null || _importedModel == null || _importedMeshes == null)
        {
            return null;
        }

        var route = new BridgeRoute(start, end);

        if (!route.IsValid)
        {
            return null;
        }

        var left = _model.SubObjects.First(subObject => subObject.Name.Contains("BRIDGE_LEFT"));
        var span = _model.SubObjects.First(subObject => subObject.Name.Contains("BRIDGE_SPAN"));
        var right = _model.SubObjects.First(subObject => subObject.Name.Contains("BRIDGE_RIGHT"));
        var sourceMeshes = PieceSourceMeshes;

        if (sourceMeshes.Count != 3)
        {
            return null;
        }

        // One matrix per raw face, in the same piece and triangle order as the authored raw faces.
        var pieceMatrices = new List<Matrix4x4>();

        foreach (var piece in _authoredPieces)
        {
            if (_importedMeshes.GetValueOrDefault(piece.Item1) is not { } pieceMesh)
            {
                continue;
            }

            for (var face = 0; face < pieceMesh.Indices.Count / 3; face++)
            {
                pieceMatrices.Add(piece.Item2);
            }
        }

        return new GeneratedBridgeLayoutInput
        {
            Route = route,
            DatumZ = datumZ,
            BridgeScale = _bridgeScale,
            AuthoredWorldMatrix = AuthoredWorldMatrix,
            RawFaces = RawFacesOf(_authoredPieces),
            PieceMatrices = pieceMatrices,
            SourceMeshes = sourceMeshes,
            LeftPieceName = left.FullName,
            SpanPieceName = span.FullName,
            RightPieceName = right.FullName,
            LengthLeft = MeasurePieceLength(left),
            LengthSpan = MeasurePieceLength(span),
            LengthRight = MeasurePieceLength(right),
        };
    }

    /// <summary>
    /// Level deck of this generated bridge over the route from start to end, or null with the reason
    /// the layout is unsupported.
    /// </summary>
    internal GeneratedBridgeLayout TryBuildGeneratedLayout(in Vector2 start, in Vector2 end, double datumZ, out string unsupportedReason)
    {
        var input = CreateLayoutInput(start, end, datumZ);

        if (input == null)
        {
            unsupportedReason = "bridge has no CPU-imported roadway model or the route is invalid";
            return null;
        }

        return GeneratedBridgeLayoutBuilder.TryBuild(input, out unsupportedReason);
    }

    /// <summary>Bone-transformed bounding-box length of one piece in route units (as <c>CreateMeshes</c> measures it).</summary>
    private double MeasurePieceLength(ModelSubObject subObject)
    {
        var transformedBounds = AxisAlignedBoundingBox.Transform(subObject.RenderObject.BoundingBox, _modelInstance.RelativeBoneTransforms[subObject.Bone.Index]);
        return (transformedBounds.Max.X - transformedBounds.Min.X) * _bridgeScale;
    }

    /// <summary>
    /// Supported-asset clearance gate (r3-g8 corrective): generated provenance, the
    /// supported template/model fingerprint, and a complete face classification are all
    /// required; otherwise the bridge is unsupported and no clearance plan exists
    /// (normal loading/movement unchanged). The plan is built against the passed
    /// pre-cut height map; callers collect every plan before applying any.
    /// </summary>
    internal BridgeSurface.TerrainClearancePlan? TryPlanClearance(HeightMap heightMap)
    {
        if (!_isGeneratedBridge)
        {
            return null;
        }

        // The level corridor plan already carries the under-deck cut.
        if (LevelLayout != null)
        {
            return null;
        }

        if (!string.Equals(_typeName, BridgeSurface.SupportedTemplateName, StringComparison.OrdinalIgnoreCase)
            || !BridgeSurface.IsSupportedGeometry(_geometryFingerprint))
        {
            Logger.Warn(
                $"Generated bridge '{_model?.Name}': unsupported asset/geometry (template '{_typeName ?? "none"}', fingerprint {_geometryFingerprint ?? "none"}); terrain clearance cut skipped.");
            return null;
        }

        var rawFaces = GetRawWorldFaces();
        var classification = BridgeSurface.ClassifyRawFaces(rawFaces, _startPositionWithHeight, _endPositionWithHeight);

        if (!classification.Supported)
        {
            Logger.Warn(
                $"Generated bridge '{_model?.Name}': face classification incomplete ({classification.CandidateFaceCount} candidates, "
                + $"{classification.UnclassifiedCount} unclassified); terrain clearance cut skipped.");
            return null;
        }

        var plan = BridgeSurface.TryBuildEnvelopePlan(classification.RoadwayFaces, heightMap, out var unsupportedReason);

        if (plan == null)
        {
            Logger.Warn($"Generated bridge '{_model?.Name}': clearance plan unsupported ({unsupportedReason}); terrain clearance cut skipped.");
            return null;
        }

        return plan;
    }

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
        if (LevelLayout != null)
        {
            return BridgeSurface.CreateFromTriangles(LevelLayout.Roadway.Select(face => (face.A, face.B, face.C)));
        }

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
        bool placeTowers,
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
        AuthoredWorldMatrix = worldMatrix;

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

        if (placeTowers)
        {
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
        }

        startAnchor = startPositionWithHeight;
        endAnchor = endPositionWithHeight;

        return meshes;
    }

    internal void BuildRenderList(RenderList renderList, Camera camera)
    {
        if (DeckModel != null)
        {
            DeckModel.Instance.BuildRenderList(
                renderList,
                camera,
                castsShadow: true,
                new MeshShaderResources.RenderItemConstantsPS
                {
                    HouseColor = Vector3.One,
                    Opacity = 1f,
                    TintColor = Vector3.One,
                });
            return;
        }

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
