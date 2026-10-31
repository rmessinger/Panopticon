using System;
using System.Collections.Generic;

namespace Panopticon.Geospatial;

public sealed class GeoDiagnosticBag
{
    private readonly List<GeoDiagnostic> _diagnostics = [];
    private readonly IReadOnlyList<GeoDiagnostic> _items;

    public GeoDiagnosticBag()
    {
        _items = _diagnostics.AsReadOnly();
    }

    public bool HasErrors { get; private set; }

    public IReadOnlyList<GeoDiagnostic> Items => _items;

    public void Add(GeoDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        _diagnostics.Add(diagnostic);

        if (diagnostic.Severity == GeoSeverity.Error)
        {
            HasErrors = true;
        }
    }
}
