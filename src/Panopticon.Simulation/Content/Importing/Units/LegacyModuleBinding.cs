namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Reference to a native module that only the Generals adapter can build (decision D1). The handle
/// indexes an adapter-owned table of already-constructed native module data, so no parser objects
/// cross the interchange. The validator rejects it from any other source and for families outside
/// the legacy allowlist.
/// </summary>
public sealed record LegacyModuleBinding(ContentSource Owner, int Handle);
