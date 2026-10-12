# X2: Capacity-probe graphics resource fixes

> Audience: human + agent.
> Phase and dependencies: [Track X](../track-x-fixes-tweaks.md). Depends on M1; independent of X1.

Architecture: [upload lifecycle](../agent/cards/WI-X2-01-bound-upload-resource-lifetimes.md#architecture-diagrams). Model/FX reuse diagrams remain gated on post-cleanup allocation evidence.

| Card | Status | Scope | Prerequisites |
|---|---|---|---|
| [WI-X2-01](../agent/cards/WI-X2-01-bound-upload-resource-lifetimes.md) | Done | Bound temporary graphics-upload resource lifetimes | none |
| [WI-X2-02](../agent/cards/WI-X2-02-reuse-combat-fx-model-resources.md) | Draft | Reduce combat FX/model resource recreation | WI-X2-01 |

## Capacity-probe evidence

Source: `perf/Report-54283f5a5797494a.diagsession`, captured on Windows with D3D11. The report is a local diagnostic artifact, not a required checked-in test fixture.

- Recorded process working set reaches 13.49 GB. The managed snapshot contains approximately 330 MB and 3.41 million objects. Native heap profiling was disabled.
- The snapshot contains 3,479 `D3D11CommandList`, 35,577 `D3D11Buffer`, 929 `ParticleSystem`, 2,567 `ModelInstance`, and 12,969 `ModelMeshPartInstance` objects. Counts alone do not establish root paths, disposal state, or native retained bytes.
- `CreateStaticBuffer` queues a temporary command list and staging buffer through `DisposeWhenIdle`. The vendored graphics device drains that queue through `WaitForIdle`, not automatically on submission or presentation. Particle-system construction invokes this upload path for its index buffer.
- The ETL shows increased particle-system and graphics-resource allocation activity late in the capture. Named model buffers include `AIRNGR_SKN.MUZZLEFX01.RenderItemConstantsPS`. Repeated naming events do not establish one new instance per event or per shot.

Working diagnosis: undrained upload temporaries retain native graphics resources during combat. Separate optimization candidate: repeated combat model/FX resource construction. Neither the exact native-byte attribution nor the model recreation owner was established by the initial analysis. The cards require independent reproduction and measurement before declaring either issue resolved.


## X2-01 results

Code, regression tests, and the Linux/Vulkan probe comparison are recorded in the [card Result](../agent/cards/WI-X2-01-bound-upload-resource-lifetimes.md#result). Summary: upload helpers drain per upload; queue-drain tests fail on the pre-fix build; the 900-frame probe shows identical battles and no measurable tick-time cost. Outstanding: Windows D3D11 native-memory measurement (operator command recorded in the card).

## X2-02 reconciliation

Still Draft. The Draft gate requires post-cleanup allocation stacks on the reporting platform (Windows/D3D11). Linux probe orientation only: managed allocation churn is approximately 11.1 MB per median tick (17.5 GB over 900 frames), which bounds the optimization surface but does not identify an owner. Owner identification must come from the D3D11 session that also produces the X2-01 native evidence; combine both into one Windows run.
