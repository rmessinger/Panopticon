using System.Collections.Generic;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiControllerSequence at 10.1.0.0: a named clip with an accumulation-root
/// name, one text-key block, and controlled blocks pairing target node names
/// with keyframe controllers. KF files carry no node blocks, so targets pair
/// by name only.
/// </summary>
public sealed record NifControllerSequence : NifBlock
{
    public string Name { get; }
    public string AccumulationRootName { get; }
    public int TextKeysReference { get; }
    public IReadOnlyList<NifControlledBlock> ControlledBlocks { get; }

    private NifControllerSequence(NifBinaryReader reader)
    {
        Name = reader.ReadSizedString("Name");
        AccumulationRootName = reader.ReadSizedString("AccumulationRootName");
        TextKeysReference = reader.ReadBlockReference("TextKeys");
        var count = reader.ReadElementCount(4, "ControlledBlocks.Count");
        var blocks = new NifControlledBlock[count];
        for (var i = 0; i < count; i++)
        {
            blocks[i] = new NifControlledBlock(
                reader.ReadSizedString($"ControlledBlocks[{i}].TargetName"),
                reader.ReadBlockReference($"ControlledBlocks[{i}].Controller"));
        }

        ControlledBlocks = blocks;
    }

    internal static NifControllerSequence Read(NifBinaryReader reader)
    {
        return new NifControllerSequence(reader);
    }
}

/// <summary>
/// One controlled block: the animated node's name and its controller
/// reference.
/// </summary>
public sealed record NifControlledBlock(string TargetName, int ControllerReference);
