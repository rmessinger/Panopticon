using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Host context for one benchmark run: OS, runtime, GC flavor, architecture, CPU, memory,
/// install id, and the environment variables that affect install and rendering.
/// </summary>
public sealed class EnvironmentRecord
{
    public string Os { get; set; } = string.Empty;

    public string Runtime { get; set; } = string.Empty;

    public string GcMode { get; set; } = string.Empty;

    public string Arch { get; set; } = string.Empty;

    public string Cpu { get; set; } = string.Empty;

    public int LogicalProcessors { get; set; }

    public long TotalPhysicalBytes { get; set; }

    public long PeakWorkingSetBytes { get; set; }

    public string? Gpu { get; set; }

    public long? VramBytes { get; set; }

    public string? InstallId { get; set; }

    public Dictionary<string, string> Env { get; set; } = new();

    /// <summary>
    /// Captures the current process environment. Headless runs have no graphics device, so
    /// GPU fields stay null; headless CPU model reading is best-effort per platform.
    /// </summary>
    public static EnvironmentRecord Capture()
    {
        using var process = Process.GetCurrentProcess();

        return new EnvironmentRecord
        {
            Os = RuntimeInformation.OSDescription,
            Runtime = Environment.Version.ToString(),
            GcMode = $"{(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}/{System.Runtime.GCSettings.LatencyMode}",
            Arch = RuntimeInformation.ProcessArchitecture.ToString(),
            Cpu = ReadCpuModel(),
            LogicalProcessors = Environment.ProcessorCount,
            TotalPhysicalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            PeakWorkingSetBytes = Math.Max(process.PeakWorkingSet64, process.WorkingSet64),
            Env = new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    private static string ReadCpuModel()
    {
        try
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (line.StartsWith("model name", StringComparison.Ordinal) && line.Contains(':'))
                    {
                        return line.Split(':', 2)[1].Trim();
                    }
                }
            }
        }
        catch (IOException)
        {
            // Best-effort only; other platforms report the empty string.
        }

        return string.Empty;
    }
}
