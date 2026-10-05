#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units.Ee2;

/// <summary>Location of a DDF token; line and column are 1-based.</summary>
internal readonly record struct DdfPosition(string File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}

/// <summary>A parsed DDF value: a scalar, a block of members, or an array of values.</summary>
internal abstract record DdfValue(DdfPosition Position);

internal sealed record DdfScalar(string Text, DdfPosition Position) : DdfValue(Position);

internal sealed record DdfArray(IReadOnlyList<DdfValue> Items, DdfPosition Position) : DdfValue(Position);

/// <summary>A brace block. <paramref name="Name"/> is set for named blocks such as <c>Attack { ... }</c> inside arrays.</summary>
internal sealed record DdfBlock(string? Name, IReadOnlyList<DdfMember> Members, DdfPosition Position) : DdfValue(Position)
{
    /// <summary>Last member with the key, matching DDF's case-insensitive keys and last-wins assignment.</summary>
    public DdfValue? Get(string key) => Members.LastOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    public string? GetText(string key) => (Get(key) as DdfScalar)?.Text;
}

internal sealed record DdfMember(string Key, DdfValue Value, DdfPosition Position);

/// <summary>A top-level record such as <c>UnitType Human { ... }</c>.</summary>
internal sealed record DdfRecord(string Type, string Name, DdfBlock Body, DdfPosition Position)
{
    public string? Parent => Body.GetText("parent");
}
