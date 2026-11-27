#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Mathematics;

namespace Panopticon.Terrain;

/// <summary>
/// Rendered model of one generated bridge's level deck: the layout's world-space triangles, grouped by piece
/// and part, with the source materials and the per-vertex normals and texture coordinates. The instance keeps
/// the identity world matrix, so the model is drawn from exactly the triangles the vehicles touch.
/// </summary>
internal sealed class GeneratedBridgeDeckModel : DisposableBase
{
    private const string DeckName = "GeneratedBridgeDeck";

    private GeneratedBridgeDeckModel(ImportedModel imported, Model model, ModelInstance instance)
    {
        Imported = imported;
        Instance = instance;
        AddDisposable(model);
        AddDisposable(instance);
    }

    /// <summary>The CPU triangle list that was rendered.</summary>
    public ImportedModel Imported { get; }

    /// <summary>Instance of the materialized deck; its world matrix is the identity.</summary>
    public ModelInstance Instance { get; }

    public static GeneratedBridgeDeckModel Create(GeneratedBridgeLayout layout, IReadOnlyDictionary<string, ImportedMesh> sourceMeshes, AssetLoadContext loadContext)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(sourceMeshes);

        if (layout.VertexAttributes.Count != 3 * layout.Faces.Count)
        {
            throw new ArgumentException("Layout needs three vertex attributes per face.", nameof(layout));
        }

        ArgumentNullException.ThrowIfNull(loadContext);

        // GroupBy keeps first-appearance order of the pieces; OrderBy is stable, so faces keep their order within a part.
        var pieces = Enumerable.Range(0, layout.Faces.Count)
            .GroupBy(face => layout.Faces[face].PieceName)
            .Select(group => (Name: group.Key, Faces: group.OrderBy(face => layout.Faces[face].MeshPartIndex).ToList()))
            .ToList();

        var subObjects = new List<ImportedSubObject>(pieces.Count);

        foreach (var piece in pieces)
        {
            if (!sourceMeshes.TryGetValue(piece.Name, out var source))
            {
                throw new InvalidOperationException($"no source mesh for piece {piece.Name}");
            }

            subObjects.Add(new ImportedSubObject(piece.Name, piece.Name, 0, BuildMesh(layout, piece.Faces, source)));
        }

        var imported = new ImportedModel(DeckName, W3dModelImporter.CreateDefaultSkeleton(), subObjects, ImportedSkeletonOrigin.Default);
        var model = ImportedModelMaterializer.MaterializeModel(imported, loadContext, 1f);
        var instance = model.CreateInstance(loadContext);
        instance.SetWorldMatrix(Matrix4x4.Identity);
        instance.Update(TimeInterval.Zero);

        return new GeneratedBridgeDeckModel(imported, model, instance);
    }

    /// <summary>One piece as an imported mesh: sequential vertices in face order, one part per source mesh part.</summary>
    private static ImportedMesh BuildMesh(GeneratedBridgeLayout layout, List<int> faces, ImportedMesh source)
    {
        var vertices = new List<ImportedVertex>(faces.Count * 3);
        var indices = new List<ushort>(faces.Count * 3);
        var hasUv1 = false;

        foreach (var face in faces)
        {
            var raw = layout.Faces[face];
            var corners = new[] { raw.A, raw.B, raw.C };

            for (var corner = 0; corner < 3; corner++)
            {
                var attributes = layout.VertexAttributes[(3 * face) + corner];
                hasUv1 |= attributes.Uv1.HasValue;
                indices.Add((ushort)vertices.Count);
                vertices.Add(new ImportedVertex(corners[corner], attributes.Normal));
            }
        }

        var parts = new List<ImportedMeshPart>();
        var triangleOffset = 0;

        // Faces are already ordered by part, so each part is one contiguous run of triangles.
        foreach (var partGroup in faces.GroupBy(face => layout.Faces[face].MeshPartIndex))
        {
            var partFaces = partGroup.ToList();
            var uv0 = new Vector2[vertices.Count];
            var uv1 = new Vector2[vertices.Count];

            for (var local = 0; local < partFaces.Count; local++)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = ((triangleOffset + local) * 3) + corner;
                    var attributes = layout.VertexAttributes[(3 * partFaces[local]) + corner];
                    uv0[vertex] = attributes.Uv0 ?? Vector2.Zero;
                    uv1[vertex] = attributes.Uv1 ?? Vector2.Zero;
                }
            }

            var partIndex = partGroup.Key;
            var sourcePart = partIndex >= 0 && partIndex < source.MeshParts.Count ? source.MeshParts[partIndex] : null;
            parts.Add(new ImportedMeshPart(
                uv0,
                hasUv1 ? uv1 : null,
                (uint)(triangleOffset * 3),
                (uint)(partFaces.Count * 3),
                sourcePart?.BlendEnabled ?? false,
                layout.Faces[partFaces[0]].MaterialIndex));
            triangleOffset += partFaces.Count;
        }

        // Bounds are computed from this mesh's vertices: the deck triangles are not the source sub-object's.
        var positions = vertices.Select(vertex => vertex.Position).ToList();
        var min = positions.Aggregate(Vector3.Min);
        var max = positions.Aggregate(Vector3.Max);
        var center = (min + max) * 0.5f;
        var radius = positions.Max(position => Vector3.Distance(position, center));

        return new ImportedMesh(
            vertices,
            indices,
            new AxisAlignedBoundingBox(min, max),
            new BoundingSphere(center, radius),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: source.HasHouseColor,
            isSkinned: false,
            materials: source.Materials,
            meshParts: parts);
    }
}
