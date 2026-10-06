#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Model and per-state animation keys for one unit visual. Keys are source asset paths resolved
/// against the source file system by Track C importers, not runtime content names.
/// </summary>
public sealed record VisualStateRecord : SourceRecord
{
    public static readonly IReadOnlyList<string> RequiredStates = ["Idle", "Move", "Attack", "Death"];

    private readonly IReadOnlyDictionary<string, Authored<string>> _stateAnimations = new Dictionary<string, Authored<string>>();

    public VisualStateRecord(ContentId id, Provenance provenance)
        : base(id, ContentKind.VisualState, provenance)
    {
    }

    public Authored<string> ModelKey { get; init; }

    /// <summary>Animation key per engine state name (<see cref="RequiredStates"/> plus optional extras).</summary>
    public IReadOnlyDictionary<string, Authored<string>> StateAnimations
    {
        get => _stateAnimations;
        init => _stateAnimations = new Dictionary<string, Authored<string>>(value, StringComparer.OrdinalIgnoreCase);
    }

    public override IEnumerable<ContentReference> GetReferences() => [];

    internal override SourceRecord InheritFrom(SourceRecord? parent)
    {
        var p = parent as VisualStateRecord;
        var merged = new Dictionary<string, Authored<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (state, animation) in p?.StateAnimations ?? new Dictionary<string, Authored<string>>())
        {
            merged[state] = animation;
        }

        foreach (var (state, animation) in StateAnimations)
        {
            if (animation.IsPresent)
            {
                merged[state] = animation.WithOrigin(Id);
            }
        }

        return this with
        {
            Parent = null,
            Unclassified = MergeUnclassified(parent),
            ModelKey = Authored<string>.Inherit(ModelKey.WithOrigin(Id), p?.ModelKey ?? default),
            StateAnimations = merged,
        };
    }
}
