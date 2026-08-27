# Changelog

## 0.4.0 - unreleased

- API 3.1.
- Sled-local velocity and force helpers with explicit world-space versions.
- Native Sledders teleport path with Rigidbody fallback.
- One-shot and forced headlight control.
- Per-callback fault isolation.
- Hot reload keeps the last working script when a changed file fails syntax or top-level execution during reload preparation.
- Current-build local sled/player discovery prefers deterministic Sledders controller singletons before compatibility scans.
- Storage autosave uses runtime wall-clock time so pause/time-scale changes do not delay persistence.
- Per-mod JSON storage with delayed writes and backup recovery.
- Exact Ctrl/Shift/Alt matching for registered key chords.
- CI, nightly, tagged release, and Pages workflows.
