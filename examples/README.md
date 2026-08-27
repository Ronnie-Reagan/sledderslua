# Examples

Copy these scripts into `Sledders/LuaMods` and edit them. They are intentionally small rather than being complete production mods.

## Start here

- `FrameworkBasics.lua` — local sled, input, headlights, fuel and velocity.
- `HeadlightToggle.lua` — smallest useful state toggle.
- `ForwardBoost.lua` — sled-local motion.
- `SimpleHUD.lua` — `onDraw()` and immediate-mode drawing.
- `TopSpeedTracker.lua` — ticking + persistent storage.
- `FolderMod/` — multi-file layout, manifest and `require()`.

## Framework examples

- `HorsepowerTune.lua` — writable vehicle-definition horsepower.
- `NativeHud.lua` — native HUD element visibility/meter control.
- `MaterialColor.lua` — named sled renderer groups and material colors.
- `CameraProjection.lua` — world-to-GUI projection.
- `AudioInspector.lua` — enumerate currently playing Unity `AudioSource`s.
- `WorldControl.lua` — snow/time/weather reads and writes.
- `PhysicsRaycast.lua` — collision/world queries.
- `NativeInput.lua` — enumerate the game's Unity Input System actions.

## Existing focused examples

- `CameraFov.lua`
- `EngineToggle.lua`
- `ForcedHeadlights.lua`
- `FuelWarning.lua`
- `Refuel.lua`
- `SpeedLogger.lua`
- `StoragePosition.lua`

For the complete surface and exact units, see `docs/API.api`.
