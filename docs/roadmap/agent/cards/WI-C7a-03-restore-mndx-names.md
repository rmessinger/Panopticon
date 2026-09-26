# Task Card `WI-C7a-03`: Restore MNDX root names

| Field | Value |
|---|---|
| Card ID | `WI-C7a-03` |
| Roadmap phase | [Track C · C7a](../../track-c-external-assets.md): StarCraft II CASC storage |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7a-01` |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Add `MarTrie` and `MndxRoot` so the MNDX root yields every `(path, content key, size)` entry, proven on synthetic tries.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 3.6, 3.7, and 11.
- CascLib `CascRootFile_MNDX.cpp` (MIT, Ladislav Zezula) may be read as a reference. Credit it in the `MarTrie` XML summary. Do not read unlicensed ports.

## Steps

1. Add `MarBitVector`: load the section 3.7 BitVector, keep the raw `u32` words, and build prefix popcounts per 512-bit block for `Rank1`. Build `Select1` and `Select0` by binary search over those counts plus in-word scanning. Ignore the serialized rank and select tables.
2. Add `MarTrie.Load(reader, isTopLevel)` in the section 3.7 load order, including the optional nested trie. Top-level loads check the `MAR\0` signature.
3. Add `MarTrie.Restore(int keyId)` following the four key-restore steps, and a non-reversing `RestoreLink` for nested tries. Tails end at NUL.
4. Add `MndxRoot.Parse(byte[] decoded)`: validate the header (versions 2 and 2), read MAR infos and content-key entries, and split name groups by bit 31. Yield `<package[pkgIndex]>/<name>` for every entry of group `i` with name `i` from MAR 1. Drop duplicate paths with equal content keys. Throw `InvalidDataException` on duplicate paths with different keys.
5. Add a test helper `MarTestTrieBuilder` that serializes a small trie (LOUDS, terminal, link, bases, extras, tail, and one nested trie) for a known key set. Keep it simple: fixed inputs, not a general marisa builder.
6. Synthetic tests: restore every key of a three-level trie with shared prefixes, and parse a synthetic MNDX root with two packages and a duplicate path.

## Files likely to change

- `src/Panopticon.FileFormats.Casc/MarBitVector.cs`, `MarTrie.cs`, `MndxRoot.cs` (new)
- `test/Panopticon.Simulation.Tests/Data/Casc/MarTestTrieBuilder.cs`, `MarTrieTests.cs`, `MndxRootTests.cs` (new)

## Acceptance criteria

- [ ] Rank and select agree with a naive scan on random 10,000-bit vectors (fixed seed).
- [ ] Every synthetic key restores exactly, including link and nested-trie paths.
- [ ] MNDX paths combine package and name, and duplicates follow the section 3.6 rule.
- [ ] A non-MNDX root and an MNDX version other than 2 fail with `NotSupportedException`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Enumeration only. No key lookup by string through the trie is needed, because `CascFileSystem` indexes restored names.
- Do not copy GPL or unlicensed code.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C7a-02` is done, change `WI-C7a-04` to `Ready`.
- Append a result line to `.exchange`.
