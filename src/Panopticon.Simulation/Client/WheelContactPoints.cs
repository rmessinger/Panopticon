#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;

namespace Panopticon.Client;

/// <summary>
/// Four tyre contact points of a drawable's model, in the model instance's space (after bone
/// animation, before the object's world matrix): the points the measured tyre fit used. Render
/// only; nothing here reads or writes logic state. Vehicles without exactly four tyres (tracked,
/// motorcycles, six-wheel trucks) get no result and keep the existing pose. One instance belongs
/// to one drawable and caches its CPU-imported tyre meshes per model.
/// </summary>
internal sealed class WheelContactPoints
{
    private const int TyreCount = 4;

    /// <summary>Vertices within this height of the tyre's lowest instance-space vertex form its contact band.</summary>
    private const float ContactBandHeight = 0.5f;

    private const int MinimumBandVertices = 3;

    private const float MinimumContactSpacing = 1e-3f;

    /// <summary>Largest distance from the spanning line at which four contacts still count as collinear.</summary>
    private const float CollinearTolerance = 1e-2f;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private Model? _resolvedFor;

    private IReadOnlyList<TyreMesh> _tyres = [];

    // A failed import is permanent for this drawable: later calls return no contacts without retrying.
    private bool _importFailed;

    /// <summary>
    /// Writes the four tyre contacts (first four entries of <paramref name="contacts"/>) and returns true,
    /// or returns false and writes nothing when the model has no valid set of exactly four tyres.
    /// </summary>
    public bool TryGetContacts(ModelInstance? modelInstance, AssetLoadContext loadContext, Span<Vector3> contacts)
    {
        ArgumentNullException.ThrowIfNull(loadContext);
        ArgumentOutOfRangeException.ThrowIfLessThan(contacts.Length, TyreCount);

        if (modelInstance == null)
        {
            return false;
        }

        var tyres = TyresFor(modelInstance.Model, loadContext);

        if (tyres.Count != TyreCount)
        {
            return false;
        }

        var points = new Vector3[TyreCount];

        for (var index = 0; index < TyreCount; index++)
        {
            if (!TryContact(modelInstance, tyres[index], out points[index]))
            {
                return false;
            }
        }

        if (!IsValidLayout(points))
        {
            return false;
        }

        points.CopyTo(contacts);
        return true;
    }

    private IReadOnlyList<TyreMesh> TyresFor(Model model, AssetLoadContext loadContext)
    {
        if (_importFailed)
        {
            return [];
        }

        if (ReferenceEquals(_resolvedFor, model))
        {
            return _tyres;
        }

        var imported = ImportModel(model, loadContext);

        if (imported == null)
        {
            _importFailed = true;
            _tyres = [];
            return _tyres;
        }

        _tyres = SelectTyres(model, imported);
        _resolvedFor = model;
        return _tyres;
    }

    private static ImportedModel? ImportModel(Model model, AssetLoadContext loadContext)
    {
        try
        {
            var importer = new W3dModelImporter(PathResolvers.W3d);
            return importer.ImportModel(model.Name, new AssetImportContext(loadContext.FileSystem, loadContext.Language));
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, $"Wheel contacts: CPU import failed for '{model.Name}'; this drawable keeps its pose.");
            return null;
        }
    }

    private static List<TyreMesh> SelectTyres(Model model, ImportedModel imported)
    {
        var tyres = new List<TyreMesh>();

        for (var index = 0; index < model.SubObjects.Length; index++)
        {
            var subObject = model.SubObjects[index];

            if (subObject.RenderObject is not ModelMesh mesh || mesh.Skinned || mesh.Hidden)
            {
                continue;
            }

            if (!subObject.FullName.Contains("TIRE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Imported sub-objects align with Model.SubObjects by index (see Bridge.cs).
            if (index >= imported.SubObjects.Count || imported.SubObjects[index].RenderObject is not ImportedMesh importedMesh)
            {
                continue;
            }

            var positions = new Vector3[importedMesh.Vertices.Count];

            for (var vertex = 0; vertex < positions.Length; vertex++)
            {
                positions[vertex] = importedMesh.Vertices[vertex].Position;
            }

            tyres.Add(new TyreMesh(subObject.Bone?.Index ?? 0, positions));
        }

        return tyres;
    }

    private static bool TryContact(ModelInstance modelInstance, TyreMesh tyre, out Vector3 contact)
    {
        contact = default;

        if (tyre.BoneIndex < 0 || tyre.BoneIndex >= modelInstance.AbsoluteBoneTransforms.Length)
        {
            return false;
        }

        var transform = modelInstance.RelativeBoneTransforms[tyre.BoneIndex];
        var posed = new Vector3[tyre.Vertices.Length];
        var lowest = float.MaxValue;

        for (var index = 0; index < posed.Length; index++)
        {
            posed[index] = Vector3.Transform(tyre.Vertices[index], transform);
            lowest = MathF.Min(lowest, posed[index].Z);
        }

        var sum = Vector3.Zero;
        var count = 0;

        foreach (var vertex in posed)
        {
            if (vertex.Z <= lowest + ContactBandHeight)
            {
                sum += vertex;
                count++;
            }
        }

        if (count < MinimumBandVertices)
        {
            return false;
        }

        contact = sum / count;
        return true;
    }

    private static bool IsValidLayout(Vector3[] points)
    {
        for (var first = 0; first < points.Length; first++)
        {
            for (var second = first + 1; second < points.Length; second++)
            {
                if (Vector3.Distance(points[first], points[second]) < MinimumContactSpacing)
                {
                    return false;
                }
            }
        }

        // Collinear contacts leave the best-fit plane undefined: find the farthest pair and test the rest against its line.
        var farthestA = 0;
        var farthestB = 1;
        var farthest = -1f;

        for (var first = 0; first < points.Length; first++)
        {
            for (var second = first + 1; second < points.Length; second++)
            {
                var distance = Vector3.Distance(points[first], points[second]);

                if (distance > farthest)
                {
                    farthest = distance;
                    farthestA = first;
                    farthestB = second;
                }
            }
        }

        var direction = Vector3.Normalize(points[farthestB] - points[farthestA]);
        var maxOffset = points.Max(point => Vector3.Cross(point - points[farthestA], direction).Length());
        return maxOffset >= CollinearTolerance;
    }

    private sealed record TyreMesh(int BoneIndex, Vector3[] Vertices);
}
