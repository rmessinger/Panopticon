# Camera zoom input handling

How mouse wheel and touchpad scroll gestures adjust tactical-view camera distance.

## Pipeline overview

1. **`GameWindow.HandleMouseWheel`** (`src/OpenSage.Game/GameWindow.cs`) receives raw Veldrid/SDL2 `MouseWheelEventArgs` for both physical mouse wheels and touchpad scroll gestures; SDL reports both through the same event.
2. The handler accumulates fractional deltas in `_scrollAccumulator` and only enqueues a `MouseWheel` input message once a whole notch (`(int)_scrollAccumulator != 0`) has accrued.
3. **`LookAtTranslator`** (`src/OpenSage.Game/Graphics/Cameras/LookAtTranslator.cs`) consumes `InputMessageType.MouseWheel` messages and calls `TacticalView.ZoomIn()`/`ZoomOut()` once per accumulated notch.
4. **`TacticalView.ZoomIn`/`ZoomOut`** (`src/OpenSage.Game/Graphics/Cameras/TacticalView.cs`) adjust the *target* `_heightAboveGround` by ±10 via `SetHeightAboveGround`.
5. **`TacticalView.Update`** eases the actual `_zoom` value toward the height-derived target every frame, at a rate controlled by `GameData.CameraAdjustSpeed`. This is a straight port of the original engine's `View::zoomIn`/`zoomOut` and update loop, so zoom changes are never snapped instantly, they always ease in.

## Why accumulation is needed

Physical mouse wheels report whole-number deltas per notch (typically ±1), so each notch produces an immediate `MouseWheel` message. Touchpads instead report a stream of small fractional deltas per gesture (macOS trackpad scrolling in particular also has inertia/"bounce" physics: the delta stream can briefly reverse sign as the gesture settles even though the user's intent didn't change direction).

`_scrollAccumulator` smooths this: fractional deltas build up until a full notch is reached, at which point one `MouseWheel` message is enqueued and the used whole-number part is subtracted back out, leaving only the fractional remainder.

## Direction-reversal debounce

`GameWindow.HandleMouseWheel` also discards a single reversal event when:

- the new delta's sign differs from `_lastScrollDirection`, **and**
- `_scrollAccumulator` still holds a nonzero fractional remainder from the in-progress gesture.

This targets touchpad inertia bounce specifically. A physical mouse wheel always fully consumes its accumulator on each notch (leaving it at exactly `0`), so a deliberate direction change on a mouse wheel is never mistaken for inertia and is applied immediately.

## `_zoom` vs `_heightAboveGround`

- `_heightAboveGround` is the *desired* camera height above terrain, set instantly by `ZoomIn`/`ZoomOut`.
- `_zoom` is the *actual* camera-distance multiplier (clamped to `[_minZoom, _maxZoom]`) applied to `_cameraOffset` when building the camera transform. It only ever changes inside `TacticalView.Update`, easing toward the height-derived target at `CameraAdjustSpeed`.

Keeping these separate, and never writing `_zoom` directly outside of `Update`, is what gives scroll-wheel zoom its smooth, single easing curve instead of a snap followed by a corrective drift.
