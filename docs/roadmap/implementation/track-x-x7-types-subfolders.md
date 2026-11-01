# X7: Types subfolder organization

> Audience: human + agent.
> Phase and dependencies: [Track X](../track-x-fixes-tweaks.md). No dependencies.

| Card | Status | Scope | Prerequisites |
|---|---|---|---|
| [WI-X7-01](../agent/cards/WI-X7-01-types-subfolders.md) | Ready | Move non-logic types into `Types/` subfolders in their domains | none |

## Starting point

- `AGENTS.md` already requires one type per file and names `Types/` as the home for groups of small supporting types with no obvious owner.
- Three `Types/` folders exist: `Simulation/Types`, `Content/Importing/Units/Types`, `Content/Importing/Units/Materialization/Types`. They keep the parent namespace.
- Most domain folders (`Logic`, `Content`, `Data`, `Graphics`, `Gui`) still mix enums, records, and data classes with logic classes.

## Design

- Non-logic type: enum, constants-only class, data-only record, struct, or class with no methods beyond construction and equality, and interfaces with no default behavior.
- Selection rule: a domain gets a `Types/` folder when at least 3 qualifying files exist. Fewer stay beside their owner.
- Placement: `Types/` sits directly inside the domain folder that owns the types, never nested above or below another `Types/`.
- Namespaces stay unchanged, so no call site or `using` edits are needed.
- Exclusions: types tightly paired with one consumer, persisted-format definitions, and generated files.
- Moves use `git mv`, one domain per commit, so review shows pure renames.

## Risks

- Rebase conflicts with in-flight branches that touch moved files. Mitigation: schedule between stacks and move domain by domain.
- Path-dependent tooling (`.csproj` globs, embedded resources, scripts, docs). Mitigation: step 6 of the card greps for old paths.
- Classification disputes (what counts as non-logic). Mitigation: the plan inventory records every skipped candidate with a reason.

## WI-X7-01 results

Pending.
