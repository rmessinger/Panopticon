namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Reference to native data that only the Generals adapter can build (decision D1): a module's
/// data, or a whole native template for a record. The handle indexes an adapter-owned table of
/// already-constructed native values, so no parser objects cross the interchange. The validator
/// rejects bindings from any source other than the registry's legacy source.
/// </summary>
public sealed record LegacyBinding(ContentSource Owner, int Handle);
