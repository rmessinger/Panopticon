# Track C · C4f: EE2 scale animation

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4f row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c4f-ee2-scale-animation.md](../deep-dives/c4f-ee2-scale-animation.md), including its
[architecture diagrams](../deep-dives/c4f-ee2-scale-animation.md#12-architecture-diagrams).

## Scope

Import NIF 10.1 KF uniform scale keys through the C4c parser and curve evaluator, preserve finite
positive bone rest scale in C1 and the runtime, and prove DDF-bound EE2 scale clips on skinless
models. Singular rest-scale animated nodes are diagnosed, not approximated.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4f-01`](https://projects.reid-corp.com/work_packages/511) | C1 and runtime rest scale plus scale animation channel. |
| 2 | [`WI-C4f-02`](https://projects.reid-corp.com/work_packages/512) | NIF node rest scale preserved and collapse rule relaxed. |
| 3 | [`WI-C4f-03`](https://projects.reid-corp.com/work_packages/513) | KF scale keys converted through C4c curve evaluation. |
| 4 | [`WI-C4f-04`](https://projects.reid-corp.com/work_packages/514) | Normal, bounds, and skinning invariants under uniform scale. |
| 5 | [`WI-C4f-05`](https://projects.reid-corp.com/work_packages/515) | Installed DDF-bound scale clip descriptor and pose checks. |
| 6 | [`WI-C4f-06`](https://projects.reid-corp.com/work_packages/516) | Deterministic textured captures showing scale animation. |

Cards 1 and 2 can proceed in parallel after C4c. Card 3 needs cards 1 and 2. Card 4 follows card
3. Card 6 is the completion point.

---

## Agent context

- Parser boundary: C4f reuses C4c `NiKeyframeData.Scales`; no new NIF block family is added.
- Interchange: `ImportedBone.BindScale` defaults to 1, and `ImportedAnimationChannelType.Scale`
  reuses float keyframes.
- Runtime: `ModelBone.Transform.Scale` stores rest scale, and `AnimatedOffset.Scale` stores the
  rest-relative scale offset.
- Offset rule: emit `sampleScale / restScale`; for translation on scaled rests, divide the
  rest-frame translation delta by rest scale before applying inverse rest rotation.
- Source conversion: C4a/C4c source-to-engine rotation conjugation leaves uniform scale unchanged;
  the 3.28084 unit factor applies only to translation.
- Proof data: `EMPIRE_EARTH_II_PATH/zips/graphics.zip` and `db.zip`. Tests that require them are
  interactive.
- Cross-phase note: C4d skinned proofs must include rest scale in inverse bind matrices and must
  not truncate influences while adding scale support.
