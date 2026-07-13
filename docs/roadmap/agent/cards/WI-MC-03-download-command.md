# Task Card `WI-MC-03`: `--download` command

| Field | Value |
|---|---|
| Card ID | `WI-MC-03` |
| Plan | [ModCollector Tool Plan](../../plans/mod-collector.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-MC-02` |
| Diagrams | N/A: standalone tool, see plan |

## Goal

Implement the `download` verb: iterate over manifest entries (optionally filtered), HTTP GET
each `downloadUrl`, save the result as `<output>/<game>/<category>/<name>.zip`, with a
retry-once strategy on failure.

## Steps

1. Create `src/OpenSage.Tools.ModCollector/DownloadOptions.cs` with `[Verb("download")]`:

```csharp
[Verb("download", HelpText = "Download mods from the manifest.")]
sealed class DownloadOptions
{
    [Option("game", Required = false, HelpText = "Filter by game slug.")]
    public string? Game { get; set; }

    [Option("category", Required = false, HelpText = "Filter by category (maps/mods/addons).")]
    public string? Category { get; set; }

    [Option('o', "output", Required = true, HelpText = "Root output directory.")]
    public string Output { get; set; } = "";
}
```

2. Create `src/OpenSage.Tools.ModCollector/Downloader.cs`. Key contract:

```csharp
// Returns true if all entries succeeded; false if any were skipped.
Task<bool> DownloadAllAsync(IReadOnlyList<ManifestEntry> entries, string outputRoot);
```

Implementation rules:
- Skip entries where `DownloadUrl` is `""` or `Status == "unverified"` (log to stderr).
- For each eligible entry:
  - Compute dest path: `Path.Combine(outputRoot, entry.Game, entry.Category, entry.Name + ".zip")`.
  - `Directory.CreateDirectory` the parent.
  - If the file already exists, skip with a console message ("already downloaded").
  - HTTP GET with a `HttpClient` singleton (one per process run, passed in or static).
  - On HTTP or IO failure: wait 2 seconds, retry once.
  - On second failure: write to `Console.Error`, continue.
  - On success: write the response stream directly to the file; log "downloaded: <name>".
- Use `HttpClient` with a reasonable timeout (30 seconds) and a descriptive `User-Agent` header
  (`OpenSAGE ModCollector/1.0`).

3. Wire `DownloadOptions` into `Program.cs` alongside `ListOptions`.

## Acceptance criteria

- Downloading an entry that already exists prints "already downloaded: <name>" and does not
  re-fetch.
- A URL that fails twice logs to stderr and the process continues to the next entry.
- Exit code is non-zero only when every entry failed.
- Downloaded files appear at the expected path.

## Validation

```sh
dotnet build src/OpenSage.Tools.ModCollector
dotnet run --project src/OpenSage.Tools.ModCollector -- download --output /tmp/mc-test --category mods
ls /tmp/mc-test
dotnet format src --verify-no-changes
```

## Result

<!-- append after completing -->
