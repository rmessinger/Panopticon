using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// The identifier-to-implementation registry for experiments: the sole place a seam
/// implementation is registered and resolved for experiment cells. Definitions validate
/// against a registry before anything runs, so unknown seams, unknown implementation ids,
/// and duplicate or incompatible bindings fail before a benchmark starts.
/// </summary>
public sealed class ExperimentImplementationRegistry
{
    private readonly Dictionary<(string Seam, string Id), ImplementationDescriptor> _descriptors = [];

    public IReadOnlyList<ImplementationDescriptor> Descriptors => [.. _descriptors.Values];

    /// <summary>The registry seeded with the landed seam implementations.</summary>
    public static ExperimentImplementationRegistry CreateDefault()
    {
        var registry = new ExperimentImplementationRegistry();
        registry.Register(AiPolicyExperimentImplementations.RuleBased());
        registry.Register(AiPolicyExperimentImplementations.Neural());
        registry.Register(ModelImportExperimentImplementations.W3d());
        registry.Register(ModelImportExperimentImplementations.Gltf());
        return registry;
    }

    public void Register(ImplementationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!_descriptors.TryAdd((descriptor.Seam, descriptor.Id), descriptor))
        {
            throw new ArgumentException(
                $"Implementation '{descriptor.Id}' is already registered for seam '{descriptor.Seam}'.");
        }
    }

    public ImplementationDescriptor Resolve(string seam, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seam);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (!_descriptors.TryGetValue((seam, id), out var descriptor))
        {
            throw new ArgumentException(
                $"No implementation '{id}' is registered for seam '{seam}'; registered: {DescribeSeam(seam)}.");
        }

        return descriptor;
    }

    public bool TryResolve(string seam, string id, out ImplementationDescriptor? descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seam);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_descriptors.TryGetValue((seam, id), out var found))
        {
            descriptor = found;
            return true;
        }

        descriptor = null;
        return false;
    }

    /// <summary>
    /// Fails with an explicit error when the definition names a seam with no registrations, a
    /// seam more than once, an unknown implementation id, or an implementation id more than
    /// once within its axis.
    /// </summary>
    public void ValidateDefinition(ExperimentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var seenSeams = new HashSet<string>(StringComparer.Ordinal);
        foreach (var axis in definition.Axes)
        {
            if (string.IsNullOrWhiteSpace(axis.Seam))
            {
                throw new ArgumentException("Experiment axes must name a seam.");
            }

            if (!seenSeams.Add(axis.Seam))
            {
                throw new ArgumentException($"Seam '{axis.Seam}' is swept by more than one axis.");
            }

            var known = _descriptors.Values.Where(d => d.Seam == axis.Seam).ToList();
            if (known.Count == 0)
            {
                throw new ArgumentException(
                    $"No implementation is registered for seam '{axis.Seam}'; known seams: {DescribeSeams()}.");
            }

            var seenImplementations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in axis.Implementations)
            {
                if (!seenImplementations.Add(id))
                {
                    throw new ArgumentException(
                        $"Implementation '{id}' appears more than once in the '{axis.Seam}' axis.");
                }

                Resolve(axis.Seam, id);
            }
        }
    }

    private string DescribeSeam(string seam)
    {
        var ids = string.Join(", ", _descriptors.Values.Where(d => d.Seam == seam).Select(d => d.Id));
        return ids.Length == 0 ? "none" : ids;
    }

    private string DescribeSeams()
    {
        var seams = string.Join(", ", _descriptors.Values.Select(d => d.Seam).Distinct().OrderBy(s => s));
        return seams.Length == 0 ? "none" : seams;
    }
}
