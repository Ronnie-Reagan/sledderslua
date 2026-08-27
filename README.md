# Sledders Lua

Lua modding framework for **Sledders** without requiring every mod author to compile a DLL.

**Documentation:** https://donreagan.ca/sledderslua/

The framework is designed around two goals:

- **Very low barrier to entry:** a useful mod can be one `.lua` file copied into `Sledders/LuaMods`.
- **High capability ceiling:** normal stable APIs cover sled tuning/physics, rider state, native HUD, camera/photo mode, audio, materials/renderers, world/weather/snow/time/fuel, native input, physics queries, scene objects and AssetBundles. Raw developer reflection is the last resort, not the normal way to mod the game.

A script can be edited while the game is running. The runtime hot-reloads changed source while preserving the last working VM when a candidate edit fails.

## Install the runtime

Requirements:

- Sledders
- MelonLoader 0.7.1

1. Close Sledders.
2. Extract a Sledders Lua release into the game directory.
3. Start Sledders.

A release installs roughly:

```text
Sledders/
  Mods/
    SleddersLuaRuntime.dll
  UserLibs/
    MoonSharp.Interpreter.dll
  LuaMods/
  SleddersLua/
    API.api
    Examples/
```

The MelonLoader console should report `Sledders Lua Runtime` during startup.

## Your first mod

Create:

```text
Sledders/LuaMods/MyFirstMod.lua
```

Paste:

```lua
function onLoad()
    print("My first Sledders Lua mod loaded")
end

sledders.input.onPressed("h", function()
    local sled = sledders.player.getSled()
    if sled then
        sled.toggleHeadlights()
    end
end)
```

Save the file. With hot reload enabled, you normally do not need to restart Sledders.

## Common modding is intentionally simple

Get the local sled:

```lua
local sled = sledders.player.getSled()
if not sled then return end
```

Read normal telemetry:

```lua
print(sled.getSpeed())
print(sled.getRpm())
print(sled.getFuel())
```

Change normal state:

```lua
sled.setFuel(20)
sled.setEngineOn(true)
sled.setHeadlights(true)
sled.addVel(0, 0, 10)
```

Tune vehicle definition values without raw reflection:

```lua
local vehicle = sled.getVehicle()
vehicle.setHorsepower(180)
vehicle.setMaxRpm(9000)
vehicle.setFuelCapacity(50)
```

Physics remains separate from vehicle-definition values:

```lua
local body = sled.getBody()
body.setMass(240)
body.setAngularDamping(0.1)
```

So `vehicle.weight` and live Rigidbody `body.mass` do not get conflated.

## High-ceiling stable APIs

### Tuning

```lua
local tuning = sled.getTuning()
local controller = tuning.getController()
controller.set("clutchRpmMin", 4500)
controller.set("clutchRpmMax", 8200)

local suspension = tuning.getSuspension()
suspension.set("antiRollBarFactor", 0.25)
```

Property bags expose `keys()`, `get()`, `set()` and `isWritable()` so advanced mods can enumerate stable named properties without using obfuscated fields.

### Graphics and materials

```lua
local hood = sled.getRenderers("hood")
if hood[1] then
    local material = hood[1].getMaterial()
    if material then
        material.setColor(sledders.color(1, 0, 0))
    end
end
```

The visual layer exposes renderer state, per-instance materials, shader property checks, colors, floats and shader keywords.

### Native HUD

```lua
sledders.hud.setElementVisible("rpmMeter", false)
local speed = sledders.hud.get("speedMeter")
if speed then
    speed.setUnit("km/h")
end
```

Whole-HUD hiding is owner-scoped so one mod does not casually steal another mod's HUD state.

### Camera and projection

```lua
local p = sled.getPos()
local screen = sledders.camera.worldToGui(p)

sledders.camera.setMode("DroneMode")
sledders.camera.setDroneDistance(12)
```

The camera service includes native Sledders camera modes, free camera, photo-mode controls, world/screen/GUI conversion and screen rays.

### World

```lua
sledders.world.snow.setCondition("Powder")
sledders.world.time.setTimeOfDay(14.5)
sledders.world.weather.set("Clear")
```

World services cover current snow, time, weather and fuel-system/station state where the current game provides reliable mutation paths.

### Audio

```lua
local playing = sledders.audio.getPlayingSources()
for _, source in ipairs(playing) do
    print(source.getName(), source.getVolume())
end
```

Mods can inspect/control Unity `AudioSource`s, read PCM data from accessible `AudioClip`s, load local WAV files, create owned audio sources, trigger a safe set of native Sledders/Wwise SFX and manipulate native audio presets.

### Scene objects, physics queries and assets

```lua
local hit = sledders.physics.raycast(
    sled.getPos(),
    sledders.vector3(0, -1, 0),
    20
)

local trees = sledders.scene.find("Tree", 64)
```

AssetBundles can be loaded only from inside the mod directory. Instantiated runtime objects are tracked and cleaned up with the mod.

## Input

Simple mods can use the lightweight input API:

```lua
sledders.input.onPressed("ctrl+h", function()
    print("Ctrl+H")
end)
```

Advanced mods can inspect the game's actual Input System:

```lua
local actions = sledders.input.native.actions()
```

## Folder mods

For multiple files:

```text
Sledders/LuaMods/MyMod/
  main.lua
  manifest.lua
  other.lua
```

Example `manifest.lua`:

```lua
return {
    id = "example.my-mod",
    name = "My Mod",
    author = "Me",
    version = "1.0.0",
    api = "3.2"
}
```

Use `require("other")` from `main.lua`.

## Developer reflection

`sledders.dev` exists for reverse engineering and unusual experiments. It is intentionally **not** the expected path for ordinary tuning, graphics, HUD, camera, audio or world mods.

It requires both:

1. `permissions = { "dev" }` in the mod manifest; and
2. `EnableDevApi = true` in `UserData/SleddersLua/config.json`.

It is disabled by default.

## Current-build binding assurance

The repository contains `tests/bindings/current.json`, generated from the current Sledders managed assembly used during API development, plus `tools/audit_assembly.py`.

Maintainers can check a locally owned game assembly with:

```text
python tools/audit_assembly.py "C:\path\to\Assembly-CSharp.dll" tests/bindings/current.json
```

The contract checks exact fields and method signatures. It is a compatibility gate, not a replacement for in-game smoke tests.

## API reference and examples

- Compact complete reference: [`docs/API.api`](docs/API.api)
- Runnable mods: [`examples/`](examples/)
- Maintainer/build notes: [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)

## Errors and hot reload

A broken repeating callback is suspended instead of throwing every frame.

During hot reload the runtime prepares the replacement VM before unloading the working one. Syntax/top-level errors leave the working copy active. Storage is handed from the old VM to the replacement so rapid edits do not intentionally roll state backward.

## Source and licensing

Sledders Lua Runtime is MIT licensed. See [`LICENSE`](LICENSE).

MoonSharp is redistributed in binary releases under its BSD-style license. MelonLoader is an external requirement and is not bundled. Sledders/Unity assemblies are not distributed by this project.

This project is independent and is not affiliated with Hanki Games, MelonLoader or MoonSharp.
