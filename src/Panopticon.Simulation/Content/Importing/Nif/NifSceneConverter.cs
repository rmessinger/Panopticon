#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Mathematics;
using FileFormatsNif = Panopticon.FileFormats.Nif;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Walks the validated NIF scene graph and converts every NiNode and
/// NiLODNode with a finite positive rest scale into an ImportedBone
/// (rest scale preserved as BindScale), depth-first, root first,
/// keeping source node names and the nearest bone ancestor as parent.
/// Every attached shape bakes its accumulated transform relative to its
/// parent bone into vertices and attaches to that bone. Nodes with
/// singular rest scale (zero, negative, tiny, or non-finite) never
/// become bones; their subtrees bake statically into the nearest bone
/// ancestor and the node is recorded in CollapsedScaleNodes. NiLODNode
/// children other than the highest-detail selection are intentionally
/// omitted. Skinned shapes defer to NifSkinConverter after the bone
/// walk.
/// </summary>
internal static class NifSceneConverter
{
    private const float MinimumRestScale = 1e-4f;

    internal static NifSceneGraph ConvertGraph(
        FileFormatsNif.NifFile file,
        string modelName,
        AssetImportContext context)
    {
        var bones = new List<ImportedBone>();
        var subObjects = new List<ImportedSubObject>();
        var nodeToBoneIndex = new Dictionary<int, int>();
        var skinnedShapes = new List<(int BlockIndex, FileFormatsNif.NifTriShape Shape)>();
        var targets = new NifPropertyTargetMap(modelName);
        var collapsedScaleNodes = new Dictionary<string, float>();

        foreach (var root in file.Roots)
        {
            if (file.Blocks[root] is FileFormatsNif.NifAvObject rootAvObject)
            {
                Walk(file, root, rootAvObject,
                    parentBoneIndex: -1,
                    relativeToBone: Matrix4x4.Identity,
                    insideScaledSubtree: false,
                    bones, subObjects, nodeToBoneIndex, skinnedShapes, targets,
                    collapsedScaleNodes, modelName, context);
            }
        }

        if (skinnedShapes.Count > 0)
        {
            NifSkinConverter.ConvertSkinnedShapes(
                file, modelName, skinnedShapes, nodeToBoneIndex, bones, subObjects, targets, context);
        }

        if (bones.Count == 0)
        {
            throw new NotSupportedException(
                $"NIF model '{modelName}' contains no node with a usable rest scale to use as a bone.");
        }

        if (subObjects.Count == 0)
        {
            throw new InvalidDataException(
                $"NIF model '{modelName}' contains no convertible geometry.");
        }

        return new NifSceneGraph(
            new ImportedSkeleton(modelName, bones),
            subObjects,
            targets,
            collapsedScaleNodes);
    }

