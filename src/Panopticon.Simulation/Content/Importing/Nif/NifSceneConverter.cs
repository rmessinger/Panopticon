#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Mathematics;
using FileFormatsNif = Panopticon.FileFormats.Nif;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Walks the validated NIF scene graph and converts every unit-scale
/// NiNode and NiLODNode into an ImportedBone, depth-first, root first,
/// keeping source node names and the nearest bone ancestor as parent.
/// Every attached shape bakes its accumulated transform relative to its
/// parent bone into vertices and attaches to that bone. Nodes with
/// non-unit scale never become bones; their subtrees bake statically into
/// the nearest bone ancestor. NiLODNode children other than the
/// highest-detail selection are intentionally omitted.
/// </summary>
internal static class NifSceneConverter
{
    private const float UnitScaleTolerance = 1e-4f;

    internal static NifSceneGraph ConvertGraph(
        FileFormatsNif.NifFile file,
        string modelName,
        AssetImportContext context)
    {
        var bones = new List<ImportedBone>();
        var subObjects = new List<ImportedSubObject>();

        foreach (var root in file.Roots)
        {
            if (file.Blocks[root] is FileFormatsNif.NifAvObject rootAvObject)
            {
                Walk(file, root, rootAvObject,
                    parentBoneIndex: -1,
                    relativeToBone: Matrix4x4.Identity,
                    insideScaledSubtree: false,
                    bones, subObjects, modelName, context);
            }
        }

        if (bones.Count == 0)
        {
            throw new NotSupportedException(
                $"NIF model '{modelName}' contains no unit-scale node to use as a bone.");
        }

        if (subObjects.Count == 0)
        {
            throw new InvalidDataException(
                $"NIF model '{modelName}' contains no convertible geometry.");
        }

        return new NifSceneGraph(
            new ImportedSkeleton(modelName, bones),
            subObjects);
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
        string modelName,
        AssetImportContext context)
    {
        var local = NifTransformConverter.CreateLocalMatrix(avObject);

        if (file.Blocks[blockIndex] is FileFormatsNif.NifTriShape shape)
        {
            if (parentBoneIndex < 0)
            {
                throw new NotSupportedException(
                    $"NIF shape '{shape.Name}' has no unit-scale node ancestor to attach to.");
            }

            subObjects.Add(ConvertShape(
                file, shape, local * relativeToBone, parentBoneIndex,
                subObjects.Count, modelName, context));
            return;
        }

        int childParentBoneIndex;
        Matrix4x4 childRelativeToBone;
        bool childInsideScaledSubtree;

        if (!insideScaledSubtree && IsUnitScale(avObject.Scale))
        {
            childParentBoneIndex = bones.Count;
            childRelativeToBone = Matrix4x4.Identity;
            childInsideScaledSubtree = false;

            var engineLocal = NifTransformConverter.CreateBoneLocalMatrix(avObject);
            bones.Add(new ImportedBone(
                avObject.Name,
                parentBoneIndex,
                new Vector3(engineLocal.M41, engineLocal.M42, engineLocal.M43),
                CanonicalRotation(Quaternion.CreateFromRotationMatrix(engineLocal))));
        }
        else
        {
            // A scaled node (or any node inside one) never becomes a bone;
            // its subtree bakes into the nearest bone ancestor.
            childParentBoneIndex = parentBoneIndex;
            childRelativeToBone = local * relativeToBone;
            childInsideScaledSubtree = true;
        }

        if (file.Blocks[blockIndex] is FileFormatsNif.NifLodNode lodNode)
        {
            // Only the highest-detail child converts; the other LOD
            // levels are dropped wholesale by contract.
            Descend(file, file.GetHighestDetailLodChild(lodNode),
                childParentBoneIndex, childRelativeToBone, childInsideScaledSubtree,
                bones, subObjects, modelName, context);
        }
        else if (file.Blocks[blockIndex] is FileFormatsNif.NifNode node)
        {
            foreach (var child in node.ChildReferences)
            {
                if (child >= 0)
                {
                    Descend(file, child,
                        childParentBoneIndex, childRelativeToBone, childInsideScaledSubtree,
                        bones, subObjects, modelName, context);
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
        string modelName,
        AssetImportContext context)
    {
        if (file.Blocks[childIndex] is FileFormatsNif.NifAvObject childAvObject)
        {
            Walk(file, childIndex, childAvObject,
                parentBoneIndex, relativeToBone, insideScaledSubtree,
                bones, subObjects, modelName, context);
        }
    }

    private static bool IsUnitScale(float scale)
    {
        return MathF.Abs(scale - 1.0f) <= UnitScaleTolerance;
    }

    private static Quaternion CanonicalRotation(Quaternion rotation)
    {
        // Same-rotation sign canonicalization, matching the glTF importer.
        return rotation.W < 0 ? Quaternion.Negate(rotation) : rotation;
    }

    private static ImportedSubObject ConvertShape(
        FileFormatsNif.NifFile file,
        FileFormatsNif.NifTriShape shape,
        Matrix4x4 relativeToParentBone,
        int parentBoneIndex,
        int subObjectIndex,
        string modelName,
        AssetImportContext context)
    {
        if (shape.SkinInstanceReference >= 0)
        {
            throw new NotSupportedException(
                $"NIF shape '{shape.Name}' is skinned; skinned-shape conversion is C4d work (WI-C4d-06).");
        }

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
        var vertices = new ImportedVertex[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var position = NifTransformConverter.TransformPosition(data.Vertices[i], bake);
            var normal = NifTransformConverter.TransformNormal(data.Normals![i], bake);
            vertices[i] = new ImportedVertex(position, normal);
        }

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

        var properties = new List<FileFormatsNif.NifProperty>();
        foreach (var propertyReference in shape.PropertyReferences)
        {
            if (propertyReference >= 0 &&
                file.Blocks[propertyReference] is FileFormatsNif.NifProperty property)
            {
                properties.Add(property);
            }
        }

        var material = NifMaterialConverter.Convert(properties, file.Blocks, shape.Name, context);

        Vector2[]? textureCoordinates = null;
        if (data.NumUvSets > 0)
        {
            // NIF UVs are V-up; engine textures are sampled top-down, so flip V.
            var source = data.UvSets[0];
            textureCoordinates = new Vector2[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                textureCoordinates[i] = new Vector2(source[i].X, 1.0f - source[i].Y);
            }
        }

        var meshPart = new ImportedMeshPart(
            textureCoordinates,
            textureCoordinates1: null,
            startIndex: 0,
            indexCount: (uint)indices.Count,
            material.BlendEnabled,
            materialIndex: 0);

        // Bounds are recomputed from baked positions; the NIF source bounds
        // stay parser diagnostics by contract.
        var positions = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            positions[i] = vertices[i].Position;
        }

        var boundingBox = AxisAlignedBoundingBox.CreateFromPoints(positions);
        var sphereCenter = (boundingBox.Min + boundingBox.Max) / 2.0f;
        var boundingSphere = new BoundingSphere(
            sphereCenter,
            Vector3.Distance(sphereCenter, boundingBox.Max));

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

        var displayName = shape.Name.Length == 0 ? $"Shape{subObjectIndex}" : shape.Name;
        return new ImportedSubObject(
            $"{modelName}.{subObjectIndex}.{displayName}",
            displayName,
            parentBoneIndex,
            mesh);
    }
}
