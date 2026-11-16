#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Panopticon.Content.Importing.Maps;
using Panopticon.Data.Map;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>
/// Importer from <see cref="MapInterchange"/> to an H3 map package directory container.
/// Maps every interchange component into package manifest DTOs and staged chunk files,
/// atomic-commits the package through <see cref="MapPackageDirectoryWriter"/>, and
/// preserves authored data faithfully without quantizing, converting, or dropping.
/// </summary>
public sealed class LegacyMapPackageImporter
{
    private readonly MapInterchange _interchange;
    private readonly string _targetPackageRoot;

    public LegacyMapPackageImporter(MapInterchange interchange, string targetPackageRoot)
    {
        ArgumentNullException.ThrowIfNull(interchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPackageRoot);
        _interchange = interchange;
        _targetPackageRoot = targetPackageRoot;
    }

    public MapPackageWriteResult Import() => Import(_interchange, _targetPackageRoot);

    public static MapPackageWriteResult Write(MapInterchange interchange, string targetPackageRoot) =>
        Import(interchange, targetPackageRoot, null, null);

    public static MapPackageWriteResult Write(
        MapInterchange interchange,
        string targetPackageRoot,
        IReadOnlyList<MapPackageCompanionEntry>? companions,
        IReadOnlyDictionary<string, byte[]>? companionPayloads) =>
        Import(interchange, targetPackageRoot, companions, companionPayloads);

    public static MapPackageWriteResult Import(MapInterchange interchange, string targetPackageRoot) =>
        Import(interchange, targetPackageRoot, null, null);

