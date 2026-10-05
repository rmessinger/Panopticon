using System.Runtime.InteropServices;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// B1-3 environment capture: the record carries OS, runtime, architecture, processor count,
/// and memory fields on every platform the tests run on.
/// </summary>
public class BenchmarkMetricEnvironmentTests
{
    [Fact]
    public void Capture_PopulatesCoreFields()
    {
        var environment = EnvironmentRecord.Capture();

        Assert.Contains(RuntimeInformation.OSDescription, environment.Os);
        Assert.False(string.IsNullOrEmpty(environment.Runtime));
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString(), environment.Arch);
        Assert.True(environment.LogicalProcessors > 0);
        Assert.True(environment.TotalPhysicalBytes > 0);
        Assert.True(environment.PeakWorkingSetBytes > 0);
        Assert.Matches("^(Server|Workstation)/", environment.GcMode);
    }

    [Fact]
    public void Capture_LeavesGraphicsFieldsNullHeadless()
    {
        var environment = EnvironmentRecord.Capture();

        Assert.Null(environment.Gpu);
        Assert.Null(environment.VramBytes);
        Assert.Null(environment.InstallId);
    }
}
