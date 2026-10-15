using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Panopticon.Tools.ReplaySketch.Services;

internal interface IHarnessProcess : IDisposable
{
    Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken);
    Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken);
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
    void KillTree();
}

internal interface IHarnessProcessFactory { IHarnessProcess Start(string executablePath, string arguments); }

internal sealed class SystemHarnessProcessFactory : IHarnessProcessFactory
{
    public IHarnessProcess Start(string executablePath, string arguments) => new SystemHarnessProcess(Process.Start(new ProcessStartInfo
    {
        FileName = executablePath, Arguments = arguments, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
    }) ?? throw new InvalidOperationException($"Failed to start '{executablePath}'."));
}

internal sealed class SystemHarnessProcess(Process process) : IHarnessProcess
{
    public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken) => process.StandardOutput.ReadToEndAsync(cancellationToken);
    public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken) => process.StandardError.ReadToEndAsync(cancellationToken);
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken) { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); return process.ExitCode; }
    public void KillTree() { try { process.Kill(true); } catch (InvalidOperationException) { } }
    public void Dispose() => process.Dispose();
}
