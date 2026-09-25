using System.IO;
using Panopticon.Gui.Apt.ActionScript;

namespace Panopticon.Data.Apt.FrameItems;

public sealed class Action : FrameItem
{
    public InstructionCollection Instructions { get; private set; }

    public static Action Parse(BinaryReader reader)
    {
        var action = new Action();
        var instructionsPosition = reader.ReadUInt32();
        action.Instructions = InstructionCollection.Parse(reader.BaseStream, instructionsPosition);
        return action;
    }
}
