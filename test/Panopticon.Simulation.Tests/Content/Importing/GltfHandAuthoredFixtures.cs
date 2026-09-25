#nullable enable

using System.Collections.Generic;

namespace Panopticon.Tests.Content.Importing;

/// <summary>
/// Hand-authored fixture documents for cases SharpGLTF Core authoring cannot produce
/// (exotic accessor layouts, morph targets) plus minimal exclusion documents. Bytes are
/// static by construction; the opt-in fixture writer materializes them next to the
/// generator output.
/// </summary>
internal static class GltfHandAuthoredFixtures
{
    public static IReadOnlyDictionary<string, string> TextFixtures { get; } = new Dictionary<string, string>
    {
        // One document, three nodes: interleaved POSITION+NORMAL in a stride-24 view, a
        // sparse overlay replacing vertex 1, and unsigned-int indices.
        ["accessor-layouts.gltf"] = """
            {
              "asset": { "version": "2.0", "generator": "hand-authored" },
              "scene": 0,
              "scenes": [ { "nodes": [0, 1, 2] } ],
              "nodes": [
                { "name": "interleaved", "mesh": 0 },
                { "name": "sparse", "mesh": 1 },
                { "name": "uintIndices", "mesh": 2 }
              ],
              "meshes": [
                { "primitives": [ { "attributes": { "POSITION": 0, "NORMAL": 1 }, "indices": 2 } ] },
                { "primitives": [ { "attributes": { "POSITION": 3, "NORMAL": 4 }, "indices": 5 } ] },
                { "primitives": [ { "attributes": { "POSITION": 6, "NORMAL": 7 }, "indices": 8 } ] }
              ],
              "accessors": [
                { "bufferView": 0, "byteOffset": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
                { "bufferView": 0, "byteOffset": 12, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" },
                { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 1],
                  "sparse": { "count": 1,
                    "indices": { "bufferView": 4, "componentType": 5125 },
                    "values": { "bufferView": 5 } } },
                { "bufferView": 3, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 1, "byteOffset": 6, "componentType": 5123, "count": 3, "type": "SCALAR" },
                { "bufferView": 6, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
                { "bufferView": 3, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 7, "componentType": 5125, "count": 3, "type": "SCALAR" }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 72, "byteStride": 24 },
                { "buffer": 1, "byteOffset": 0, "byteLength": 12 },
                { "buffer": 2, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 2, "byteOffset": 36, "byteLength": 36 },
                { "buffer": 3, "byteOffset": 0, "byteLength": 4 },
                { "buffer": 4, "byteOffset": 0, "byteLength": 12 },
                { "buffer": 5, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 6, "byteOffset": 0, "byteLength": 12 }
              ],
              "buffers": [
                { "uri": "accessor-layouts-interleaved.bin", "byteLength": 72 },
                { "uri": "accessor-layouts-indices.bin", "byteLength": 12 },
                { "uri": "accessor-layouts-geometry.bin", "byteLength": 72 },
                { "uri": "accessor-layouts-sparse-keys.bin", "byteLength": 4 },
                { "uri": "accessor-layouts-sparse-values.bin", "byteLength": 12 },
                { "uri": "accessor-layouts-positions.bin", "byteLength": 36 },
                { "uri": "accessor-layouts-uint-indices.bin", "byteLength": 12 }
              ]
            }
            """,

        ["unsupported-morph.gltf"] = """
            {
              "asset": { "version": "2.0", "generator": "hand-authored" },
              "scene": 0,
              "scenes": [ { "nodes": [0] } ],
              "nodes": [ { "name": "morph", "mesh": 0 } ],
              "meshes": [ { "primitives": [ {
                "attributes": { "POSITION": 0 },
                "targets": [ { "POSITION": 0 } ]
              } ] } ],
              "accessors": [ { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] } ],
              "bufferViews": [ { "buffer": 0, "byteOffset": 0, "byteLength": 36 } ],
              "buffers": [ { "uri": "unsupported-morph.bin", "byteLength": 36 } ]
            }
            """,

        ["unsupported-mirrored-node.gltf"] = """
            {
              "asset": { "version": "2.0", "generator": "hand-authored" },
              "scene": 0,
              "scenes": [ { "nodes": [0] } ],
              "nodes": [ { "name": "mirror", "scale": [-1, 1, 1] } ]
            }
            """,

        ["unsupported-required-extension.gltf"] = """
            {
              "asset": { "version": "2.0", "generator": "hand-authored" },
              "extensionsRequired": [ "KHR_draco_mesh_compression" ],
              "scene": 0,
              "scenes": [ { "nodes": [0] } ],
              "nodes": [ { "name": "compressed" } ]
            }
            """,

        ["malformed-accessor.gltf"] = """
            {
              "asset": { "version": "2.0", "generator": "hand-authored" },
              "scene": 0,
              "scenes": [ { "nodes": [0] } ],
              "nodes": [ { "name": "broken", "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
              ],
              "buffers": [ { "uri": "malformed-accessor.bin", "byteLength": 42 } ]
            }
            """,

        ["unsupported-cubicspline.gltf"] = """
            {
              "asset": { "version": "2.0", "generator": "hand-authored" },
              "scene": 0,
              "scenes": [ { "nodes": [0] } ],
              "nodes": [ { "name": "animated", "translation": [0, 0, 0] } ],
              "animations": [ {
                "channels": [ { "sampler": 0, "target": { "node": 0, "path": "translation" } } ],
                "samplers": [ { "input": 0, "output": 1, "interpolation": "CUBICSPLINE" } ]
              } ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 2, "type": "SCALAR", "min": [0], "max": [1] },
                { "bufferView": 1, "componentType": 5126, "count": 6, "type": "VEC3" }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 8 },
                { "buffer": 0, "byteOffset": 8, "byteLength": 72 }
              ],
              "buffers": [ { "uri": "unsupported-cubicspline.bin", "byteLength": 80 } ]
            }
            """,

        ["unsupported-third-influence.gltf"] = """
            {
              "asset": { "version": "2.0", "generator": "hand-authored" },
              "scene": 0,
              "scenes": [ { "nodes": [0, 2] } ],
              "nodes": [
                { "name": "jointA", "children": [1] },
                { "name": "jointB", "translation": [0, 1, 0] },
                { "name": "meshNode", "mesh": 0, "skin": 0 }
              ],
              "skins": [ { "joints": [0, 1], "skeleton": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0, "NORMAL": 1, "JOINTS_0": 2, "WEIGHTS_0": 3 }, "indices": 4 } ] } ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
                { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 2, "componentType": 5123, "count": 3, "type": "VEC4" },
                { "bufferView": 3, "componentType": 5126, "count": 3, "type": "VEC4" },
                { "bufferView": 4, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 72, "byteLength": 24 },
                { "buffer": 0, "byteOffset": 96, "byteLength": 48 },
                { "buffer": 0, "byteOffset": 144, "byteLength": 6 }
              ],
              "buffers": [ { "uri": "unsupported-third-influence.bin", "byteLength": 150 } ]
            }
            """,

        ["unsupported-multiple-skins.gltf"] = """
            {
              "asset": { "version": "2.0", "generator": "hand-authored" },
              "scene": 0,
              "scenes": [ { "nodes": [0] } ],
              "nodes": [ { "name": "joint" } ],
              "skins": [ { "joints": [0], "skeleton": 0 }, { "joints": [0], "skeleton": 0 } ]
            }
            """,
    };

