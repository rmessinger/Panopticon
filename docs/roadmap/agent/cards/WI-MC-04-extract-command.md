# Task Card: `WI-MC-04` — `--extract` command

| Field | Value |
|---|---|
| Card ID | `WI-MC-04` |
| Plan | [ModCollector Tool Plan](../../plans/mod-collector.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-MC-03` |
| Diagrams | N/A — standalone tool, see plan |

## Goal

Implement the `extract` verb: walk the output tree, find all `.zip` files, extract any `.big`
files they contain into the same directory, then delete the `.zip`. After extraction, the
directory tree is engine-ready — a user can point `CNC_GENERALS_ZH_PATH` (etc.) at a
`<game>/<category>` directory and OpenSAGE picks up the `.big` files via `SkudefReader`.

## Context

Key references:
- `src/OpenSage.Tools.BigTool/Program.cs:201-221` — `ExtractMode` opens a `BigArchive` and
  streams entries to disk. The extraction here is different (we're unpacking a `.zip` to find
  `.big` files, not extracting a `.big` archive), but the pattern of streaming to file is the
  same.
- `System.IO.Compression.ZipFile` / `ZipArchive` — built into .NET, no extra packages needed.

## Steps

1. Create `src/OpenSage.Tools.ModCollector/ExtractOptions.cs` with `[Verb("extract")]`:

```csharp
[Verb("extract", HelpText = "Extract .big files from downloaded .zip archives in the output tree.")]
sealed class ExtractOptions
{
    [Option('o', "output", Required = true, HelpText = "Root output directory (same as used for download).")]
    public string Output { get; set; } = "";
}
```

2. Create `src/OpenSage.Tools.ModCollector/Extractor.cs`. Algorithm:

```
For each .zip file found recursively under outputRoot:
    Open as ZipArchive (read mode).
    For each entry whose Name ends with ".big" (case-insensitive):
        Destination = Path.Combine(directory of the .zip, entry.Name)
        If destination already exists, skip (log "already extracted: <name>").
        Otherwise stream entry to destination (log "extracted: <name>").
    Close archive.
    Delete the .zip (log "deleted archive: <filename>").
```

Use `ZipFile.OpenRead(zipPath)` from `System.IO.Compression`.

3. Wire `ExtractOptions` into `Program.cs` alongside the other verbs.

4. Update the `download` command's help text (in `DownloadOptions`) to mention that
   `extract` must be run separately after download.

## Acceptance criteria

- After `extract`, all `.zip` files in the output tree are deleted.
- For each `.zip` that contained `.big` files, those files appear beside where the `.zip` was.
- `.zip` files with no `.big` entries are still deleted (log a warning: "no .big files found in <name>.zip").
- Re-running `extract` on an already-extracted tree is a no-op (no `.zip` files remain).
- Build is clean, `dotnet format src --verify-no-changes` passes.

## Validation

```sh
dotnet build src/OpenSage.Tools.ModCollector
# Assuming WI-MC-03 has been run and /tmp/mc-test has .zip files:
dotnet run --project src/OpenSage.Tools.ModCollector -- extract --output /tmp/mc-test
ls /tmp/mc-test/**/*.big
dotnet format src --verify-no-changes
```

## Result

<!-- append after completing -->
