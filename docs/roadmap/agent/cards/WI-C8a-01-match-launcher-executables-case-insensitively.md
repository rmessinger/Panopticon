# Task Card `WI-C8a-01`: Match launcher executables case-insensitively

| Field | Value |
|---|---|
| Card ID | `WI-C8a-01` |
| Roadmap phase | [Track C · C8a](../../track-c-external-assets.md): SAGE-family W3D corpus |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Make every game-definition `Probe` find its launcher executable regardless of file-name case, so the Launcher's directory probe accepts the shipped `Generals.exe` on Linux.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` section 2.
- `src/Panopticon.Simulation/IGameDefinition.cs:42`: default `Probe`.
- `src/Panopticon.Mods.Generals/GeneralsDefinition.cs:51` and `GeneralsZeroHourDefinition.cs:55`.
- `src/Panopticon.Launcher/Program.cs` around line 161: the caller.

## Steps

1. Add an internal helper (for example `InstallationProbe.FileExists(directory, fileName)`) in `Panopticon.Simulation` that returns true when the directory holds a file whose name equals `fileName` under `StringComparison.OrdinalIgnoreCase`. Use an exact `File.Exists` first, then one directory enumeration.
2. Use it in the default `IGameDefinition.Probe` and in both Generals probes, for the executable and for `INI.big` / `INIZH.big`.
3. Leave `LauncherExecutable` values unchanged.
4. Add unit tests over temp directories: `Generals.exe` + `INIZH.big` probes true for Zero Hour; `generals.exe` + `INI.big` probes true for Generals; a missing executable probes false; `lotrbfme2.exe` in upper case probes true for BFME2.

## Files likely to change

- `src/Panopticon.Simulation/IGameDefinition.cs`, `src/Panopticon.Simulation/Data/InstallationProbe.cs` (new)
- `src/Panopticon.Mods.Generals/GeneralsDefinition.cs`, `src/Panopticon.Mods.Generals/GeneralsZeroHourDefinition.cs`
- `test/Panopticon.Simulation.Tests/Data/InstallationProbeTests.cs` (new)

## Acceptance criteria

- [ ] All four probe tests pass on Linux.
- [ ] No other locator behavior changes.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~InstallationProbe"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not change `EnvironmentInstallationLocator`, registry, or Steam locators.
- No BFME boot work.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to `.exchange`.
