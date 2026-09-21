using System;
using System.Collections.Generic;
using System.IO;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// A fully decoded NIF 10.1.0.0 file: header, block directory, block
/// bodies, and validated references. Every block body starts with the
/// leading u32 0 documented in the C4a deep dive; the file ends with the
/// 8-byte footer (1, 0).
/// </summary>
public sealed record NifFile(
    NifHeader Header,
    IReadOnlyList<NifBlock> Blocks,
    IReadOnlyList<int> Roots)
{
    public const uint Footer1 = 1;
    public const uint Footer2 = 0;

    public static NifFile Read(Stream stream)
    {
        var reader = new NifBinaryReader(stream);

        var header = NifHeader.Parse(reader);

        var blocks = new NifBlock[header.NumBlocks];
        for (var i = 0; i < header.NumBlocks; i++)
        {
            var leading = reader.ReadUInt32($"Block[{i}].Leading");
            if (leading != 0)
            {
                throw new InvalidDataException(
                    $"NIF block {i} ({header.Blocks[i].TypeName}) has leading word {leading}; expected 0.");
            }
            blocks[i] = ReadBlock(reader, header, i);
        }

        var footer1 = reader.ReadUInt32("Footer1");
        var footer2 = reader.ReadUInt32("Footer2");
        if (footer1 != Footer1 || footer2 != Footer2)
        {
            throw new InvalidDataException(
                $"NIF footer ({footer1}, {footer2}) is invalid; expected ({Footer1}, {Footer2}).");
        }
        if (reader.RemainingBytes != 0)
        {
            throw new InvalidDataException(
                $"NIF stream has {reader.RemainingBytes} trailing bytes after the footer.");
        }

        var roots = ValidateReferences(header, blocks);

        return new NifFile(header, blocks, roots);
    }

    private static NifBlock ReadBlock(NifBinaryReader reader, NifHeader header, int index)
    {
        var typeName = header.Blocks[index].TypeName;
        switch (typeName)
        {
            case "NiNode":
                return NifNode.Read(reader);
            case "NiStringExtraData":
                return NifStringExtraData.Read(reader);
            default:
                throw new NotSupportedException(
                    $"NIF block {index} has unsupported type '{typeName}'.");
        }
    }

    private static IReadOnlyList<int> ValidateReferences(NifHeader header, NifBlock[] blocks)
    {
        int[]? parents = new int[blocks.Length];
        for (var i = 0; i < parents.Length; i++)
        {
            parents[i] = -1;
        }

        void ValidateReference(int reference, int owner, string field)
        {
            if (reference < -1 || reference >= blocks.Length)
            {
                throw new InvalidDataException(
                    $"NIF block {owner} has {field} reference {reference} " +
                    $"outside the valid range [-1, {blocks.Length - 1}].");
            }
        }

        for (var i = 0; i < blocks.Length; i++)
        {
            switch (blocks[i])
            {
                case NifObjectNet netBlock:
                    foreach (var extra in netBlock.ExtraDataReferences)
                    {
                        ValidateReference(extra, i, "ExtraData");
                        if (extra >= 0 && blocks[extra] is not NifStringExtraData)
                        {
                            throw new InvalidDataException(
                                $"NIF block {i} references block {extra} as extra data, " +
                                $"but that block is a {blocks[extra].GetType().Name}.");
                        }
                    }
                    ValidateReference(netBlock.ControllerReference, i, "Controller");
                    if (blocks[i] is NifAvObject avBlock)
                    {
                        foreach (var property in avBlock.PropertyReferences)
                        {
                            ValidateReference(property, i, "Property");
                        }
                        ValidateReference(avBlock.CollisionObjectReference, i, "CollisionObject");
                        if (blocks[i] is NifNode node)
                        {
                            foreach (var child in node.ChildReferences)
                            {
                                ValidateReference(child, i, "Child");
                                if (child >= 0)
                                {
                                    if (child == i)
                                    {
                                        throw new InvalidDataException(
                                            $"NIF node {i} lists itself as a child.");
                                    }
                                    if (parents[child] != -1)
                                    {
                                        throw new InvalidDataException(
                                            $"NIF node {child} has two parents: {parents[child]} and {i}.");
                                    }
                                    parents[child] = i;
                                }
                            }
                        }
                    }
                    break;
            }
        }

        var roots = new List<int>();
        for (var i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] is NifNode && parents[i] == -1)
            {
                roots.Add(i);
            }
        }

        DetectCycles(blocks, roots);

        return roots;
    }

    private static void DetectCycles(NifBlock[] blocks, IReadOnlyList<int> roots)
    {
        const int White = 0;
        const int Grey = 1;
        const int Black = 2;

        var color = new int[blocks.Length];

        void Visit(int node)
        {
            color[node] = Grey;
            if (blocks[node] is NifNode n)
            {
                foreach (var child in n.ChildReferences)
                {
                    if (child < 0)
                    {
                        continue;
                    }
                    if (color[child] == Grey)
                    {
                        throw new InvalidDataException(
                            $"NIF scene graph contains a cycle through node {child}.");
                    }
                    if (color[child] == White)
                    {
                        Visit(child);
                    }
                }
            }
            color[node] = Black;
        }

        foreach (var root in roots)
        {
            if (color[root] == White)
            {
                Visit(root);
            }
        }

        for (var i = 0; i < blocks.Length; i++)
        {
            // Every non-root node's parent chain either reaches a root
            // (visited above) or cycles, so any white node left is cyclic.
            if (blocks[i] is NifNode && color[i] == White)
            {
                throw new InvalidDataException(
                    $"NIF scene graph contains a cycle through node {i}.");
            }
        }
    }
}
