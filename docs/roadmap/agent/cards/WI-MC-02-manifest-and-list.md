# Task Card `WI-MC-02`: Manifest model and `--list` command

| Field | Value |
|---|---|
| Card ID | `WI-MC-02` |
| Plan | [ModCollector Tool Plan](../../plans/mod-collector.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-MC-01` |
| Diagrams | N/A: standalone tool, see plan |

## Goal

Add an embedded JSON manifest of curated mod download entries and implement `--list` to
print them as a console table, filtered by optional `--game` and `--category` flags.

## Steps

1. Create `src/Panopticon.Tools.ModCollector/manifest.json` as an embedded resource.
   Schema: array of objects with fields `name`, `game`, `category`, `moddbUrl`, `downloadUrl`, `status`.
   Game values are the canonical slugs defined in the plan (e.g. `cnc-generals-zero-hour`, `bfme2`,
   `bfme2-rotwk`). See the [game slug table](../../plans/mod-collector.md#game-slug-table).
   Populate with the seed entries from the plan (Contra, Shockwave, NProject Mod, Edain Mod,
   Age of the Ring, and a handful of map packs per game). For each entry, verify that
   `downloadUrl` is a direct `.zip` download before committing. Use `curl -I <url>` to check
   `Content-Type: application/zip` (or similar). If a URL cannot be verified, leave
   `downloadUrl` as `""` and mark the entry with `"status": "unverified"`.

2. Wire the embedded resource in the `.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="manifest.json" />
</ItemGroup>
```

3. Create `src/Panopticon.Tools.ModCollector/ManifestEntry.cs`:

```csharp
namespace Panopticon.Tools.ModCollector;

sealed class ManifestEntry
{
    public string Name { get; init; } = "";
    public string Game { get; init; } = "";
    public string Category { get; init; } = "";
    public string ModdbUrl { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string Status { get; init; } = "ok"; // "ok" | "unverified"
}
```

4. Create `src/Panopticon.Tools.ModCollector/ManifestLoader.cs` that reads the embedded
   resource and deserializes it with `System.Text.Json.JsonSerializer`. Return
   `IReadOnlyList<ManifestEntry>`.

5. Create `src/Panopticon.Tools.ModCollector/ListOptions.cs` with `[Verb("list")]`:

```csharp
[Verb("list", HelpText = "List available mods in the manifest.")]
sealed class ListOptions
{
    [Option("game", Required = false, HelpText = "Filter by game slug.")]
    public string? Game { get; set; }

    [Option("category", Required = false, HelpText = "Filter by category (maps/mods/addons).")]
    public string? Category { get; set; }
}
```

6. Update `Program.cs` to use `Parser.Default.ParseArguments<ListOptions>(args)` (will be
   expanded in `WI-MC-03` and `WI-MC-04`). In the list handler, load the manifest, apply
   filters, and print via `ConsoleTable` with columns: Name, Game, Category, Status.

## Acceptance criteria

- `dotnet run --project src/Panopticon.Tools.ModCollector -- list` prints a table with at least
  one row.
- `dotnet run --project src/Panopticon.Tools.ModCollector -- list --game cnc-generals-zero-hour`
  shows only Zero Hour entries.
- Build is clean, `dotnet format src --verify-no-changes` passes.

## Validation

```sh
dotnet build src/Panopticon.Tools.ModCollector
dotnet run --project src/Panopticon.Tools.ModCollector -- list
dotnet run --project src/Panopticon.Tools.ModCollector -- list --category mods
dotnet format src --verify-no-changes
```

## Result

<!-- append after completing -->