    private static void Walk(
        FileFormatsNif.NifFile file,
        int blockIndex,
        FileFormatsNif.NifAvObject avObject,
        int parentBoneIndex,
        Matrix4x4 relativeToBone,
        bool insideScaledSubtree,
        List<ImportedBone> bones,
        List<ImportedSubObject> subObjects,
        Dictionary<int, int> nodeToBoneIndex,
        List<(int BlockIndex, FileFormatsNif.NifTriShape Shape)> skinnedShapes,
        NifPropertyTargetMap targets,
        Dictionary<string, float> collapsedScaleNodes,
        string modelName,
        AssetImportContext context)
    {
        var hasValidRestScale = IsValidRestScale(avObject.Scale);

        // A non-finite scale cannot feed CreateLocalMatrix (it rejects
        // non-finite transforms); nothing meaningful can accumulate from
        // such a node, so its subtree keeps the accumulated transform.
        Matrix4x4 local = float.IsFinite(avObject.Scale)
            ? NifTransformConverter.CreateLocalMatrix(avObject)
            : relativeToBone;

        if (file.Blocks[blockIndex] is FileFormatsNif.NifTriShape shape)
        {
            if (shape.SkinInstanceReference >= 0)
            {
                // Skinning is authoritative for placement: the shape's
                // vertex data is stored in skin space, so its node
                // ancestors contribute nothing and no bone ancestor is
                // required. Conversion waits for the full bone walk.
                skinnedShapes.Add((blockIndex, shape));
                return;
            }

            if (parentBoneIndex < 0)
            {
                throw new NotSupportedException(
                    $"NIF shape '{shape.Name}' has no bone ancestor with a usable rest scale to attach to.");
            }

            subObjects.Add(ConvertShape(
                file, blockIndex, shape, local * relativeToBone, parentBoneIndex,
                subObjects.Count, targets, modelName, context));
            return;
        }

        int childParentBoneIndex;
        Matrix4x4 childRelativeToBone;
        bool childInsideScaledSubtree;

        if (!insideScaledSubtree && hasValidRestScale)
        {
            childParentBoneIndex = bones.Count;
            childRelativeToBone = Matrix4x4.Identity;
            childInsideScaledSubtree = false;

            // The conjugated local is T(t * E) * s * (E^-1 R E): scalar
            // scale commutes with the conjugation, so dividing the linear
            // part by the rest scale recovers the conjugated rotation.
            var engineLocal = NifTransformConverter.CreateBoneLocalMatrix(avObject);
            nodeToBoneIndex.Add(blockIndex, childParentBoneIndex);
            targets.RecordBone(blockIndex, avObject.Name);
            bones.Add(new ImportedBone(
                avObject.Name,
                parentBoneIndex,
                new Vector3(engineLocal.M41, engineLocal.M42, engineLocal.M43),
                CanonicalRotation(Quaternion.CreateFromRotationMatrix(engineLocal * (1.0f / avObject.Scale))),
                avObject.Scale));
        }
        else
        {
            // A singular-rest-scale node (or any node inside one) never
            // becomes a bone; its subtree bakes into the nearest bone
            // ancestor.
            childParentBoneIndex = parentBoneIndex;
            childRelativeToBone = local * relativeToBone;
            childInsideScaledSubtree = true;
            if (!hasValidRestScale)
            {
                collapsedScaleNodes.TryAdd(avObject.Name, avObject.Scale);
            }
        }

        if (file.Blocks[blockIndex] is FileFormatsNif.NifLodNode lodNode)
        {
            // Only the highest-detail child converts; the other LOD
            // levels are dropped wholesale by contract.
            Descend(file, file.GetHighestDetailLodChild(lodNode),
                childParentBoneIndex, childRelativeToBone, childInsideScaledSubtree,
                bones, subObjects, nodeToBoneIndex, skinnedShapes, targets,
                collapsedScaleNodes, modelName, context);
        }
        else if (file.Blocks[blockIndex] is FileFormatsNif.NifNode node)
        {
            foreach (var child in node.ChildReferences)
            {
                if (child >= 0)
                {
                    Descend(file, child,
                        childParentBoneIndex, childRelativeToBone, childInsideScaledSubtree,
                        bones, subObjects, nodeToBoneIndex, skinnedShapes, targets,
                        collapsedScaleNodes, modelName, context);
                }
            }
        }
    }

    private static void Descend(
        FileFormatsNif.NifFile file,
        int childIndex,
        int parentBoneIndex,
        Matrix4x4 relativeToBone,
        bool insideScaledSubtree,
        List<ImportedBone> bones,
        List<ImportedSubObject> subObjects,
        Dictionary<int, int> nodeToBoneIndex,
        List<(int BlockIndex, FileFormatsNif.NifTriShape Shape)> skinnedShapes,
        NifPropertyTargetMap targets,
        Dictionary<string, float> collapsedScaleNodes,
        string modelName,
        AssetImportContext context)
    {
        if (file.Blocks[childIndex] is FileFormatsNif.NifAvObject childAvObject)
        {
            Walk(file, childIndex, childAvObject,
                parentBoneIndex, relativeToBone, insideScaledSubtree,
                bones, subObjects, nodeToBoneIndex, skinnedShapes, targets,
                collapsedScaleNodes, modelName, context);
        }
    }

