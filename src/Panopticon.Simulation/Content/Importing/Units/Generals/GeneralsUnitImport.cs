#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units.Materialization;

namespace Panopticon.Content.Importing.Units.Generals;

/// <summary>
/// Runs staged Generals unit content through the shared pipeline: adapter, inheritance, dependency
/// resolution, capability validation, materialization (reusing native templates), and publication
/// by the publisher. Called by <see cref="ContentManager"/> once INI subsystems are loaded.
/// </summary>
public static class GeneralsUnitImport
{
    public static GeneralsUnitImportReport Run(AssetStore store, SageGame game, UnitContentStaging staging)
    {
        var adapter = new GeneralsUnitAdapter(staging, game);
        var set = adapter.Read([]);
        var publisher = new UnitContentPublisher(store);
        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());

        if (!resolution.Succeeded)
        {
            return new GeneralsUnitImportReport(set, resolution.Diagnostics, null);
        }

        var context = new UnitMaterializationContext(new UnitConversionPolicy(game), publisher.Lookup, adapter.Natives, store);
        var content = UnitRuntimeMaterializer.Materialize(resolution, context);
        if (!content.Succeeded)
        {
            return new GeneralsUnitImportReport(set, resolution.Diagnostics.Concat(content.Diagnostics).ToArray(), null);
        }

        var publication = publisher.Publish(content);
        return new GeneralsUnitImportReport(set, resolution.Diagnostics.Concat(content.Diagnostics).Concat(publication.Diagnostics).ToArray(), publication.Published);
    }
}

public sealed record GeneralsUnitImportReport(UnitContentSet Set, IReadOnlyList<ImportDiagnostic> Diagnostics, PublishedUnitContent? Published)
{
    public bool Succeeded => Published is not null && !Diagnostics.Any(d => d.IsError);

    public IEnumerable<ImportDiagnostic> Errors => Diagnostics.Where(d => d.IsError);

    public void ThrowIfFailed()
    {
        if (!Succeeded)
        {
            throw new InvalidOperationException(
                $"Generals unit import failed with {Errors.Count()} errors:{Environment.NewLine}{string.Join(Environment.NewLine, Errors.Take(20))}");
        }
    }
}
