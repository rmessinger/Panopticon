#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Blend description for two-way or multi-way terrain texture transitions.</summary>
public sealed record MapBlendDescriptionRecord(
    uint SecondaryTextureTile,
    byte[] RawBlendDirection,
    byte Flags,
    bool TwoSided,
    uint MagicValue1);
