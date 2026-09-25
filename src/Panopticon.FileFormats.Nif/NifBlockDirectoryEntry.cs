namespace Panopticon.FileFormats.Nif;

/// <summary>
/// One entry of the NIF block directory: the block's position in the file
/// and the type name it dispatches to.
/// </summary>
public sealed record NifBlockDirectoryEntry(
    int BlockIndex,
    ushort TypeIndex,
    string TypeName);
