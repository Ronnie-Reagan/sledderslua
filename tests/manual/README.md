# Manual runtime tests

Run these against the current Sledders build before cutting an RC or stable release.

## Normal runtime smoke test

Copy RuntimeSmokeTest.lua into Sledders/LuaMods and load a level with the local player on a sled.

Check startup, hot reload, drawing, and input matching. L and Ctrl+L must trigger separately.

Press Ctrl+Shift+5 while stationary. The safe API sweep exercises game/time/window/screen metadata, player and sled discovery, sled reads, identity/no-op writes for velocity, force, mass, headlights, engine, fuel, camera FOV, audio volume, and a storage round trip. It should finish with every check passing.

The other hotkeys are deliberately state-changing tests:

- Ctrl+Shift+1: print current sled state.
- Ctrl+Shift+2: add +10 m/s local forward velocity.
- Ctrl+Shift+3: teleport +5 m on world Y.
- Ctrl+Shift+4: toggle the HUD.

For callback fault isolation, temporarily add error("smoke test") inside onDraw(). The error should be logged once and only onDraw should remain suspended until the next successful reload.

For syntax-error hot reload, introduce a Lua syntax error and save. The last working script should stay loaded. Fix the syntax and save again.

## Developer reflection smoke test

Copy the whole DevSmokeTest folder into Sledders/LuaMods/DevSmokeTest. It has an explicit dev permission in manifest.lua.

Press Ctrl+Shift+9 in a loaded level. The test is read-only: it resolves UnityEngine.Time, reads static metadata, discovers camera-related types/objects, and reads camera member information through the reflection proxy. It should report a passing summary without mutating game state.

Use the MelonLoader log when reporting a failed binding. Include the current Sledders build and runtime commit/tag.
