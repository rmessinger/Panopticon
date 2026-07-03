using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenSage.FileFormats;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;

namespace OpenSage.Data.Rep;

[DebuggerDisplay("[{Header.Timecode}]: {Order.OrderType} ({Order.Arguments.Count})")]
public sealed class ReplayChunk
{
    public ReplayChunkHeader Header { get; private set; }
    public Order Order { get; private set; }

    internal static ReplayChunk Parse(BinaryReader reader)
    {
        var result = new ReplayChunk
        {
            Header = ReplayChunkHeader.Parse(reader)
        };

        var numUniqueArgumentTypes = reader.ReadByte();

        // Pairs of {argument type, count}.
        var argumentCounts = new (OrderArgumentType argumentType, byte count)[numUniqueArgumentTypes];
        for (var i = 0; i < numUniqueArgumentTypes; i++)
        {
            argumentCounts[i] = (reader.ReadByteAsEnum<OrderArgumentType>(), reader.ReadByte());
        }

        var order = new Order((int)result.Header.Number - 1, result.Header.OrderType);
        result.Order = order;

        for (var i = 0; i < numUniqueArgumentTypes; i++)
        {
            ref var argumentCount = ref argumentCounts[i];
            var argumentType = argumentCount.argumentType;

            for (var j = 0; j < argumentCount.count; j++)
            {
                switch (argumentType)
                {
                    case OrderArgumentType.Integer:
                        order.AddIntegerArgument(reader.ReadInt32());
                        break;

                    case OrderArgumentType.Float:
                        order.AddFloatArgument(reader.ReadSingle());
                        break;

                    case OrderArgumentType.Boolean:
                        order.AddBooleanArgument(reader.ReadBooleanChecked());
                        break;

                    case OrderArgumentType.ObjectId:
                        order.AddObjectIdArgument(new ObjectId(reader.ReadUInt32()));
                        break;

                    case OrderArgumentType.Position:
                        order.AddPositionArgument(reader.ReadVector3());
                        break;

                    case OrderArgumentType.ScreenPosition:
                        order.AddScreenPositionArgument(reader.ReadPoint2D());
                        break;

                    case OrderArgumentType.ScreenRectangle:
                        order.AddScreenRectangleArgument(reader.ReadRectangle());
                        break;

                    case OrderArgumentType.Unknown4:
                        // in order to align bytes in a random replay, we needed to read 4. has to do with DrawBoxSelection
                        order.AddIntegerArgument(reader.ReadInt32());
                        // skip silently
                        break;

                    // this commented code block is here in case somebody needs to parse a replay file with argumenttype unknown10
                    /*
                    case OrderArgumentType.Unknown10:
                        // seems to be 2 bytes, has to do with OrderType 1091. TODO: check this!
                        order.AddIntegerArgument(reader.ReadInt16());
                        break;
                    */

                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Writes a single replay chunk for <paramref name="order"/> at <paramref name="timecode"/>.
    /// <paramref name="playerSlot"/> is 1-based (matching the <c>Number</c> field in the chunk header).
    /// </summary>
    internal static void Write(BinaryWriter writer, uint timecode, Order order)
    {
        // Chunk header: timecode (uint32), orderType (uint32), playerSlot 1-based (uint32)
        writer.Write(timecode);
        writer.Write((uint)order.OrderType);
        writer.Write((uint)(order.PlayerIndex + 1));

        // Group arguments by type (preserving order within each group)
        var groups = order.Arguments
            .GroupBy(a => a.ArgumentType)
            .Select(g => (type: g.Key, args: g.ToList()))
            .ToList();

        // numUniqueArgumentTypes
        writer.Write((byte)groups.Count);

        // (type, count) pairs
        foreach (var (type, args) in groups)
        {
            writer.Write((byte)type);
            writer.Write((byte)args.Count);
        }

        // argument values, grouped by type
        foreach (var (type, args) in groups)
        {
            foreach (var arg in args)
            {
                switch (type)
                {
                    case OrderArgumentType.Integer:
                        writer.Write(arg.Value.Integer);
                        break;

                    case OrderArgumentType.Float:
                        writer.Write(arg.Value.Float);
                        break;

                    case OrderArgumentType.Boolean:
                        writer.Write(arg.Value.Boolean);
                        break;

                    case OrderArgumentType.ObjectId:
                        writer.Write(arg.Value.ObjectId.Index);
                        break;

                    case OrderArgumentType.Position:
                        writer.Write(arg.Value.Position);
                        break;

                    case OrderArgumentType.ScreenPosition:
                        writer.Write(arg.Value.ScreenPosition);
                        break;

                    case OrderArgumentType.ScreenRectangle:
                        var r = arg.Value.ScreenRectangle;
                        writer.Write(r.X);
                        writer.Write(r.Y);
                        writer.Write(r.Width);
                        writer.Write(r.Height);
                        break;

                    default:
                        throw new InvalidOperationException($"Cannot write argument type {type}");
                }
            }
        }
    }
}