    public static MapPackageWriteResult Import(
        MapInterchange interchange,
        string targetPackageRoot,
        IReadOnlyList<MapPackageCompanionEntry>? companions,
        IReadOnlyDictionary<string, byte[]>? companionPayloads)
    {
        ArgumentNullException.ThrowIfNull(interchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPackageRoot);

        var interchangeDiagnostics = MapInterchangeValidator.Validate(interchange);

        if (interchangeDiagnostics.Count > 0)
        {
            return new MapPackageWriteResult(
                false,
                interchangeDiagnostics.Select(d => new MapPackageDiagnostic(d.Code, d.Message)).ToArray());
        }

        var identity = MapPackageIdentity.FromModel(interchange.Identity);
        var layout = MapPackageTerrainLayout.FromModel(interchange.Layout);
        var blend = MapPackageBlendModel.FromModel(interchange.Blend);
        var objects = interchange.Objects?.Objects.Select(MapPackagePlacedObject.FromModel).ToArray();
        var waypoints = interchange.Waypoints?.Waypoints.Select(MapPackageWaypoint.FromModel).ToArray();
        var provenance = MapPackageProvenance.FromModel(interchange.Provenance);

        var chunkLookup = new Dictionary<(int X, int Y), Chunk>(interchange.Layout.Chunks.Count);

        foreach (var chunk in interchange.Layout.Chunks)
        {
            chunkLookup[(chunk.Key.Cx, chunk.Key.Cy)] = chunk;
        }

        var layers = new List<MapPackageLayerEntry>();

        for (var cy = 0; cy < interchange.Layout.ChunksY; cy++)
        {
            for (var cx = 0; cx < interchange.Layout.ChunksX; cx++)
            {
                var chunk = chunkLookup[(cx, cy)];
                var coreW = Math.Min(MapChunkGeometry.CorePointsPerAxis, interchange.Layout.LatticeWidth - (cx * MapChunkGeometry.IntervalsPerChunkAxis));
                var coreH = Math.Min(MapChunkGeometry.CorePointsPerAxis, interchange.Layout.LatticeHeight - (cy * MapChunkGeometry.IntervalsPerChunkAxis));

                var heightsWidth = coreW + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var heightsHeight = coreH + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var heightsEncoded = MapPackageChunkCodec.EncodeHeights(chunk.Heights.Samples.ToArray());
                var heightsDigest = ComputeSha256Hex(heightsEncoded);

                layers.Add(new MapPackageLayerEntry(
                    cx,
                    cy,
                    MapPackageFormat.LayerKindHeights,
                    $"layers/heights_{cx}_{cy}.bin",
                    MapPackageFormat.EncodingFloat32Le,
                    heightsWidth,
                    heightsHeight,
                    heightsEncoded.Length,
                    heightsDigest));

                if (chunk.Materials is not null)
                {
                    var materialsEncoded = MapPackageChunkCodec.EncodeMaterials(chunk.Materials.Indices.ToArray());
                    var materialsDigest = ComputeSha256Hex(materialsEncoded);

                    layers.Add(new MapPackageLayerEntry(
                        cx,
                        cy,
                        MapPackageFormat.LayerKindMaterials,
                        $"layers/materials_{cx}_{cy}.bin",
                        MapPackageFormat.EncodingUint8,
                        coreW,
                        coreH,
                        materialsEncoded.Length,
                        materialsDigest));
                }

                if (chunk.Masks is not null)
                {
                    var masksEncoded = MapPackageChunkCodec.EncodeMasks(chunk.Masks.Flags.ToArray());
                    var masksDigest = ComputeSha256Hex(masksEncoded);

                    layers.Add(new MapPackageLayerEntry(
                        cx,
                        cy,
                        MapPackageFormat.LayerKindMasks,
                        $"layers/masks_{cx}_{cy}.bin",
                        MapPackageFormat.EncodingUint8,
                        coreW,
                        coreH,
                        masksEncoded.Length,
                        masksDigest));
                }

                if (chunk.Imagery is not null)
                {
                    var imageryEncoded = MapPackageChunkCodec.EncodeImagery(chunk.Imagery.Rgba.ToArray());
                    var imageryDigest = ComputeSha256Hex(imageryEncoded);

                    layers.Add(new MapPackageLayerEntry(
                        cx,
                        cy,
                        MapPackageFormat.LayerKindImagery,
                        $"layers/imagery_{cx}_{cy}.raw",
                        MapPackageFormat.EncodingRgba8,
                        chunk.Imagery.Width,
                        chunk.Imagery.Height,
                        imageryEncoded.Length,
                        imageryDigest));
                }
            }
        }

        var retainedSections = ResolveRetainedSections(interchange);
        var retainedLookup = new Dictionary<string, RetainedSection>(retainedSections.Count, StringComparer.Ordinal);
        var retainedEntries = new List<MapPackageRetainedEntry>(retainedSections.Count);

        foreach (var section in retainedSections)
        {
            retainedLookup[section.SectionName] = section;
            var digest = ComputeSha256Hex(section.Bytes.ToArray());

            retainedEntries.Add(new MapPackageRetainedEntry(
                section.SectionName,
                section.SectionVersion,
                $"retained/{section.SectionName}.bin",
                section.Bytes.Length,
                digest));
        }

        MapPackageVectorLayerEntry? vectorEntry = null;
        byte[]? vectorBytes = null;

        if (interchange.VectorLayers is { Features.Count: > 0 } vectorLayers)
        {
            vectorBytes = MapPackageVectorCodec.Serialize(vectorLayers);
            vectorEntry = new MapPackageVectorLayerEntry(
                MapPackageFormat.VectorLayerPath,
                MapPackageFormat.EncodingJson,
                vectorBytes.Length,
                ComputeSha256Hex(vectorBytes),
                vectorLayers.Features.Count);
        }

        MapPackageBuildingsEntry? buildingsEntry = null;
        byte[]? buildingMetadataBytes = null;
        var buildingMeshBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (interchange.Buildings is { } buildingLayer && (buildingLayer.Buildings.Count > 0 || buildingLayer.MeshChunks.Count > 0))
        {
            try
            {
                buildingMetadataBytes = MapPackageBuildingCodec.SerializeMetadata(buildingLayer.Buildings);
                var meshEntries = new List<MapPackageBuildingMeshEntry>(buildingLayer.MeshChunks.Count);

                foreach (var meshChunk in buildingLayer.MeshChunks)
                {
                    var meshBytes = MapPackageBuildingCodec.EncodeMesh(meshChunk);
                    var meshPath = MapPackageFormat.BuildingMeshPath(meshChunk.ChunkX, meshChunk.ChunkY);
                    buildingMeshBytes[meshPath] = meshBytes;
                    meshEntries.Add(new MapPackageBuildingMeshEntry(
                        meshChunk.ChunkX,
                        meshChunk.ChunkY,
                        meshPath,
                        MapPackageFormat.EncodingPbm1,
                        meshBytes.Length,
                        ComputeSha256Hex(meshBytes),
                        meshChunk.Segments.Count,
                        meshChunk.Segments.Sum(s => s.Vertices.Count),
                        meshChunk.Segments.Sum(s => s.Indices.Count)));
                }

                buildingsEntry = new MapPackageBuildingsEntry(
                    MapPackageFormat.BuildingsMetadataPath,
                    MapPackageFormat.EncodingJson,
                    buildingMetadataBytes.Length,
                    ComputeSha256Hex(buildingMetadataBytes),
                    buildingLayer.Buildings.Count,
                    meshEntries);
            }
            catch (FormatException ex)
            {
                return new MapPackageWriteResult(
                    false,
                    [new MapPackageDiagnostic(MapPackageDiagnostic.BuildingLayer, ex.Message)]);
            }
        }

        var packageVersion = buildingsEntry is not null
            ? MapPackageFormat.BuildingPackageVersion
            : vectorEntry is null ? MapPackageFormat.CurrentPackageVersion : MapPackageFormat.VectorPackageVersion;

        var manifest = new MapPackageManifest(
            MapPackageFormat.PackageFormatName,
            packageVersion,
            interchange.SchemaVersion,
            identity,
            layout,
            blend,
            objects,
            waypoints,
            provenance,
            layers,
            retainedEntries,
            companions,
            vectorEntry,
            buildingsEntry);

        var payloadProvider = new InterchangePayloadProvider(
            chunkLookup,
            retainedLookup,
            companionPayloads,
            vectorBytes,
            buildingMetadataBytes,
            buildingMeshBytes);

        return MapPackageDirectoryWriter.Write(targetPackageRoot, manifest, payloadProvider);
    }

