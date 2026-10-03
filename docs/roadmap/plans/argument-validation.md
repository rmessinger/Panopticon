# Plan: Single-line argument validation

> Solution-wide refactor that replaces multi-line `if (...) { throw new Argument*Exception(...); }`
> guards with single-line BCL throw helpers or a shared `Ensure` helper class. Plan and `WI-AV-`
> card index.
>
> Audience: human + agent (implementation reference).

---

## Purpose

A braced guard costs at least four lines per argument. Constructors in the import interchange
types (`src/Panopticon.Simulation/Content/Importing/`) spend most of their body on validation.
Single-line guards keep the same checks readable at a glance.

## Current state

Survey of `src/` and `test/` on 2026-10-03:

| Category | Sites | Target form |
|---|---|---|
| Null check, bare `nameof` | 59 | `ArgumentNullException.ThrowIfNull` |
| Null element in a loop, custom message | 6 | `Ensure.NoNullElements` |
| String null-or-empty / null-or-whitespace, custom message | 23 | `Ensure.NotNullOrEmpty` / `Ensure.NotNullOrWhiteSpace` |
| Simple range (`< 0`, `<= 0`, `< TimeSpan.Zero`) | 22 | `Ensure.AtLeast` / `Ensure.GreaterThan` |
| Finite float | 3 | `Ensure.FinitePositive` and siblings |
| Compound and unique conditions | 45 | `Ensure.*` when one fits, else brace-less `if` |

- 159 sites across 77 files; 117 in `Panopticon.Simulation`, 21 in `Panopticon.Tools.ReplaySketch.Core`.
- `ArgumentNullException.ThrowIfNull` already appears at 26 sites.
- Tests: 45 `Assert.Throws<ArgumentException>`, 8 `Assert.Throws<ArgumentNullException>`, 5
  `Assert.Throws<ArgumentOutOfRangeException>`. xUnit `Throws<T>` matches the exact type.
- `Panopticon.Simulation.CodeGen` targets `netstandard2.0` and lacks the BCL helpers.

## Decisions

| Decision | Choice |
|---|---|
| Exception type for range and finite checks | `ArgumentOutOfRangeException`. Tests asserting `ArgumentException` for those sites are updated to the new type. |
| Null string arguments | `ArgumentNullException` for null, `ArgumentException` for empty or whitespace, matching the BCL helpers. |
| Messages | Existing domain wording is kept verbatim. BCL helpers apply only where the guard has no custom message, because `ThrowIfNullOrEmpty` and the `ArgumentOutOfRangeException.ThrowIf*` family accept no message. |
| Helper name and home | `Panopticon.Ensure` in `src/Panopticon.Core/Ensure.cs`. `Guard` collides with the AI guard-state vocabulary (`GuardInTunnelNetworkState`). Core is referenced by Simulation and ReplaySketch.Core. |
| Projects outside Core's reference graph | Mathematics, FileFormats, IO, Big, RefPack, Veldrid.ImageSharp use BCL helpers or brace-less `if` only. No new project references. |
| Out of scope | `Panopticon.Simulation.CodeGen` (`netstandard2.0`); non-`Argument*` throws (`InvalidOperationException`, `ObjectDisposedException`). |

## `Ensure` API

Every method takes an optional message where noted, then
`[CallerArgumentExpression] string? paramName = null`.

| Method | Throws | Notes |
|---|---|---|
| `NotNullOrEmpty(string? value, string? message)` | `ArgumentNullException` / `ArgumentException` | |
| `NotNullOrWhiteSpace(string? value, string? message)` | `ArgumentNullException` / `ArgumentException` | |
| `NotEmpty<T>(IReadOnlyCollection<T>? items, string? message)` | `ArgumentNullException` / `ArgumentException` | |
| `NoNullElements<T>(IReadOnlyList<T?>? items, string elementName)` | `ArgumentNullException` / `ArgumentException` | Message: `$"{elementName} at index {i} must not be null."`, matching existing wording. |
| `AtLeast<T>(T value, T min, string? message)` | `ArgumentOutOfRangeException` | `T : IComparable<T>`; covers `int`, `long`, `float`, `TimeSpan`. |
| `GreaterThan<T>(T value, T bound, string? message)` | `ArgumentOutOfRangeException` | |
| `InRange<T>(T value, T min, T max, string? message)` | `ArgumentOutOfRangeException` | Inclusive bounds. |
| `Finite(float value, string? message)` | `ArgumentOutOfRangeException` | Overloads for `Vector3` and `Matrix4x4` throw `ArgumentException`. |
| `FiniteNonNegative(float value, string? message)` | `ArgumentOutOfRangeException` | |
| `FinitePositive(float value, string? message)` | `ArgumentOutOfRangeException` | |

Selection rule: no custom message uses the BCL helper; a custom message uses the `Ensure` overload
carrying it; a condition no helper expresses uses a brace-less `if` (`docs/coding-style.md` rule 1).

## Work items

| Card | Title | Effort | Prerequisites |
|---|---|---|---|
| [`WI-AV-01`](../agent/cards/WI-AV-01-add-ensure-helper.md) | Add the `Ensure` helper and tests | S | none |
| [`WI-AV-02`](../agent/cards/WI-AV-02-convert-null-checks.md) | Convert bare null checks to `ThrowIfNull` | S | none |
| [`WI-AV-03`](../agent/cards/WI-AV-03-convert-string-and-element-checks.md) | Convert string and null-element checks | S | `WI-AV-01` |
| [`WI-AV-04`](../agent/cards/WI-AV-04-convert-range-and-finite-checks.md) | Convert range and finite checks, update tests | S | `WI-AV-01` |
| [`WI-AV-05`](../agent/cards/WI-AV-05-collapse-compound-checks.md) | Collapse compound and unique checks | S | `WI-AV-01` |
| [`WI-AV-06`](../agent/cards/WI-AV-06-enforce-and-document.md) | Analyzer enforcement and style rule | S | `WI-AV-02`..`05` |

## Sequencing

`WI-AV-01` and `WI-AV-02` are independent. `WI-AV-03`, `04`, `05` follow `WI-AV-01` and can run in
parallel. `WI-AV-06` runs last so the analyzers report zero remaining sites. One stacked PR per
card. Determinism suites are unaffected: guards throw only on invalid input.

## Finding sites

```sh
grep -rn --include='*.cs' -B2 'throw new Argument[A-Za-z]*Exception' src test
```
