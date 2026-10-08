using System;
using System.Collections.Generic;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// One expanded combination of an experiment definition: the binding map (seam to
/// implementation id) and the stable key rendered from it in axis order.
/// </summary>
public sealed record ExperimentCell(string Key, IReadOnlyDictionary<string, string> Bindings);
