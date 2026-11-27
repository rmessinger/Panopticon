#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Terrain;

/// <summary>
/// Builds the level deck of one generated bridge in route-local coordinates (the route runs along +Y
/// from 0 to its length). Pieces are re-placed at native scale, the last full span is copied, shifted,
/// trimmed to the remaining length and capped, and the roadway mean is moved to the datum. Render
/// attributes follow each triangle: copied faces keep their source normals and texture coordinates,
/// cut vertices interpolate them, and caps take a planar mapping. Layouts that cannot be built return
/// null with a reason.
/// </summary>
internal static class GeneratedBridgeLayoutBuilder
{
    /// <summary>Source face index of cap triangles; outside the roadway index range.</summary>
    private const int CapSourceFaceBase = 900;

    /// <summary>Trimmed lengths at or below this are treated as an exact fit with no trimmed piece.</summary>
    private const double ZeroTrimLength = 1e-6;

    private const string RouteShorterMessage = "route shorter than the two end pieces";

    private static readonly (int From, int To, double T)[] IdentityProvenance = [(0, 0, 0d), (1, 1, 0d), (2, 2, 0d)];

    internal static GeneratedBridgeLayout? TryBuild(GeneratedBridgeLayoutInput input, out string? unsupportedReason)
    {
        ArgumentNullException.ThrowIfNull(input);
        unsupportedReason = null;

        var route = input.Route;

        if (!route.IsValid)
        {
            unsupportedReason = "route is degenerate";
            return null;
        }

        if (!(input.LengthLeft > 0d && input.LengthSpan > 0d && input.LengthRight > 0d))
        {
            unsupportedReason = "piece lengths must be positive";
            return null;
        }

        if (input.RawFaces.Count == 0 || string.IsNullOrEmpty(input.LeftPieceName) || string.IsNullOrEmpty(input.SpanPieceName) || string.IsNullOrEmpty(input.RightPieceName))
        {
            unsupportedReason = "raw faces or piece names missing";
            return null;
        }

        var interior = route.Length - input.LengthLeft - input.LengthRight;

        if (interior < 0d)
        {
            unsupportedReason = RouteShorterMessage;
            return null;
        }

        var fullSpans = (int)Math.Floor(interior / input.LengthSpan);
        var trimmedLength = interior - (fullSpans * input.LengthSpan);

        // Floating point can leave the remainder a hair negative after the floor; one span moves into the trim.
        if (trimmedLength < 0d)
        {
            fullSpans--;
            trimmedLength += input.LengthSpan;
        }

        if (fullSpans < 0)
        {
            unsupportedReason = RouteShorterMessage;
            return null;
        }

        var toLocal = route.WorldToLocal;
        var fromLocal = route.LocalToWorld;
        var authoredLocal = input.AuthoredWorldMatrix * toLocal;

        if (!Matrix4x4.Invert(authoredLocal, out var inverseAuthored))
        {
            unsupportedReason = "authored placement is singular";
            return null;
        }

        var level = Matrix4x4.CreateScale(input.BridgeScale) * Matrix4x4.CreateFromQuaternion(QuaternionUtility.CreateRotation(Vector3.UnitX, Vector3.UnitY));
        var a0 = inverseAuthored * level;
        var rightMatrix = a0 * Matrix4x4.CreateTranslation(0f, (float)trimmedLength, 0f);

        var drafts = new List<Draft>(input.RawFaces.Count);

        for (var index = 0; index < input.RawFaces.Count; index++)
        {
            var face = input.RawFaces[index];
            var matrix = IsRight(face.PieceName, input) ? rightMatrix : a0;
            drafts.Add(new Draft(
                face.PieceName,
                face.CopyIndex,
                face.SourceFaceIndex,
                face.MeshPartIndex,
                face.MaterialIndex,
                GeneratedBridgeFaceKind.Original,
                [
                    ToDouble(Vector3.Transform(Vector3.Transform(face.A, toLocal), matrix)),
                    ToDouble(Vector3.Transform(Vector3.Transform(face.B, toLocal), matrix)),
                    ToDouble(Vector3.Transform(Vector3.Transform(face.C, toLocal), matrix)),
                ],
                index,
                IdentityProvenance));
        }

        var cloneSource = drafts
            .Where(draft => string.Equals(draft.Piece, input.SpanPieceName, StringComparison.OrdinalIgnoreCase) && draft.Copy == fullSpans - 1)
            .ToList();
        var trimmed = trimmedLength > ZeroTrimLength;

        if (cloneSource.Count == 0 && (fullSpans >= 1 || trimmed))
        {
            unsupportedReason = "no span piece to trim";
            return null;
        }

        var keptIntact = 0;
        var clippedFaces = 0;
        var clippedTriangles = 0;
        var droppedFaces = 0;
        var cutAnomalies = 0;
        var trimmedPieceFaces = 0;
        var clipDrafts = new List<Draft>();
        var caps = new GeneratedBridgeCaps
        {
            Loops = 0,
            OpenChains = 0,
            NonManifoldNodes = 0,
            Area = 0d,
            Triangles = [],
        };

        if (trimmed)
        {
            var copies = cloneSource
                .Select(draft => draft with
                {
                    Copy = fullSpans,
                    Vertices = draft.Vertices.Select(vertex => (vertex.X, vertex.Y + input.LengthSpan, vertex.Z)).ToArray(),
                })
                .ToList();
            trimmedPieceFaces = copies.Count;
            var yClip = copies.SelectMany(copy => copy.Vertices).Min(vertex => vertex.Y) + trimmedLength;
            var cutSegments = new List<((double X, double Z) A, (double X, double Z) B)>();

            foreach (var copy in copies)
            {
                var insideCount = copy.Vertices.Count(vertex => vertex.Y <= yClip);

                if (insideCount == 3)
                {
                    keptIntact++;
                    clipDrafts.Add(copy);
                    continue;
                }

                if (insideCount == 0)
                {
                    droppedFaces++;
                    continue;
                }

                clippedFaces++;
                var crossings = new List<(double X, double Z)>();
                var polygon = GeneratedBridgeTrimGeometry.ClipBelowPlaneWithSources(copy.Vertices, yClip, crossings);

                if (crossings.Count == 2)
                {
                    cutSegments.Add((crossings[0], crossings[1]));
                }
                else
                {
                    cutAnomalies++;
                }

                for (var k = 1; k + 1 < polygon.Count; k++)
                {
                    var corners = new[] { polygon[0], polygon[k], polygon[k + 1] };
                    clipDrafts.Add(new Draft(copy.Piece, copy.Copy, copy.Source, copy.Part, copy.Material, GeneratedBridgeFaceKind.Clipped,
                        corners.Select(corner => corner.Position).ToArray(), copy.RawIndex,
                        corners.Select(corner => (corner.From, corner.To, corner.T)).ToArray()));
                    clippedTriangles++;
                }
            }

            caps = GeneratedBridgeTrimGeometry.BuildCaps(cutSegments, yClip);

            if (caps.OpenChains > 0 || caps.NonManifoldNodes > 0 || cutAnomalies > 0)
            {
                unsupportedReason = "the trimmed piece's cut does not close";
                return null;
            }
        }

        var all = new List<Draft>(drafts.Count + clipDrafts.Count + caps.Triangles.Count);
        all.AddRange(drafts);
        all.AddRange(clipDrafts);

        foreach (var triangle in caps.Triangles)
        {
            all.Add(new Draft(
                input.SpanPieceName,
                fullSpans,
                CapSourceFaceBase + triangle.LoopIndex,
                0,
                0,
                GeneratedBridgeFaceKind.Cap,
                [triangle.A, triangle.B, triangle.C],
                -1,
                IdentityProvenance));
        }

        // Datum: the mean Z of the roadway vertices lands on DatumZ; every triangle moves with it.
        var roadwayVertices = all.Where(IsRoadway).SelectMany(draft => draft.Vertices).ToList();

        if (roadwayVertices.Count == 0)
        {
            unsupportedReason = "no roadway faces";
            return null;
        }

        var datumShift = input.DatumZ - roadwayVertices.Average(vertex => vertex.Z);
        var placed = all
            .Select(draft => draft with
            {
                Vertices = draft.Vertices.Select(vertex => (vertex.X, vertex.Y, vertex.Z + datumShift)).ToArray(),
            })
            .ToList();

        var sourceAttributes = ResolveSourceAttributes(input, toLocal, a0, rightMatrix, out var attributeReason);

        if (sourceAttributes == null)
        {
            unsupportedReason = attributeReason;
            return null;
        }

        var faces = new List<BridgeSurface.BridgeRawFace>(placed.Count);
        var kinds = new List<GeneratedBridgeFaceKind>(placed.Count);
        var vertexAttributes = new List<GeneratedBridgeVertexAttributes>(placed.Count * 3);

        foreach (var draft in placed)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                vertexAttributes.Add(WorldAttributes(draft, corner, sourceAttributes, fromLocal));
            }

