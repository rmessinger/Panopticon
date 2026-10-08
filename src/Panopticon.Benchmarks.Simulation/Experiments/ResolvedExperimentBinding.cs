using System;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// One resolved cell binding: the registered descriptor and a fresh settings object from its
/// factory. Settings are created per cell so runs never share mutable state.
/// </summary>
public sealed record ResolvedExperimentBinding(ImplementationDescriptor Descriptor, object Settings);