    /// <summary>Binary companions of the hand-authored documents, keyed by file name.</summary>
    public static IReadOnlyDictionary<string, byte[]> BinaryFixtures { get; } = new Dictionary<string, byte[]>
    {
        // Interleaved: three (position, normal) pairs with stride 24.
        ["accessor-layouts-interleaved.bin"] = Interleave(
            Floats(0, 0, 0, 1, 0, 0, 0, 1, 0),
            Floats(0, 0, 1, 0, 0, 1, 0, 0, 1)),

        ["accessor-layouts-indices.bin"] = U16(0, 1, 2, 0, 2, 1),

        // Geometry buffer: normals then spare.
        ["accessor-layouts-geometry.bin"] = Concat(
            Floats(0, 0, 1, 0, 0, 1, 0, 0, 1),
            Floats(0, 0, 1, 0, 0, 1, 0, 0, 1)),

        ["accessor-layouts-sparse-keys.bin"] = U32(1),

        ["accessor-layouts-sparse-values.bin"] = Floats(1, 1, 1),

        ["accessor-layouts-positions.bin"] = Floats(0, 0, 0, 1, 0, 0, 0, 1, 0),

        ["accessor-layouts-uint-indices.bin"] = U32(0, 1, 2),

        ["unsupported-morph.bin"] = Floats(0, 0, 0, 1, 0, 0, 0, 1, 0),

        // 36 bytes of positions plus u16 indices (2, 1, 0) that stay in range, and the
        // malformed variant is produced by the document's own layout.
        ["malformed-accessor.bin"] = Concat(Floats(0, 0, 0, 1, 0, 0, 0, 1, 0), U16(0, 1, 5)),

        ["unsupported-cubicspline.bin"] = Concat(Floats(0, 1), new byte[72]),

        // Weights (0.5, 0.25, 0.25, 0) x3: meaningful third influence, sum one.
        ["unsupported-third-influence.bin"] = Concat(
            Floats(0, 0, 0, 1, 0, 0, 0, 1, 0),
            Floats(0, 0, 1, 0, 0, 1, 0, 0, 1),
            U16(0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0),
            Floats(0.5f, 0.25f, 0.25f, 0, 0.5f, 0.25f, 0.25f, 0, 0.5f, 0.25f, 0.25f, 0),
            U16(0, 1, 2)),
    };

    private static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] U16(params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] U32(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using var stream = new System.IO.MemoryStream();
        foreach (var part in parts)
        {
            stream.Write(part, 0, part.Length);
        }

        return stream.ToArray();
    }

    private static byte[] Interleave(byte[] first, byte[] second)
    {
        using var stream = new System.IO.MemoryStream();
        for (var i = 0; i < 3; i++)
        {
            stream.Write(first, i * 12, 12);
            stream.Write(second, i * 12, 12);
        }

        return stream.ToArray();
    }
}
