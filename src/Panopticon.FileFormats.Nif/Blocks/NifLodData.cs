namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiLODData base at 10.1.0.0: no fields of its own (NiObject-derived, so
/// no name or controller either).
/// </summary>
public abstract record NifLodData : NifBlock;
