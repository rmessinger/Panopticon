using System;
using System.IO;
using System.Text;
using OpenSage.Data.Utilities.Extensions;
using OpenSage.FileFormats;
using OpenSage.Network;

namespace OpenSage.Data.Rep;

public sealed class ReplayMetadata
{
    public int MapFileUnknownInt { get; private set; }
    public string MapFile { get; private set; }
    public int MapCrc { get; private set; }
    public int MapSize { get; private set; }

    // Might be seed. Seems to influence the color and faction when set to random
    public int SD { get; private set; }

    public int C { get; private set; }

    public int SR { get; private set; }

    public int StartingCredits { get; private set; }

    public string O { get; private set; }

    public ReplaySlot[] Slots { get; private set; }

    public static ReplayMetadata Create(
        string mapFile,
        int mapCrc,
        int mapSize,
        int seed,
        int startingCredits,
        ReplaySlot[] slots) => new ReplayMetadata
        {
            MapFileUnknownInt = 0,
            MapFile = mapFile,
            MapCrc = mapCrc,
            MapSize = mapSize,
            SD = seed,
            C = 0,
            SR = 0,
            StartingCredits = startingCredits,
            O = string.Empty,
            Slots = slots
        };

    internal static ReplayMetadata Parse(BinaryReader reader)
    {
        var raw = reader.ReadNullTerminatedAsciiString();
        var rawSplit = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        var result = new ReplayMetadata();

        foreach (var rawEntry in rawSplit)
        {
            var keyValue = rawEntry.Split(new[] { '=' }, 2);

            if (keyValue.Length < 2 || string.IsNullOrEmpty(keyValue[0]))
                continue;

            switch (keyValue[0])
            {
                case "US":
                    break;

                case "M":
                    result.MapFileUnknownInt = Convert.ToInt32(keyValue[1].Substring(0, 2));
                    result.MapFile = keyValue[1].Substring(2);
                    break;

                case "MC":
                    result.MapCrc = Convert.ToInt32(keyValue[1], 16);
                    break;

                case "MS":
                    result.MapSize = Convert.ToInt32(keyValue[1]);
                    break;

                case "SD":
                    result.SD = Convert.ToInt32(keyValue[1]);
                    break;

                case "C":
                    result.C = Convert.ToInt32(keyValue[1]);
                    break;

                case "SR":
                    result.SR = Convert.ToInt32(keyValue[1]);
                    break;

                case "SC":
                    result.StartingCredits = Convert.ToInt32(keyValue[1]);
                    break;

                case "O":
                    result.O = keyValue[1];
                    break;

                case "S":
                    var slots = keyValue[1].Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                    result.Slots = new ReplaySlot[slots.Length];
                    for (var i = 0; i < slots.Length; i++)
                    {
                        result.Slots[i] = ReplaySlot.Parse(slots[i]);
                    }

                    break;

                default:
                    // Unknown keys are silently skipped — replay files from different
                    // game versions may contain keys this parser does not recognise.
                    break;
            }
        }

        return result;
    }

    internal void Write(BinaryWriter writer)
    {
        var sb = new StringBuilder();

        sb.Append($"M={MapFileUnknownInt:D2}{MapFile};");
        sb.Append($"MC={MapCrc:X};");
        sb.Append($"MS={MapSize};");
        sb.Append($"SD={SD};");
        sb.Append($"C={C};");
        sb.Append($"SR={SR};");
        sb.Append($"SC={StartingCredits};");
        if (!string.IsNullOrEmpty(O))
        {
            sb.Append($"O={O};");
        }

        if (Slots != null && Slots.Length > 0)
        {
            sb.Append("S=");
            foreach (var slot in Slots)
            {
                sb.Append(slot.Encode());
                sb.Append(':');
            }

            sb.Append(';');
        }

        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        writer.Write(bytes);
        writer.Write((byte)0); // null terminator
    }
}

public sealed class ReplaySlot
{
    public ReplaySlotType SlotType { get; private set; }

    public string HumanName { get; private set; }
    public ReplaySlotDifficulty? ComputerDifficulty { get; private set; }

