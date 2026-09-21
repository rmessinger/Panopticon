#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using OpenSage.Mathematics;
using FileFormatsNif = OpenSage.FileFormats.Nif;

namespace OpenSage.Content.Importing.Nif;

/// <summary>
/// Walks the validated NIF scene graph and converts every attached shape
/// into a baked, unskinned ImportedSubObject hanging off one identity
/// root bone. NiLODNode children other than the highest-detail selection
/// are intentionally omitted.
/// </summary>
internal static class NifSceneConverter
{
    internal static List<ImportedSubObject> Convert(
        FileFormatsNif.NifFile file,
        string modelName,
        AssetImportContext context)
    {
        var subObjects = new List<ImportedSubObject>();

        foreach (var root in file.Roots)
        {
            var rootBlock = file.Blocks[root];
            if (rootBlock is FileFormatsNif.NifAvObject rootAvObject)
            {
                Walk(file, root, NifTransformConverter.CreateLocalMatrix(rootAvObject),
                    Matrix4x4.Identity, subObjects, modelName, context);
            }
        }

        if (subObjects.Count == 0)
        {
            throw new InvalidDataException(
                $"NIF model '{modelName}' contains no convertible geometry.");
        }

        return subObjects;
    }

    private static void Walk(
        FileFormatsNif.NifFile file,
        int blockIndex,
        Matrix4x4 local,
        Matrix4x4 parentWorld,
        List<ImportedSubObject> subObjects,
        string modelName,
        AssetImportContext context)
    {
        var block = file.Blocks[blockIndex];
        if (block is not FileFormatsNif.NifAvObject avObject)
        {
            return;
        }

        var world = NifTransformConverter.ComposeWorld(local, parentWorld);

        switch (block)
        {
            case FileFormatsNif.NifLodNode lodNode:
                // Only the highest-detail child converts; the other LOD
                // levels are dropped wholesale by contract.
                var selectedChild = file.GetHighestDetailLodChild(lodNode);
                Descend(file, selectedChild, world, subObjects, modelName, context);
                break;

            case FileFormatsNif.NifNode node:
                foreach (var child in node.ChildReferences)
                {
                    if (child >= 0)
                    {
                        Descend(file, child, world, subObjects, modelName, context);
                    }
                }
                break;

            case FileFormatsNif.NifTriShape shape:
                subObjects.Add(ConvertShape(file, shape, world, subObjects.Count, modelName, context));
                break;
        }
    }

    private static void Descend(
        FileFormatsNif.NifFile file,
        int childIndex,
        Matrix4x4 parentWorld,
        List<ImportedSubObject> subObjects,
        string modelName,
        AssetImportContext context)
    {
        if (file.Blocks[childIndex] is FileFormatsNif.NifAvObject childAvObject)
        {
            Walk(file, childIndex, NifTransformConverter.CreateLocalMatrix(childAvObject),
                parentWorld, subObjects, modelName, context);
        }
    }

    private static ImportedSubObject ConvertShape(
        FileFormatsNif.NifFile file,
        FileFormatsNif.NifTriShape shape,
        Matrix4x4 world,
        int subObjectIndex,
        string modelName,
        AssetImportContext context)
    {
        if (shape.DataReference < 0 ||
            file.Blocks[shape.DataReference] is not FileFormatsNif.NifTriShapeData data)
        {
            throw new InvalidDataException(
                $"NIF shape '{shape.Name}' has no geometry data.");
        }

        var bake = NifTransformConverter.CreateBakeMatrix(world);
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
            parentBoneIndex: 0,
            mesh);
    }
}
