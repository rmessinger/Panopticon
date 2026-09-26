# Task Card `WI-C7a-02`: Decode BLTE spans and the encoding file

| Field | Value |
|---|---|
| Card ID | `WI-C7a-02` |
| Roadmap phase | [Track C · C7a](../../track-c-external-assets.md): StarCraft II CASC storage |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7a-01` |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Add `BlteDecoder` and `CascEncoding` so a content key resolves to decoded bytes, with explicit rejection of encrypted and unknown frames.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 3.4, 3.5, and 11.
- `src/Panopticon.FileFormats.Casc/CascIndex.cs` from `WI-C7a-01`.

## Steps

1. Add `BlteDecoder.Decode(Stream span)`: skip the 30-byte archive header, check `BLTE`, read the big-endian header size, then either one frame or the chunk table (flags 0x0F, `u24` count, per-chunk sizes and MD5).
2. Decode frames: `N` copies, `Z` inflates through `ZLibStream`. Verify each chunk's MD5 and decompressed size (`InvalidDataException` on mismatch).
3. Reject `E` with `NotSupportedException` whose message contains "encrypted BLTE frame", and every other mode with the mode byte.
4. Add `CascEncoding.Parse(byte[] decoded)`: validate `EN` and the key sizes, skip the ESpec block and page table, and read content pages into a dictionary from content key to (first encoding key, size). Stop a page at a zero key count.
5. Add `CascArchiveSet`, which opens `data.NNN` files lazily and reads a `CascSpan` into bytes. It is thread-safe per read and disposes handles.
6. Synthetic tests: build BLTE spans for single-frame `N`, multi-chunk `N` + `Z`, a bad MD5, `E`, and `F`. Build a two-page encoding file and look up keys in both pages.

## Files likely to change

- `src/Panopticon.FileFormats.Casc/BlteDecoder.cs`, `CascEncoding.cs`, `CascArchiveSet.cs` (new)
- `test/Panopticon.Simulation.Tests/Data/Casc/BlteDecoderTests.cs`, `CascEncodingTests.cs` (new)

## Acceptance criteria

- [ ] `N` and `Z` chunks round-trip, including a multi-chunk span.
- [ ] A bad chunk MD5 fails with `InvalidDataException`.
- [ ] `E` fails with "encrypted BLTE frame", and `F` fails naming the mode.
- [ ] Encoding lookup finds keys on both pages and returns the recorded sizes.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No decryption, no key files, no CDN access.
- Do not parse the root. That is `WI-C7a-03`.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C7a-03` is done, change `WI-C7a-04` to `Ready`.
- Append a result line to `.exchange`.
