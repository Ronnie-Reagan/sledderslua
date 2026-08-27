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
- Developer reflection now requires owner-side `EnableDevApi=true` in addition to a mod's `"dev"` manifest permission; the setting defaults off.
- Existing runtime config files are normalized on load so newly added settings are written explicitly.
- Per-mod JSON storage with delayed writes and backup recovery.
- Exact Ctrl/Shift/Alt matching for registered key chords.
- CI, nightly, tagged release, and Pages workflows.
