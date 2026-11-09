#nullable enable

using System.Collections.Generic;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Describes an H4 unit package import in a ScenarioCapture profile with explicit path and adapter selection.
/// </summary>
public sealed record HarnessPackageImportDescriptor(
    string PackagePath,
    string Adapter,
    IReadOnlyList<string>? Roots = null,
    IReadOnlyList<string>? Overrides = null);
