#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units.Materialization;

public sealed record PublicationResult(PublishedUnitContent? Published, IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool Succeeded => Published is not null && !Diagnostics.Any(d => d.IsError);
}
