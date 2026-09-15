using System;
using Xunit;
using Xunit.Sdk;

namespace OpenSage.Tests;

/// <summary>
/// Runs the test only on hosts that can create a headless graphics device
/// (see GraphicsDeviceUtility.CanCreateHeadlessDevice).
/// </summary>
[XunitTestCaseDiscoverer("OpenSage.Tests.GraphicsFactDiscoverer", "OpenSage.Game.Tests")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class GraphicsFactAttribute : FactAttribute
{
}
