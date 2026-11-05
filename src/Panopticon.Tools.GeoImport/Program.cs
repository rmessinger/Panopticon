using System;
using System.Threading;

namespace Panopticon.Tools.GeoImport;

/// <summary>
/// Console host of the GeoImport CLI: subscribes Ctrl+C to one cancellation token,
/// runs the in-process entry against the real console streams, and always unsubscribes.
/// No other process-global state.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var source = new CancellationTokenSource();
        void Handler(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            // Soft-cancel: the run reports the outcome itself instead of the process dying.
            eventArgs.Cancel = true;
            source.Cancel();
        }

        Console.CancelKeyPress += Handler;

        try
        {
            return GeoImportCli.Run(args, Console.Out, Console.Error, source.Token);
        }
        finally
        {
            Console.CancelKeyPress -= Handler;
        }
    }
}
