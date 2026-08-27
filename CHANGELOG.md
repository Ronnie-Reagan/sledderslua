# Changelog

## 0.5.0 - unreleased

- API 3.2 framework expansion.
- Deterministic current-build binding resolver for local sled, rider, vehicle, structure, fuel, RPM, engine/headlight state and related core anchors, with compatibility discovery retained as fallback.
- Stable high-level services for sled physics, vehicle definition/tuning, suspension/shocks, structure/renderers/materials, rider state, native HUD, cameras/photo mode, world/snow/time/weather/fuel, native input, audio, scene objects, physics queries and AssetBundles.
- Getter/setter symmetry for writable stable properties; derived telemetry remains read-only rather than exposing fake setters.
- Vehicle definition weight is separated from live Rigidbody mass.
- Native fuel remains normalized internally while the Lua API consistently exposes litres.
- Owner-scoped persistent headlight and HUD state.
- Handle-based wrappers and scene invalidation prevent stale Unity object references from being retained or silently rebound after scene changes.
- Object handle IDs are not recycled during a mod VM lifetime.
- Hot reload prepares a candidate VM before replacing the working VM and uses content fingerprints rather than timestamps.
- Hot reload storage handoff preserves final old-VM `onUnload` state while allowing candidate changes to win.
- Temporary file I/O failures are retried rather than permanently suppressing a source fingerprint.
- Storage autosave uses wall-clock time, is independent of game time scale, bounds table size/indexes, rejects cycles and non-finite numeric values, and retains backup recovery.
- Developer reflection requires both the manifest `dev` permission and owner-side `EnableDevApi=true`; it remains disabled by default.
- Current-game assembly contract and zero-dependency metadata auditor added under `tests/bindings` and `tools/audit_assembly.py`.
- Canonical naming uses `getRpm()` / `getFps()` while keeping `getRPM()` / `getFPS()` compatibility aliases.
- Expanded manual smoke testing and examples for framework services.

## 0.4.0 - unreleased baseline

- API 3.1.
- Sled-local velocity and force helpers with explicit world-space versions.
- Native Sledders teleport path with Rigidbody fallback.
- One-shot and forced headlight control.
- Per-callback fault isolation.
- Per-mod JSON storage with delayed writes and backup recovery.
- Exact Ctrl/Shift/Alt matching for registered key chords.
- CI, nightly, tagged release, and Pages workflows.