            faces.Add(new BridgeSurface.BridgeRawFace(
                draft.Piece,
                draft.Copy,
                draft.Source,
                draft.Part,
                draft.Material,
                ToWorld(draft.Vertices[0], fromLocal),
                ToWorld(draft.Vertices[1], fromLocal),
                ToWorld(draft.Vertices[2], fromLocal)));
            kinds.Add(draft.Kind);
        }

        var roadwayDrafts = placed.Where(IsRoadway).ToList();
        var roadwayLocal = roadwayDrafts.SelectMany(draft => draft.Vertices).ToList();
        var roadway = faces.Where(face => IsRoadwaySource(face.SourceFaceIndex)).ToList();

        var trim = new GeneratedBridgeTrimFacts
        {
            LengthLeft = input.LengthLeft,
            LengthSpan = input.LengthSpan,
            LengthRight = input.LengthRight,
            TrimmedLength = trimmedLength,
            TrimAmount = input.LengthSpan - trimmedLength,
            CapArea = caps.Area,
            FullSpans = fullSpans,
            TrimmedPieceFaces = trimmedPieceFaces,
            KeptIntact = keptIntact,
            ClippedFaces = clippedFaces,
            ClippedOutputTriangles = clippedTriangles,
            DroppedFaces = droppedFaces,
            CapLoops = caps.Loops,
            CapOpenChains = caps.OpenChains,
            CapNonManifoldNodes = caps.NonManifoldNodes,
            CapTriangles = caps.Triangles.Count,
        };

        return new GeneratedBridgeLayout
        {
            Route = route,
            DatumZ = input.DatumZ,
            Faces = faces,
            FaceKinds = kinds,
            VertexAttributes = vertexAttributes,
            Roadway = roadway,
            RoadwayStartS = roadwayLocal.Min(vertex => vertex.Y),
            RoadwayEndS = roadwayLocal.Max(vertex => vertex.Y),
            RoadwayMinN = roadwayLocal.Min(vertex => vertex.X),
            RoadwayMaxN = roadwayLocal.Max(vertex => vertex.X),
            Trim = trim,
        };
    }

    /// <summary>
    /// Route-local attributes of every raw face's three vertices: the source normal through the inverse
    /// transpose of the full chain (piece, route, placement), and the texture coordinates of the face's
    /// mesh part. Null with a reason when a source is missing or degenerate.
    /// </summary>
    private static GeneratedBridgeVertexAttributes[][]? ResolveSourceAttributes(
        GeneratedBridgeLayoutInput input,
        Matrix4x4 toLocal,
        Matrix4x4 a0,
        Matrix4x4 rightMatrix,
        out string? reason)
    {
        reason = null;

        if (input.PieceMatrices.Count != input.RawFaces.Count)
        {
            reason = "piece matrices missing";
            return null;
        }

        var result = new GeneratedBridgeVertexAttributes[input.RawFaces.Count][];

        for (var index = 0; index < input.RawFaces.Count; index++)
        {
            var face = input.RawFaces[index];

            if (!input.SourceMeshes.TryGetValue(face.PieceName, out var mesh))
            {
                reason = $"no source mesh for piece {face.PieceName}";
                return null;
            }

            var firstIndex = (face.SourceFaceIndex * 3);

            if (face.SourceFaceIndex < 0 || firstIndex + 2 >= mesh.Indices.Count || face.MeshPartIndex < 0 || face.MeshPartIndex >= mesh.MeshParts.Count)
            {
                reason = $"source face outside mesh for piece {face.PieceName}";
                return null;
            }

            var part = mesh.MeshParts[face.MeshPartIndex];
            var matrix = IsRight(face.PieceName, input) ? rightMatrix : a0;
            var chain = input.PieceMatrices[index] * toLocal * matrix;

            if (!Matrix4x4.Invert(chain, out var inverseChain))
            {
                reason = "normal transform is singular";
                return null;
            }

            var normalMatrix = Matrix4x4.Transpose(inverseChain);
            var corners = new GeneratedBridgeVertexAttributes[3];

            for (var corner = 0; corner < 3; corner++)
            {
                var vertexIndex = mesh.Indices[firstIndex + corner];
                var normal = Vector3.TransformNormal(mesh.Vertices[vertexIndex].Normal, normalMatrix);

                if (normal.LengthSquared() <= float.Epsilon)
                {
                    reason = $"degenerate source normal for piece {face.PieceName}";
                    return null;
                }

                corners[corner] = new GeneratedBridgeVertexAttributes(
                    Vector3.Normalize(normal),
                    TextureAt(part.TextureCoordinates0, vertexIndex),
                    TextureAt(part.TextureCoordinates1, vertexIndex));
            }

            result[index] = corners;
        }

        return result;
    }

    private static Vector2? TextureAt(IReadOnlyList<Vector2>? coordinates, int vertexIndex)
    {
        return coordinates is { } values ? values[vertexIndex] : null;
    }

    /// <summary>Route-local attributes of one draft corner, before the world rotation.</summary>
    private static GeneratedBridgeVertexAttributes LocalAttributes(Draft draft, int corner, GeneratedBridgeVertexAttributes[][] sourceAttributes)
    {
        if (draft.Kind == GeneratedBridgeFaceKind.Cap)
        {
            return new GeneratedBridgeVertexAttributes(Vector3.UnitY, null, null);
        }

        var (from, to, t) = draft.Provenance[corner];
        var source = sourceAttributes[draft.RawIndex];

        return from == to ? source[from] : GeneratedBridgeVertexAttributes.Lerp(source[from], source[to], t);
    }

    private static GeneratedBridgeVertexAttributes WorldAttributes(Draft draft, int corner, GeneratedBridgeVertexAttributes[][] sourceAttributes, Matrix4x4 fromLocal)
    {
        var local = LocalAttributes(draft, corner, sourceAttributes);
        var normal = Vector3.Normalize(Vector3.TransformNormal(local.Normal, fromLocal));

        if (draft.Kind != GeneratedBridgeFaceKind.Cap)
        {
            return new GeneratedBridgeVertexAttributes(normal, local.Uv0, local.Uv1);
        }

        // Caps sit inside the joint between the trimmed span and the end piece, so a planar mapping of the
        // final route-local position is enough.
        var vertex = draft.Vertices[corner];
        return new GeneratedBridgeVertexAttributes(normal, new Vector2((float)(vertex.X / 10d), (float)(vertex.Z / 10d)), null);
    }

    private static bool IsRight(string pieceName, GeneratedBridgeLayoutInput input)
    {
        return string.Equals(pieceName, input.RightPieceName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRoadwaySource(int sourceFaceIndex)
    {
        return sourceFaceIndex >= BridgeSurface.RoadwayFirstFaceIndex && sourceFaceIndex <= BridgeSurface.RoadwayLastFaceIndex;
    }

    private static bool IsRoadway(Draft draft) => IsRoadwaySource(draft.Source);

    private static (double X, double Y, double Z) ToDouble(Vector3 value) => (value.X, value.Y, value.Z);

    private static Vector3 ToWorld((double X, double Y, double Z) vertex, Matrix4x4 fromLocal)
    {
        return Vector3.Transform(new Vector3((float)vertex.X, (float)vertex.Y, (float)vertex.Z), fromLocal);
    }

    /// <summary>
    /// One triangle in route-local double precision. <c>RawIndex</c> names the raw face it copies (-1 for caps);
    /// <c>Provenance</c> names, per corner, the raw corner it comes from and the interpolation parameter.
    /// </summary>
    private sealed record Draft(
        string Piece,
        int Copy,
        int Source,
        int Part,
        int Material,
        GeneratedBridgeFaceKind Kind,
        (double X, double Y, double Z)[] Vertices,
        int RawIndex,
        (int From, int To, double T)[] Provenance);
}
