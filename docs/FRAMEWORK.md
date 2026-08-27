# Framework coverage

API 3.2 is structured so common Sledders mods can stay entirely on the stable Lua surface.

## Stable layers

1. **Convenience layer** — player/sled, input, screen, storage, game/time/window.
2. **Subsystem layer** — body, vehicle, tuning, structure, visuals, HUD, camera/photo, audio, world, scene, physics, assets.
3. **Semantic property bags** — advanced named read/write state without exposing obfuscated CLR names.
4. **Developer reflection** — opt-in investigation/escape hatch.

## Core mod categories

| Mod category | Stable framework path |
| --- | --- |
| Engine/vehicle tuning | `sled:getVehicle()`, `sled:getTuning()` |
| Live physics | `sled`, `sled:getBody()`, `sledders.physics` |
| Suspension/shocks | `sled:getTuning():getSuspension()/getShock()` |
| Rider mods | `sledders.player`, player transform/state/renderers |
| Graphics/materials | `sled:getStructure()`, `sledders.visual`, renderer/material wrappers |
| Native HUD mods | `sledders.hud`, HUD element wrappers |
| Custom overlay HUD | `sledders.screen` |
| Camera/director tools | `sledders.camera`, `.free`, `.photo`, projection/rays |
| Input/controller tools | `sledders.input`, `sledders.input.native` |
| Audio tools | `sledders.audio`, source/clip wrappers, WAV, native SFX/presets |
| Snow/weather/time | `sledders.world.snow/time/weather` |
| Fuel/stations | sled fuel API + `sledders.world.fuel` |
| World/scene utilities | `sledders.scene`, transform/render/audio/component wrappers |
| Collision/AI sensing | `sledders.physics.raycast/overlapSphere` |
| Custom models/assets | `sledders.assets` AssetBundle wrappers |
| Persistence | `sledders.storage` |
| Reverse engineering | gated `sledders.dev` |

## Getter/setter policy

A stable getter is paired with a setter when the value is genuinely writable and the current game exposes a reliable mutation path.

Read-only values are intentionally limited to derived/immutable telemetry such as current speed, RPM, raycast hit distance, audio clip length, and similar measurements.

Actions use verbs instead of fake setters: `teleport`, `addForce`, `play`, `rescue`, `nextMode`, etc.

## What remains developer-only

Do not promote a member merely because reflection can reach it. Keep it behind `sledders.dev` until its ownership, units, enum meanings, side effects and lifetime are understood.

Examples of things that may remain developer-only on a given game build:

- heavily obfuscated subsystems with unknown semantics;
- network mutation/authority internals;
- arbitrary private game methods that bypass normal state transitions;
- unsupported asset formats or arbitrary filesystem/network access;
- Wwise internals that cannot be represented honestly as Unity `AudioClip` PCM.

The framework should expand by promoting proven semantic operations out of this layer over time.
