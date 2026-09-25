namespace Panopticon.FileFormats.Nif;

/// <summary>
/// One NiTriShapeData match group: vertex indices belonging to the same
/// "matched" set. Preserved for diagnostics only.
/// </summary>
public sealed record NifMatchGroup(ushort[] VertexIndices);
