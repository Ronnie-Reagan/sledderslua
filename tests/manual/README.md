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

For runtime-error hot reload, add a valid top-level statement that throws during script execution, such as `error("reload preparation smoke test")`, and save. The last working script must remain active. Remove the error and save again; the replacement should then load normally.

At startup on the current Sledders build, confirm the binding log reports exact `Controller` and `NetClient` types. If the runtime reports that exact local-sled binding is unavailable, treat the compatibility fallback as a release warning and investigate before tagging.

## Developer reflection smoke test

Copy the whole DevSmokeTest folder into Sledders/LuaMods/DevSmokeTest. It has an explicit dev permission in manifest.lua.

Set `EnableDevApi` to `true` in `UserData/SleddersLua/config.json` and restart Sledders before running this test. The runtime defaults developer reflection off; both this owner-side setting and the mod's `"dev"` manifest permission are required.

Press Ctrl+Shift+9 in a loaded level. The test is read-only: it resolves UnityEngine.Time, reads static metadata, discovers camera-related types/objects, reads camera member information, compares the live local sled's `mainBody.mass` with `VehicleScriptableObject.weight`, and inventories the native teleport entry points. It should report a passing summary without mutating game state.

Record the two `[PROBE]` lines when validating a release candidate. The mass probe is evidence for deciding whether API `getMass/setMass` should mean the live main Rigidbody mass or the vehicle-definition weight; do not change that semantic from metadata alone.

Use the MelonLoader log when reporting a failed binding. Include the current Sledders build and runtime commit/tag.