    private static bool IsValidRestScale(float scale)
    {
        return scale > MinimumRestScale && float.IsFinite(scale);
    }

    /// <summary>
    /// Some EE2 models ship shapes without stored normals; C1 requires one
    /// normal per vertex, so face normals are accumulated per vertex.
    /// </summary>
    private static Vector3[] SynthesizeNormals(FileFormatsNif.NifTriShapeData data)
    {
        var normals = new Vector3[data.NumVertices];
        foreach (var (v1, v2, v3) in data.Triangles)
        {
            var a = data.Vertices[v1];
            var b = data.Vertices[v2];
            var c = data.Vertices[v3];
            var face = Vector3.Cross(b - a, c - a);
            normals[v1] += face;
            normals[v2] += face;
            normals[v3] += face;
        }

        for (var i = 0; i < normals.Length; i++)
        {
            var normal = normals[i];
            normals[i] = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        }

        return normals;
    }

    private static Quaternion CanonicalRotation(Quaternion rotation)
    {
        // Same-rotation sign canonicalization, matching the glTF importer.
        return rotation.W < 0 ? Quaternion.Negate(rotation) : rotation;
    }

    private static ImportedSubObject ConvertShape(
        FileFormatsNif.NifFile file,
        int shapeBlockIndex,
        FileFormatsNif.NifTriShape shape,
        Matrix4x4 relativeToParentBone,
        int parentBoneIndex,
        int subObjectIndex,
        NifPropertyTargetMap targets,
        string modelName,
        AssetImportContext context)
    {
        if (shape.DataReference < 0 ||
            file.Blocks[shape.DataReference] is not FileFormatsNif.NifTriShapeData data)
        {
            throw new InvalidDataException(
                $"NIF shape '{shape.Name}' has no geometry data.");
        }

        var bake = NifTransformConverter.CreateBakeMatrix(relativeToParentBone);
        var bakeDeterminant = NifTransformConverter.BakeDeterminant(bake);
        if (bakeDeterminant == 0)
        {
            throw new NotSupportedException(
                $"NIF shape '{shape.Name}' has a degenerate accumulated transform.");
        }

        var reverseWinding = bakeDeterminant < 0;

        var vertexCount = data.NumVertices;
        var sourceNormals = data.Normals ?? SynthesizeNormals(data);
        var vertices = new ImportedVertex[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var position = NifTransformConverter.TransformPosition(data.Vertices[i], bake);
            var normal = NifTransformConverter.TransformNormal(sourceNormals[i], bake);
            vertices[i] = new ImportedVertex(position, normal);
        }

        var indices = CopyTriangleIndices(data, reverseWinding);
        var material = ConvertShapeMaterial(file, shape, context);
        var textureCoordinates = ConvertTextureCoordinates(data);
        var meshPart = CreateMeshPart(textureCoordinates, (uint)indices.Count, material);

        var positions = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            positions[i] = vertices[i].Position;
        }

        var (boundingBox, boundingSphere) = ComputeBounds(positions);

        var mesh = new ImportedMesh(
            vertices,
            indices,
            boundingBox,
            boundingSphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: [material],
            meshParts: [meshPart]);

