# Implementation Plan: Track P · P0-1b: ViewModel Testability (`IPreferencesStore`)

> Track: [Track P: Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0: ReplaySketch arbitrary-command harness](../deep-dives/playable-baseline-harness.md), sub-phase P0-1b: ViewModel testability
> Checklist item: [implementation-checklist.md](../implementation-checklist.md), P0 item "Extract an `IPreferencesStore` interface from `MainViewModel`"
>
> Audience: human overview of the implementation plan. Agent context sits below the divider.

## Problem

`MainViewModel.LoadMapFromFolderAsync` persists the chosen install path by calling the MAUI
static `Preferences.Default.Set("cnc_generals_path", folderPath)` directly
(`src/OpenSage.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs:273`), before `LoadMapAsync`
runs. The save is therefore unconditional : it happens even when map loading subsequently fails.
Because `Preferences.Default` is a MAUI static with no seam, that behavior cannot be unit-tested
without a MAUI runtime, so the harness's own logic has no coverage here. The same static is read at
`MainViewModel.cs:135` and in `Views/MapPreviewView.xaml.cs:69`.

Checklist item AC:
- `MainViewModel` accepts an `IPreferencesStore` via constructor injection (production uses the
  `Preferences.Default` implementation; tests supply a `MemoryPreferencesStore`).
- A unit test calls `LoadMapFromFolder` with a valid directory path where map loading will fail
  (no game files), and asserts the preference was saved regardless of map load outcome.

## Design decision

Where the seam lives and how production wires it. Options considered:

- A (chosen): Define `IPreferencesStore` in `OpenSage.Tools.ReplaySketch.Core` (so the non-MAUI
  test project can reference it), with a `MemoryPreferencesStore` implementation in Core for tests
  and a thin `MauiPreferencesStore` adapter (wrapping `Preferences.Default`) in the MAUI project.
  Inject via the `MainViewModel` constructor, defaulting to the MAUI adapter in the composition root.
- B: Keep the interface in the MAUI project. Rejected : the test project
  (`OpenSage.Tools.ReplaySketch.Tests`, `net10.0`, references Core only) cannot see MAUI types, so
  the interface must live in Core to be testable there.
- C: Wrap only the single call site with a `Func<string,string,bool>` delegate. Rejected : does not
  cover the read sites (`:135`, `MapPreviewView`) and gives no reusable seam.

Rationale: the test project references Core only; placing the interface + memory implementation in
Core is the minimal change that makes the contract testable, matching the pattern the P0 deep dive
prescribes (§3.1).

## Approach

### New seam (Core)

- `IPreferencesStore` : `string Get(string key, string defaultValue)` and
  `void Set(string key, string value)`. Keep it minimal: only the operations `MainViewModel` uses.
- `MemoryPreferencesStore : IPreferencesStore` : backed by a `Dictionary<string,string>`; for tests.

### Adapter (MAUI)

- `MauiPreferencesStore : IPreferencesStore` : delegates to `Preferences.Default.Get/Set`.

### MainViewModel changes

- Add an `IPreferencesStore` constructor parameter; store it in a field. The existing composition
  root (wherever `MainViewModel` is constructed in the MAUI app) passes `new MauiPreferencesStore()`.
- Replace the three direct `Preferences.Default` uses (`:135` read, `:273` write; and the read in
  `Views/MapPreviewView.xaml.cs:69` if that view holds a `MainViewModel`/store reference) with the
  injected store. Preserve the existing ordering in `LoadMapFromFolderAsync`: store the path first,
  then `await LoadMapAsync()` : so the unconditional-save behavior is unchanged (the test pins it).

### Tests (`OpenSage.Tools.ReplaySketch.Tests`)

- `LoadMapFromFolder_SavesPreference_EvenWhenMapLoadFails`: construct `MainViewModel` with a
  `MemoryPreferencesStore` and a folder path that has no game files; call `LoadMapFromFolderAsync`;
  assert the store contains `cnc_generals_path == folderPath` even though `MapLoadError` is set.
- `LoadMapFromFolder_PersistsExactPath`: assert the stored value equals the input path verbatim.

If `MainViewModel` cannot currently be constructed headlessly (other MAUI statics in its ctor), the
plan's first step is to confirm/჻minimize ctor dependencies; if a blocking MAUI static is found, the first step is to confirm and minimize ctor dependencies, then
escalate rather than widening scope (the card may need to extract a second seam).

---

> Agent implementation details : the sections below list exact files, methods, and
> tests for agents executing this plan. Human readers can stop here.

## Files to change

| File | Change |
|---|---|
| `src/OpenSage.Tools.ReplaySketch.Core/Services/IPreferencesStore.cs` | NEW : interface |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/MemoryPreferencesStore.cs` | NEW : in-memory impl for tests |
| `src/OpenSage.Tools.ReplaySketch.Maui/Services/MauiPreferencesStore.cs` | NEW : `Preferences.Default` adapter |
| `src/OpenSage.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs` | Inject `IPreferencesStore`; replace `Preferences.Default` uses (`:135`, `:273`) |
| `src/OpenSage.Tools.ReplaySketch.Maui/Views/MapPreviewView.xaml.cs` | Use the injected store instead of `Preferences.Default` (`:69`), if it constructs/holds the VM |
| MAUI composition root (where `MainViewModel` is `new`-ed) | Pass `new MauiPreferencesStore()` |
| `src/OpenSage.Tools.ReplaySketch.Tests/PreferencesStoreTests.cs` | NEW : the two tests above |

## Out of scope (later P0 items)

- Any change to export/order logic : this is purely the persistence seam.
- Generalizing other MAUI statics (file pickers, `MainThread`) : only `Preferences` is in scope here.
