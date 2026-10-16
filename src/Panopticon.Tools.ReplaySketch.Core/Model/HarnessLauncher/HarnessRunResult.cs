namespace Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

public sealed record HarnessRunResult(int ExitCode, string Meaning, bool Cancelled, string? ConfigPath);
