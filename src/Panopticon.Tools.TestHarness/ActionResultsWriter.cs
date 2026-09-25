using System;
using System.IO;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Buffered writer for a TestHarness run's engine-action results file.
///
/// This is a pure I/O layer: it knows nothing about game state, only how to write and
/// periodically flush lines to disk. <see cref="EngineActionRecorder"/> decides what to log.
///
/// Every line is also routed through NLog automatically, so it reaches stdout and
/// <c>panopticon.log</c> (per <c>Panopticon.Core/NLog.config</c>'s catch-all rule) in real time,
/// in addition to the dedicated results file. Callers never need to log a result twice.
///
/// The file body is deterministic (logic-frame-based, not wall-clock) so two runs of the same
/// scenario/config can be diffed directly. A leading block of <c>#</c>-prefixed lines carries
/// non-comparable run metadata (scenario, game, host, wall-clock start time) — parity-diff
/// tooling should skip those lines.
/// </summary>
public sealed class ActionResultsWriter : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private const int FlushEveryLines = 20;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly StreamWriter _writer;
    private int _linesSinceFlush;
    private DateTime _lastFlush;

    public string FilePath { get; }

    public ActionResultsWriter(string filePath, string scenarioName, string scenarioPath, string game)
    {
        FilePath = filePath;
        _writer = new StreamWriter(filePath, append: false) { AutoFlush = false };
        _lastFlush = DateTime.UtcNow;

        WriteHeader(scenarioName, scenarioPath, game);
    }

    private void WriteHeader(string scenarioName, string scenarioPath, string game)
    {
        _writer.WriteLine($"# TestHarness action results");
        _writer.WriteLine($"# scenario: {scenarioName}");
        _writer.WriteLine($"# scenarioPath: {scenarioPath}");
        _writer.WriteLine($"# game: {game}");
        _writer.WriteLine($"# started: {DateTime.UtcNow:O}");
        _writer.WriteLine($"# columns: logicFrame\tstate\torderType\tplayer\tdetails");
        _writer.Flush();
    }

    /// <summary>
    /// Writes one tab-separated result line: <c>logicFrame\tstate\torderType\tplayer\tdetails</c>.
    /// Also logs the same result through NLog (stdout + panopticon.log) automatically.
    /// </summary>
    public void WriteLine(uint logicFrame, string state, string orderType, string player, string details)
    {
        _writer.WriteLine($"{logicFrame}\t{state}\t{orderType}\t{player}\t{details}");

        _linesSinceFlush++;
        MaybeFlush();

        Logger.Info($"[{state}] {orderType} frame={logicFrame} player={player} {details}");
    }

    /// <summary>
    /// Flushes if either the line-count or time-elapsed threshold since the last flush has been
    /// reached. Call this once per main-loop iteration in addition to after every write, so idle
    /// periods with no new actions still get flushed periodically.
    /// </summary>
    public void MaybeFlush()
    {
        if (_linesSinceFlush >= FlushEveryLines || DateTime.UtcNow - _lastFlush >= FlushInterval)
        {
            _writer.Flush();
            _linesSinceFlush = 0;
            _lastFlush = DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }
}