    private static string ComputeSha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // A typed component without a same-name retained section is serialized into one, so the
    // package reader recovers it. Existing retained sections, including legacy-sourced ones,
    // are kept as authored.
    private static IReadOnlyList<RetainedSection> ResolveRetainedSections(MapInterchange interchange)
    {
        var sections = new List<RetainedSection>(interchange.RetainedSections);

        if (interchange.BlendTileData is not null
            && !sections.Exists(section => section.SectionName == BlendTileData.AssetName)
            && RuntimeBridge.MaterializeBlendTileData(interchange) is { } blend)
        {
            sections.Add(Retain(blend, BlendTileData.AssetName, (writer, _) => blend.WriteTo(writer)));
        }

        if (interchange.GlobalLighting is not null
            && !sections.Exists(section => section.SectionName == GlobalLighting.AssetName)
            && RuntimeBridge.MaterializeGlobalLighting(interchange) is { } lighting)
        {
            sections.Add(Retain(lighting, GlobalLighting.AssetName, (writer, _) => lighting.WriteTo(writer)));
        }

        if (interchange.WaypointPaths is not null
            && !sections.Exists(section => section.SectionName == WaypointsList.AssetName)
            && RuntimeBridge.MaterializeWaypointsList(interchange) is { } waypointsList)
        {
            sections.Add(Retain(waypointsList, WaypointsList.AssetName, (writer, _) => waypointsList.WriteTo(writer)));
        }

        return sections;
    }

    /// <summary>Serializes one section the way the legacy writer frames it: a fresh name table, the payload, and the section version.</summary>
    private static RetainedSection Retain(Asset section, string sectionName, Action<BinaryWriter, AssetNameCollection> writeTo)
    {
        using var stream = new MemoryStream();

        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writeTo(writer, new AssetNameCollection());
        }

        var bytes = stream.ToArray();

        return new RetainedSection(sectionName, section.Version, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private sealed class InterchangePayloadProvider : IMapPackagePayloadProvider
    {
        private readonly Dictionary<(int X, int Y), Chunk> _chunks;
        private readonly Dictionary<string, RetainedSection> _retained;
        private readonly IReadOnlyDictionary<string, byte[]>? _companions;
        private readonly byte[]? _vectorLayer;
        private readonly byte[]? _buildingMetadata;
        private readonly IReadOnlyDictionary<string, byte[]> _buildingMeshes;

        public InterchangePayloadProvider(
            Dictionary<(int X, int Y), Chunk> chunks,
            Dictionary<string, RetainedSection> retained,
            IReadOnlyDictionary<string, byte[]>? companions = null,
            byte[]? vectorLayer = null,
            byte[]? buildingMetadata = null,
            IReadOnlyDictionary<string, byte[]>? buildingMeshes = null)
        {
            _chunks = chunks;
            _retained = retained;
            _companions = companions;
            _vectorLayer = vectorLayer;
            _buildingMetadata = buildingMetadata;
            _buildingMeshes = buildingMeshes ?? new Dictionary<string, byte[]>(StringComparer.Ordinal);
        }

        public float[] GetHeights(MapPackageLayerEntry entry)
        {
            var chunk = _chunks[(entry.ChunkX, entry.ChunkY)];
            return chunk.Heights.Samples is float[] arr ? arr : chunk.Heights.Samples.ToArray();
        }

        public byte[] GetMaterials(MapPackageLayerEntry entry)
        {
            var chunk = _chunks[(entry.ChunkX, entry.ChunkY)];
            return chunk.Materials!.Indices is byte[] arr ? arr : chunk.Materials.Indices.ToArray();
        }

        public byte[] GetMasks(MapPackageLayerEntry entry)
        {
            var chunk = _chunks[(entry.ChunkX, entry.ChunkY)];
            return chunk.Masks!.Flags is byte[] arr ? arr : chunk.Masks.Flags.ToArray();
        }

        public byte[] GetImagery(MapPackageLayerEntry entry)
        {
            var chunk = _chunks[(entry.ChunkX, entry.ChunkY)];
            return chunk.Imagery!.Rgba is byte[] arr ? arr : chunk.Imagery.Rgba.ToArray();
        }

        public byte[] GetRetainedSection(MapPackageRetainedEntry entry)
        {
            var section = _retained[entry.SectionName];
            return section.Bytes.ToArray();
        }

        public byte[] GetCompanion(MapPackageCompanionEntry entry)
        {
            if (_companions is not null && _companions.TryGetValue(entry.Name, out var bytes))
            {
                return bytes;
            }

            return [];
        }

        public byte[] GetVectorLayer(MapPackageVectorLayerEntry entry) => _vectorLayer ?? [];

        public byte[] GetBuildingMetadata(MapPackageBuildingsEntry entry) => _buildingMetadata ?? [];

        public byte[] GetBuildingMesh(MapPackageBuildingMeshEntry entry) =>
            _buildingMeshes.TryGetValue(entry.Path, out var bytes) ? bytes : [];
    }
}
