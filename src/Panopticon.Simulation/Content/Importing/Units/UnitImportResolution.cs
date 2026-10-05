#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>Validated closure ready for materialization, with every diagnostic from read through validate.</summary>
public sealed record UnitImportResolution(
    ContentSource Source,
    IReadOnlyList<ContentId> Roots,
    IReadOnlyList<SourceRecord> Closure,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool Succeeded => !Diagnostics.Any(diagnostic => diagnostic.IsError);

    public T Get<T>(ContentId id)
        where T : SourceRecord
    {
        return (T)Closure.First(record => record.Id.Equals(id));
    }

    /// <summary>Runs inheritance, dependency resolution, and validation over one adapter's output.</summary>
    public static UnitImportResolution Resolve(UnitContentSet set, UnitCapabilityRegistry registry, IPublishedContentLookup? published = null)
    {
        var inheritance = new UnitInheritanceResolver().Resolve(set);
        var dependencies = new UnitDependencyResolver().Resolve(inheritance, set.Roots, published);
        var validation = new UnitImportValidator(registry).Validate(dependencies.Closure);

        var diagnostics = set.Diagnostics
            .Concat(inheritance.Diagnostics)
            .Concat(dependencies.Diagnostics)
            .Concat(validation)
            .ToArray();

        return new UnitImportResolution(set.Source, set.Roots, dependencies.Closure, diagnostics);
    }
}
