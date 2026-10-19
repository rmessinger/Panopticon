#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Simulation;

/// <summary>
/// Base of the renderable primitive set (WI-A5-12): every primitive carries the stable bucket
/// key of the topology element it draws, matching T1 signature recording.
/// </summary>
public abstract record DomainVisualPrimitive(string BucketKey);

/// <summary>A positioned marker: one entity or topology node.</summary>
public sealed record DomainVisualPoint(
    string BucketKey,
    Vector3 Position,
    int OwnerIndex,
    string KindName,
    float Size = 1f) : DomainVisualPrimitive(BucketKey);

/// <summary>An open path: a graph edge, a route, or a movement trail.</summary>
public sealed record DomainVisualPolyline(
    string BucketKey,
    IReadOnlyList<Vector3> Points,
    string Category) : DomainVisualPrimitive(BucketKey);

/// <summary>A closed area: a zone, district, or region boundary.</summary>
public sealed record DomainVisualPolygon(
    string BucketKey,
    IReadOnlyList<Vector3> Points,
    string Category) : DomainVisualPrimitive(BucketKey);

/// <summary>A text label anchored in world space.</summary>
public sealed record DomainVisualLabel(string BucketKey, Vector3 Position, string Text)
    : DomainVisualPrimitive(BucketKey);

/// <summary>Describes one metric series a domain emits (WI-A5-12).</summary>
public sealed record DomainMetricDescriptor(string Series, string? Key, string Label);

/// <summary>Describes one categorical overlay option; categories are the legal values (WI-A5-12).</summary>
public sealed record DomainOverlayDescriptor(string Id, string Label, IReadOnlyList<string> Categories);
