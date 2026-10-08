using System;
using System.Collections.Generic;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// One swept seam of an experiment: the seam key from the seam registry and the registered
/// implementation ids to compare.
/// </summary>
public sealed record ExperimentAxis(string Seam, IReadOnlyList<string> Implementations);
