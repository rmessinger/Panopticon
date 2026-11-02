# Task Card `WI-G12-02`: Fetch open materials

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G12-02` |
| Roadmap phase | [Track G · G12](../../track-g-rendering-clarity.md): Open material packs |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `S` |
| Prerequisites | `WI-G12-01` |
| Diagrams | [G12 architecture](../../deep-dives/g12-open-material-packs.md#5-architecture-diagrams) |

## Goal

Download every material source named in the mapping table from the command line, with licences recorded and unclear licences rejected.

## Context to read (and only this)

- [G12 design](../../deep-dives/g12-open-material-packs.md) sections 2 and 3.2, and the fetch-and-bake sequence.
- `src/Panopticon.Tools.TestHarness/terrain-material-map.json`.
- `scripts/tests/test_annotate_capture_gif.py` (Python test style).

## Steps

1. Add `scripts/textures/fetch_materials.py <work dir> [--map <path>] [--resolution 1K|2K]` (Python standard library plus `curl` if needed). Per source id, query the provider's public download API without account or key, download the archive with resume, and extract colour, normal (OpenGL convention), roughness, and ambient occlusion into `<work dir>/<source id>/`.
2. Write `licences.json`: source id, provider, licence identifier, attribution text, page URL, archive URL, retrieval date, and file SHA-256 values.
3. Reject any source whose provider metadata is not CC0, or CC-BY with attribution text; exit non-zero listing rejects.
4. Re-runs reuse complete downloads and verify hashes.
5. Add `scripts/tests/test_fetch_materials.py` (offline, network stubbed): map parsing, map-kind extraction by file name, licence filter, and resume logic.

## Files likely to change

- `scripts/textures/fetch_materials.py` (new)
- `scripts/tests/test_fetch_materials.py` (new)

## Acceptance criteria

- [ ] An empty work dir fills unattended for every mapped source, with `licences.json` complete.
- [ ] A second run downloads nothing.
- [ ] Offline tests pass; nothing downloaded is committed.

## Validation commands

```sh
python3 -m unittest discover -s scripts/tests -p "test_fetch_materials.py"
python3 scripts/textures/fetch_materials.py "<work dir>"
```

## Out of scope / guardrails

- Never run in CI; the script is operator-local.
- Providers that need accounts, keys, or interactive licence acceptance.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G12-03` to `Ready`.
- Append a result line to `.exchange`.
