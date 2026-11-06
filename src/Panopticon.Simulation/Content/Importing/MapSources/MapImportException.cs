#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources;

/// <summary>Import failure carrying the typed diagnostics that blocked publication. No model content is published with the exception.</summary>
public sealed class MapImportException : Exception
{
    private const string DefaultMessage = "Map import failed; no model content was published.";

    public MapImportException(IReadOnlyList<MapInterchangeDiagnostic> diagnostics)
        : this(diagnostics, DefaultMessage)
    {
    }

    public MapImportException(IReadOnlyList<MapInterchangeDiagnostic> diagnostics, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<MapInterchangeDiagnostic> Diagnostics { get; }

    public override string Message => Diagnostics.Count == 0
        ? base.Message
        : $"{base.Message} Diagnostics: {string.Join("; ", Diagnostics.Select(d => d.ToString()))}";
}
