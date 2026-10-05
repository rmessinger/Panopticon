#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Computes the required dependency closure of the requested roots over resolved records. Cycles are
/// legal only through production and prerequisite edges, where a builder can produce its builder.
/// </summary>
public sealed class UnitDependencyResolver
{
    private static readonly IReadOnlyDictionary<DependencyKind, ContentKind[]> AllowedTargets = new Dictionary<DependencyKind, ContentKind[]>
    {
        [DependencyKind.Parent] = [],
        [DependencyKind.Weapon] = [ContentKind.Weapon],
        [DependencyKind.Armor] = [ContentKind.Armor],
        [DependencyKind.Locomotor] = [ContentKind.Locomotor],
        [DependencyKind.Projectile] = [ContentKind.Projectile],
        [DependencyKind.Ability] = [ContentKind.Ability],
        [DependencyKind.Upgrade] = [ContentKind.Upgrade],
        [DependencyKind.Production] = [ContentKind.Unit],
        [DependencyKind.Prerequisite] = [ContentKind.Unit, ContentKind.Upgrade],
        [DependencyKind.Visual] = [ContentKind.VisualState],
    };

    public DependencyResult Resolve(InheritanceResult inheritance, IEnumerable<ContentId> roots, IPublishedContentLookup? published = null)
    {
        var diagnostics = new List<ImportDiagnostic>();
        var records = inheritance.Resolved;
        var closure = new List<SourceRecord>();
        var visited = new HashSet<ContentId>();
        var pending = new Queue<ContentId>();

        foreach (var root in roots)
        {
            if (records.ContainsKey(root))
            {
                pending.Enqueue(root);
            }
            else
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error, ImportStage.Resolve, DiagnosticCodes.MissingRoot,
                    $"Requested root '{root}' was not read or failed inheritance.", root));
            }
        }

        while (pending.TryDequeue(out var id))
        {
            if (!visited.Add(id))
            {
                continue;
            }

            var record = records[id];
            closure.Add(record);

            foreach (var reference in record.GetReferences())
            {
                if (!IsAllowedTarget(reference))
                {
                    diagnostics.Add(Diagnostic(ImportDiagnosticSeverity.Error, DiagnosticCodes.WrongDependencyKind,
                        $"'{record.Id}' references '{reference.Target}' as {reference.Kind}.", record));
                    continue;
                }

                if (records.ContainsKey(reference.Target))
                {
                    pending.Enqueue(reference.Target);
                }
                else if (published?.Contains(reference.Target) != true)
                {
                    diagnostics.Add(reference.IsRequired
                        ? Diagnostic(ImportDiagnosticSeverity.Error, DiagnosticCodes.MissingDependency,
                            $"'{record.Id}' requires {reference.Kind} '{reference.Target}', which was not found.", record)
                        : Diagnostic(ImportDiagnosticSeverity.Warning, DiagnosticCodes.MissingOptionalDependency,
                            $"'{record.Id}' references optional '{reference.Target}', which was not found.", record));
                }
            }
        }

        ReportForbiddenCycles(closure, records, diagnostics);

        return new DependencyResult(closure, diagnostics);
    }

    private static bool IsAllowedTarget(ContentReference reference)
    {
        return reference.Kind == DependencyKind.Presentation || AllowedTargets[reference.Kind].Contains(reference.Target.Kind);
    }

    private static void ReportForbiddenCycles(
        List<SourceRecord> closure,
        IReadOnlyDictionary<ContentId, SourceRecord> records,
        List<ImportDiagnostic> diagnostics)
    {
        var state = new Dictionary<ContentId, bool>();

        foreach (var record in closure)
        {
            Visit(record);
        }

        // Returns false once a cycle was reported, to stop repeated reports along the same path.
        bool Visit(SourceRecord record)
        {
            if (state.TryGetValue(record.Id, out var finished))
            {
                if (!finished)
                {
                    diagnostics.Add(Diagnostic(ImportDiagnosticSeverity.Error, DiagnosticCodes.DependencyCycle,
                        $"Dependency cycle through '{record.Id}'.", record));
                    return false;
                }

                return true;
            }

            state[record.Id] = false;

            foreach (var reference in record.GetReferences())
            {
                if (reference.Kind is DependencyKind.Production or DependencyKind.Prerequisite)
                {
                    continue;
                }

                if (records.TryGetValue(reference.Target, out var target) && !Visit(target))
                {
                    state[record.Id] = true;
                    return false;
                }
            }

            state[record.Id] = true;
            return true;
        }
    }

    private static ImportDiagnostic Diagnostic(ImportDiagnosticSeverity severity, string code, string message, SourceRecord subject)
    {
        return new ImportDiagnostic(severity, ImportStage.Resolve, code, message, subject.Id, subject.Provenance);
    }
}
