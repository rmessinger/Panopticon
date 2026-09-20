#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Production process adapter over <see cref="Process"/>: direct execution without a shell,
/// redirected and bounded standard error, awaited exit, and tree-scoped kill of exactly the
/// started instance.
/// </summary>
internal sealed class SystemExternalModelProcess : IExternalModelProcess
{
    private const int MaxStdErrChars = 64 * 1024;

    private readonly Process _process;

    private SystemExternalModelProcess(Process process)
    {
        _process = process;
    }

    public static IExternalModelProcess Start(string executablePath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            CreateNoWindow = true,
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");

        return new SystemExternalModelProcess(process);
    }

    public async Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];

        while (builder.Length < MaxStdErrChars)
        {
            var read = await _process.StandardError.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, Math.Min(read, MaxStdErrChars - builder.Length));
        }

        return builder.ToString();
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public void KillTree()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Already exiting on some platforms.
        }
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}

internal sealed class SystemExternalModelProcessFactory : IExternalModelProcessFactory
{
    public IExternalModelProcess Start(string executablePath, string arguments)
    {
        return SystemExternalModelProcess.Start(executablePath, arguments);
    }
}
