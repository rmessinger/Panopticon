#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

public enum ListOverrideMode
{
    /// <summary>Nothing authored; the parent list carries over unchanged.</summary>
    Inherit,
    /// <summary>The authored list replaces the parent list, including an explicit empty list.</summary>
    Replace,
    /// <summary>The parent list is kept with explicit additions and removals.</summary>
    Edit,
}

/// <summary>An authored list edit. Items are copied on construction, so callers cannot mutate them afterwards.</summary>
public sealed class ListOverride<T>
{
    public static readonly ListOverride<T> Inherit = new(ListOverrideMode.Inherit, [], []);

    public ListOverrideMode Mode { get; }

    /// <summary>Replacement items for <see cref="ListOverrideMode.Replace"/>; additions for <see cref="ListOverrideMode.Edit"/>.</summary>
    public IReadOnlyList<T> Items { get; }

    public IReadOnlyList<T> Removals { get; }

    private ListOverride(ListOverrideMode mode, IEnumerable<T> items, IEnumerable<T> removals)
    {
        Mode = mode;
        Items = items.ToArray();
        Removals = removals.ToArray();
    }

    public static ListOverride<T> Replace(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ListOverride<T>(ListOverrideMode.Replace, items, []);
    }

    public static ListOverride<T> Edit(IEnumerable<T> additions, IEnumerable<T> removals)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);
        return new ListOverride<T>(ListOverrideMode.Edit, additions, removals);
    }

    /// <summary>Applies this edit over the parent's resolved list and returns a resolved (Replace) list.</summary>
    internal ListOverride<T> ApplyOver(ListOverride<T> parent, IEqualityComparer<T>? comparer = null)
    {
        var parentItems = parent.Mode == ListOverrideMode.Inherit ? [] : parent.Items;

        switch (Mode)
        {
            case ListOverrideMode.Replace:
                return this;
            case ListOverrideMode.Inherit:
                return parent.Mode == ListOverrideMode.Inherit ? this : Replace(parentItems);
            default:
                comparer ??= EqualityComparer<T>.Default;
                var result = parentItems.Where(item => !Removals.Contains(item, comparer)).ToList();
                result.AddRange(Items);
                return Replace(result);
        }
    }

    /// <summary>The resolved items; an unauthored list resolves to empty.</summary>
    public IReadOnlyList<T> Resolved => Mode == ListOverrideMode.Replace ? Items : [];
}