        var subObject = CreateSubObject(modelName, shape.Name, subObjectIndex, parentBoneIndex, mesh);
        targets.RecordShape(
            shapeBlockIndex, shape.Name, subObject.FullName, GatherShapePropertyTypes(file, shape));
        return subObject;
    }

    /// <summary>
    /// Collects a shape's property references as (block index, wire type
    /// name) pairs for the property target map; non-property refs are
    /// skipped.
    /// </summary>
    internal static List<(int PropertyIndex, string TypeName)> GatherShapePropertyTypes(
        FileFormatsNif.NifFile file,
        FileFormatsNif.NifTriShape shape)
    {
        var properties = new List<(int PropertyIndex, string TypeName)>();
        foreach (var propertyReference in shape.PropertyReferences)
        {
            if (propertyReference >= 0 &&
                file.Blocks[propertyReference] is FileFormatsNif.NifProperty)
            {
                properties.Add((propertyReference, file.Header.Blocks[propertyReference].TypeName));
            }
        }

        return properties;
    }

    /// <summary>
    /// Gathers a shape's property chain into one imported material.
    /// Shared by the rigid and skinned shape paths.
    /// </summary>
    internal static ImportedMaterial ConvertShapeMaterial(
        FileFormatsNif.NifFile file,
        FileFormatsNif.NifTriShape shape,
        AssetImportContext context)
    {
        var properties = new List<FileFormatsNif.NifProperty>();
        foreach (var propertyReference in shape.PropertyReferences)
        {
            if (propertyReference >= 0 &&
                file.Blocks[propertyReference] is FileFormatsNif.NifProperty property)
            {
                properties.Add(property);
            }
        }

        return NifMaterialConverter.Convert(properties, file.Blocks, shape.Name, context);
    }

    /// <summary>
    /// Copies UV set 0 with the V flip (NIF UVs are V-up; engine textures
    /// are sampled top-down). Shared by the rigid and skinned shape paths.
    /// </summary>
    internal static Vector2[]? ConvertTextureCoordinates(
        FileFormatsNif.NifTriShapeData data)
    {
        if (data.NumUvSets == 0)
        {
            return null;
        }

        var source = data.UvSets[0];
        var textureCoordinates = new Vector2[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            textureCoordinates[i] = new Vector2(source[i].X, 1.0f - source[i].Y);
        }

        return textureCoordinates;
    }

    /// <summary>
    /// Copies triangle indices, swapping winding when the bake mirrors.
    /// Shared by the rigid and skinned shape paths.
    /// </summary>
    internal static List<ushort> CopyTriangleIndices(
        FileFormatsNif.NifTriShapeData data,
        bool reverseWinding)
    {
        var indices = new List<ushort>(data.Triangles.Count * 3);
        foreach (var triangle in data.Triangles)
        {
            if (reverseWinding)
            {
                indices.Add(triangle.V1);
                indices.Add(triangle.V3);
                indices.Add(triangle.V2);
            }

            else
            {
                indices.Add(triangle.V1);
                indices.Add(triangle.V2);
                indices.Add(triangle.V3);
            }
        }

        return indices;
    }

    internal static ImportedMeshPart CreateMeshPart(
        Vector2[]? textureCoordinates,
        uint indexCount,
        ImportedMaterial material)
    {
        return new ImportedMeshPart(
            textureCoordinates,
            textureCoordinates1: null,
            startIndex: 0,
            indexCount,
            material.BlendEnabled,
            materialIndex: 0);
    }

    /// <summary>
    /// Bounds are recomputed from baked positions; the NIF source bounds
    /// stay parser diagnostics by contract.
    /// </summary>
    internal static (AxisAlignedBoundingBox Box, BoundingSphere Sphere) ComputeBounds(
        Vector3[] positions)
    {
        var boundingBox = AxisAlignedBoundingBox.CreateFromPoints(positions);
        var sphereCenter = (boundingBox.Min + boundingBox.Max) / 2.0f;
        var boundingSphere = new BoundingSphere(
            sphereCenter,
            Vector3.Distance(sphereCenter, boundingBox.Max));
        return (boundingBox, boundingSphere);
    }

    internal static ImportedSubObject CreateSubObject(
        string modelName,
        string shapeName,
        int subObjectIndex,
        int parentBoneIndex,
        ImportedMesh mesh)
    {
        var displayName = shapeName.Length == 0 ? $"Shape{subObjectIndex}" : shapeName;
        return new ImportedSubObject(
            $"{modelName}.{subObjectIndex}.{displayName}",
            displayName,
            parentBoneIndex,
            mesh);
    }
}
