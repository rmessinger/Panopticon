#nullable enable

using System.Collections.Generic;
using Panopticon.Content.Importing.Units.Materialization;

namespace Panopticon.Content.Importing.Units.Generals;

/// <summary>Native values the INI parser built, addressed by the handles in Generals legacy bindings.</summary>
internal sealed class GeneralsNativeTable : ILegacyNativeTable
{
    private readonly List<object> _values = [];

    public ContentSource Owner => ContentSource.Generals;

    public object? Get(int handle) => handle >= 0 && handle < _values.Count ? _values[handle] : null;

    public LegacyBinding Bind(object value)
    {
        _values.Add(value);
        return new LegacyBinding(ContentSource.Generals, _values.Count - 1);
    }
}
