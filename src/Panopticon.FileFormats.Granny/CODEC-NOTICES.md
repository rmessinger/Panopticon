# Codec provenance notices

Panopticon's Granny section decoders are original C# written against published
written specifications from the reverse-engineering community, with the listed
implementations used as cross-check references only. No upstream code is copied
or translated verbatim, and no proprietary Granny SDK material, binaries, or
excluded sources are consulted. Full review: `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` section 6.

## Oodle1 (GR2 section compression type 2)

- Written specification: liboodle `README.md`,
  `github.com/LunaticInAHat/liboodle@7837c253f8ca505a3ba3cd8e35d8bc3f5ab76a30`
  (Unlicense; the public-domain dedication is restated in that project's files).
  Documents the bitstream, symbol coder, LZ layer, 12-byte stream header, and
  the Granny three-stream section wrapper.
- Cross-check reference implementation: blendergranny
  `io_scene_gr2/gr2/decompress/oodle1.py`,
  `github.com/Rasetsuu/blendergranny@aec91bbd8e244d82277d1f5435dc20feff3086f9`
  (MIT, Copyright (c) 2026 ciupix and contributors; that repository's
  `docs/PROVENANCE.md` likewise bars proprietary-derived code).
- Local parity evidence: the AoE3 DE falconet GR2 section 0 decodes to the same
  SHA-256 digest in both implementations (asserted by the C10d Interactive
  test; digest recorded there).

## BitKnit2 (GR2 section compression type 4)

- Written specification: libbg3 `docs/bitknit2.txt`,
  `github.com/eiz/libbg3@23c701a943a189a789163af40b0398e290d2681a`
  (MIT, Copyright 2024 Mackenzie Straight).
- Cross-check reference implementation: pybg3 `src/rans.h`,
  `github.com/eiz/pybg3@3b02e59bb4484c27e49e51ee9d83e8a9d09df4ed`
  (MIT, Copyright 2024 Mackenzie Straight).
- Local parity: every non-empty BitKnit2 section of the six AoE3 DE BitKnit2
  model files (24 sections, 48 bytes to 5.9 MB, up to 90 rANS quanta) decodes
  identically under the pybg3 reference and this C# decoder; the sloop_damaged
  section 0 and its compressed relocation blob are pinned by SHA-256 in the
  C10d Interactive tests (digests only, no assets).
