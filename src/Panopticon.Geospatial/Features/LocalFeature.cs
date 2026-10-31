using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Features;

/// <summary>
/// One clipped piece of a source feature in local game coordinates (B4b deep dive 5.3).
/// Geometry positions are game units relative to the map grid's southwest origin:
/// X is game +X (projected easting) and Y is game +Y (projected northing). They are
/// local map coordinates, not geographic degrees and not projected meters. A position's
/// AltitudeMeters retains the unconverted source altitude (interpolated on line vertices
/// created by clipping, zero on polygon vertices the clipper introduces); feature mapping
/// samples terrain Z instead, and that Z dominates.
/// </summary>
/// <param name="Geometry">Clipped geometry in local game coordinates.</param>
/// <param name="SourceFeature">
/// The original feature; source id, feature id, attributes, altitude mode, and provenance
/// stay attached through it.
/// </param>
/// <param name="PartId">
/// Stable identity of this clipped part, distinct from the source feature id:
/// SourceId/FeatureId#PartIndex, indexed from 0 in deterministic clipped order. An
/// unsplit feature keeps part index 0.
/// </param>
public sealed record LocalFeature(GeoGeometry Geometry, GeospatialFeature SourceFeature, string PartId);
