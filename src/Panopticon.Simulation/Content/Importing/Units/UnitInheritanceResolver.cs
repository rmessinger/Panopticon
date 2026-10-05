#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Flattens parent chains within one content set. Source records are never mutated; each resolved
/// record is a new value with <see cref="SourceRecord.Parent"/> cleared and field origins stamped.
/// Records involved in an error are left out of the result.
/// </summary>
public sealed class UnitInheritanceResolver
{
    public const int MaxDepth = 16;

    public InheritanceResult Resolve(UnitContentSet set)
    {
        var diagnostics = new List<ImportDiagnostic>();
        var byId = new Dictionary<ContentId, SourceRecord>();

        foreach (var record in set.Records)
        {
            if (!byId.TryAdd(record.Id, record))
            {
                diagnostics.Add(Error(DiagnosticCodes.DuplicateRecord, $"Record '{record.Id}' is defined more than once.", record));
            }
        }

        var duplicates = diagnostics.Select(d => d.Subject!.Value).ToHashSet();
        var resolved = new Dictionary<ContentId, SourceRecord>();
        var depths = new Dictionary<ContentId, int>();
        var failed = new HashSet<ContentId>(duplicates);

        foreach (var record in byId.Values)
        {
            ResolveRecord(record, byId, resolved, depths, failed, diagnostics);
        }

        return new InheritanceResult(resolved, diagnostics);
    }

    private static SourceRecord? ResolveRecord(
        SourceRecord record,
        Dictionary<ContentId, SourceRecord> byId,
        Dictionary<ContentId, SourceRecord> resolved,
        Dictionary<ContentId, int> depths,
        HashSet<ContentId> failed,
        List<ImportDiagnostic> diagnostics)
    {
        if (resolved.TryGetValue(record.Id, out var done))
        {
            return done;
        }

        if (failed.Contains(record.Id))
        {
            return null;
        }

        var chain = new List<SourceRecord> { record };
        var current = record;

        while (current.Parent is { } parentId)
        {
            if (resolved.TryGetValue(parentId, out _))
            {
                break;
            }

            if (failed.Contains(parentId))
            {
                return Fail(chain, failed, diagnostics, DiagnosticCodes.MissingParent, $"Parent '{parentId}' of '{current.Id}' failed to resolve.", current);
            }

            if (!byId.TryGetValue(parentId, out var parent))
            {
                return Fail(chain, failed, diagnostics, DiagnosticCodes.MissingParent, $"Parent '{parentId}' of '{current.Id}' was not found.", current);
            }

            if (parent.GetType() != current.GetType())
            {
                return Fail(chain, failed, diagnostics, DiagnosticCodes.ParentKindMismatch, $"Parent '{parentId}' of '{current.Id}' is a different record kind.", current);
            }

            if (chain.Contains(parent))
            {
                return Fail(chain, failed, diagnostics, DiagnosticCodes.InheritanceCycle, $"Inheritance cycle through '{parentId}'.", current);
            }

            if (chain.Count > MaxDepth)
            {
                return Fail(chain, failed, diagnostics, DiagnosticCodes.InheritanceTooDeep, $"Inheritance of '{record.Id}' exceeds {MaxDepth} levels.", record);
            }

            chain.Add(parent);
            current = parent;
        }

        var baseDepth = current.Parent is { } resolvedTop ? depths[resolvedTop] + 1 : 0;
        if (baseDepth + chain.Count - 1 > MaxDepth)
        {
            return Fail(chain, failed, diagnostics, DiagnosticCodes.InheritanceTooDeep, $"Inheritance of '{record.Id}' exceeds {MaxDepth} levels.", record);
        }

        SourceRecord? resolvedParent = current.Parent is { } top ? resolved[top] : null;

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            resolvedParent = chain[i].InheritFrom(resolvedParent);
            resolved[chain[i].Id] = resolvedParent;
            depths[chain[i].Id] = baseDepth + chain.Count - 1 - i;
        }

        return resolvedParent;
    }

    private static SourceRecord? Fail(
        List<SourceRecord> chain,
        HashSet<ContentId> failed,
        List<ImportDiagnostic> diagnostics,
        string code,
        string message,
        SourceRecord subject)
    {
        diagnostics.Add(Error(code, message, subject));
        foreach (var record in chain)
        {
            failed.Add(record.Id);
        }

        return null;
    }

    private static ImportDiagnostic Error(string code, string message, SourceRecord subject)
    {
        return new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportStage.Resolve, code, message, subject.Id, subject.Provenance);
    }
}
