# Manual runtime tests

Run these against the current Sledders build before cutting an RC or stable release.

## Normal API 3.2 runtime smoke test

Copy `RuntimeSmokeTest.lua` into `Sledders/LuaMods` and load a gameplay level with the local player on a sled.

Startup should report exact current-build bindings rather than compatibility fallback warnings for the audited core members.

Press **Ctrl+Shift+5** while stationary. The safe sweep covers:

- API 3.2 metadata/capabilities;
- time/window/screen/value constructors;
- deterministic local sled/rider discovery;
- player transform identity writes;
- sled telemetry and no-op physics writes;
- vehicle-definition get/set;
- Rigidbody wrapper get/set;
- tuning property-bag discovery;
- structure/renderers/material wrapper discovery;
- persistent headlight override lifecycle;
- engine/fuel identity writes;
- camera projection/rays and FOV identity write;
- native HUD discovery;
- native Input System action discovery;
- world snow/time/weather reads plus identity writes;
- audio source/native-SFX/preset discovery;
- scene and physics services;
- structured storage round trip.

Every check should pass.

Other keys:

- `L` and `Ctrl+L` must trigger separately (modifier matching).
- `Ctrl+Shift+1` prints current framework/sled state.
- `Ctrl+Shift+2` adds +10 m/s local forward velocity.
- `Ctrl+Shift+3` teleports +5 m on world Y.
- `Ctrl+Shift+4` toggles only the smoke-test overlay.

### Hot reload failure isolation

For syntax failure, introduce invalid Lua and save. The last working VM must remain active. Fix it and save again.

For top-level runtime failure, temporarily add:

```lua
error("reload preparation test")
```

at file scope and save. The previous VM must remain active. Remove the error and save again.

For callback isolation, put `error("callback test")` inside `onDraw()`. Only `onDraw` should be suspended; other callbacks continue. A successful reload re-enables it.

### Storage handoff

1. Set a storage value from the working mod.
2. Immediately edit/save the Lua file before the normal autosave delay expires.
3. Confirm the reloaded VM sees the latest value.
4. Add an `onUnload` storage change and repeat; the candidate should inherit that final old-VM value unless the new candidate explicitly changed the same key.

### Scene wrapper invalidation

Retain a sled/scene-object wrapper in a global, leave the level, then load another level. The old wrapper must report invalid rather than resolving to a new object. Fetching a fresh wrapper should work normally.

## Developer reflection smoke test

Developer reflection is intentionally not required for normal framework features.

Copy the entire `DevSmokeTest` folder into `Sledders/LuaMods/DevSmokeTest`.

The manifest requests `dev`; also set:

```json
"EnableDevApi": true
```

in `UserData/SleddersLua/config.json`, then restart Sledders.

Press **Ctrl+Shift+9** in a loaded level. The test is read-only and verifies type/static-member/object discovery through the developer layer.

Disable `EnableDevApi` again for normal use unless you actively need reflection tooling.

## Assembly contract

Before the in-game tests, validate the exact managed assembly used by the game:

```text
python tools/audit_assembly.py "C:\...\Sledders_Data\Managed\Assembly-CSharp.dll" tests/bindings/current.json
```

A passing metadata contract is required, but does not replace these live tests.
