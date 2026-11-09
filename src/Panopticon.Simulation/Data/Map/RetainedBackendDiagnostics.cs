#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Panopticon.Data.Map;

/// <summary>
/// Diagnostic tracker for unclassified fields in retained map backend sections.
/// Unclassified fields with no established physical dimension are retained without conversion
/// and reported here.
/// </summary>
public static class RetainedBackendDiagnostics
{
    private static readonly ConcurrentBag<string> Diagnostics = new();

    public static IReadOnlyCollection<string> Emitted => Diagnostics.ToArray();

    public static void ReportUnclassified(string fieldName)
    {
        Diagnostics.Add(fieldName);
    }

    public static void Clear()
    {
        Diagnostics.Clear();
    }
}
