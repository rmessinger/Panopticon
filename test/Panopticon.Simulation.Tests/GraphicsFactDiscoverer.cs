using System.Collections.Generic;
using Panopticon.Graphics;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Panopticon.Tests;

public sealed class GraphicsFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diagnosticMessageSink;

    private static readonly bool HeadlessDeviceSupported;

    public GraphicsFactDiscoverer(IMessageSink diagnosticMessageSink)
    {
        _diagnosticMessageSink = diagnosticMessageSink;
    }

    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo factAttribute)
    {
        if (!HeadlessDeviceSupported)
        {
            _diagnosticMessageSink.OnMessage(
                new DiagnosticMessage(
                    $"Skipped test {testMethod.TestClass.Class.Name}.{testMethod.Method.Name}, " +
                    "because no headless-capable graphics backend is available."));

            yield break;
        }

        yield return new XunitTestCase(
            _diagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            TestMethodDisplayOptions.All,
            testMethod);
    }

    static GraphicsFactDiscoverer()
    {
        HeadlessDeviceSupported = GraphicsDeviceUtility.CanCreateHeadlessDevice();
    }
}
