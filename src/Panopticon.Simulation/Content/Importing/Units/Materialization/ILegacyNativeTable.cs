#nullable enable

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Adapter-owned native values addressed by <see cref="LegacyBinding.Handle"/> (decision D1): module
/// data for module bindings, or a native template asset for record bindings.
/// </summary>
public interface ILegacyNativeTable
{
    ContentSource Owner { get; }

    object? Get(int handle);
}
