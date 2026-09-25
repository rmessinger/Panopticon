using System;
using Xunit;
using Xunit.Sdk;

namespace Panopticon.Tests;

/// <summary>
/// Runs the test only on hosts that can create a headless graphics device
/// (see GraphicsDeviceUtility.CanCreateHeadlessDevice).
/// </summary>
[XunitTestCaseDiscoverer("Panopticon.Tests.GraphicsFactDiscoverer", "Panopticon.Simulation.Tests")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class GraphicsFactAttribute : FactAttribute
{
}