    public sbyte Color { get; private set; }
    public int Faction { get; private set; }
    public int StartPosition { get; private set; }
    public int Team { get; private set; }

    public static ReplaySlot CreateHuman(string name, sbyte color, int faction, int startPosition, int team) =>
        new ReplaySlot
        {
            SlotType = ReplaySlotType.Human,
            HumanName = name,
            Color = color,
            Faction = faction,
            StartPosition = startPosition,
            Team = team
        };

    public static ReplaySlot CreateComputer(ReplaySlotDifficulty difficulty, sbyte color, int faction, int startPosition, int team) =>
        new ReplaySlot
        {
            SlotType = ReplaySlotType.Computer,
            ComputerDifficulty = difficulty,
            Color = color,
            Faction = faction,
            StartPosition = startPosition,
            Team = team
        };

    public static ReplaySlot CreateEmpty() => new ReplaySlot { SlotType = ReplaySlotType.Empty };

    // HDESKTOP-J8EU7T4,0,0,TT,-1,2,-1,-1,1:
    // CH,-1,-1,-1,-1:
    // CH,-1,-1,-1,-1:
    // CH,-1,-1,-1,-1:
    // X:
    // X:
    // X:
    // X:
    internal static ReplaySlot Parse(string raw)
    {
        var result = new ReplaySlot();

        ReplaySlotType getSlotType()
        {
            switch (raw[0])
            {
                case 'H':
                    return ReplaySlotType.Human;
                case 'C':
                    return ReplaySlotType.Computer;
                case 'X':
                case 'O': // slot is open, but still empty (multiplayer online)
                    return ReplaySlotType.Empty;
                default:
                    throw new InvalidDataException();
            }
        }

        ReplaySlotDifficulty getSlotDifficulty()
        {
            switch (raw[1])
            {
                case 'E':
                    return ReplaySlotDifficulty.Easy;
                case 'M':
                    return ReplaySlotDifficulty.Medium;
                case 'H':
                    return ReplaySlotDifficulty.Hard;
                default:
                    throw new InvalidDataException();
            }
        }

        result.SlotType = getSlotType();

        var slotDetails = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        switch (result.SlotType)
        {
            case ReplaySlotType.Human:
                result.HumanName = slotDetails[0].Substring(1);
                // TODO: 1, 2, 3, 4
                result.Color = Convert.ToSByte(slotDetails[4]);
                result.Faction = Convert.ToInt32(slotDetails[5]);
                result.StartPosition = Convert.ToInt32(slotDetails[6]);
                result.Team = Convert.ToInt32(slotDetails[7]);
                // TODO: 8
                break;

            case ReplaySlotType.Computer:
                result.ComputerDifficulty = getSlotDifficulty();
                result.Color = Convert.ToSByte(slotDetails[1]);
                result.Faction = Convert.ToInt32(slotDetails[2]);
                result.StartPosition = Convert.ToInt32(slotDetails[3]);
                result.Team = Convert.ToInt32(slotDetails[4]);
                break;

            case ReplaySlotType.Empty:
                break;
        }

        return result;
    }

    internal string Encode()
    {
        return SlotType switch
        {
            ReplaySlotType.Human =>
                // H<name>,0,0,TT,<color>,<faction>,<startpos>,<team>,1
                $"H{HumanName},0,0,TT,{Color},{Faction},{StartPosition},{Team},1",
            ReplaySlotType.Computer =>
                // C<diff>,<color>,<faction>,<startpos>,<team>
                $"C{DifficultyChar()},{Color},{Faction},{StartPosition},{Team}",
            _ => "X"
        };
    }

    private char DifficultyChar() => ComputerDifficulty switch
    {
        ReplaySlotDifficulty.Easy => 'E',
        ReplaySlotDifficulty.Medium => 'M',
        ReplaySlotDifficulty.Hard => 'H',
        _ => 'E'
    };
}

public enum ReplaySlotType
{
    Human,
    Computer,
    Empty
}

public enum ReplaySlotDifficulty
{
    Easy,
    Medium,
    Hard
}
