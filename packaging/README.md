# Sledders Lua Modding

Full documentation: https://donreagan.ca/sledderslua/

You do not need to compile anything to make Lua mods.

## Install a Lua mod

Put a single `.lua` file in:

```text
Sledders/LuaMods/
```

Or install a folder mod as:

```text
Sledders/LuaMods/MyMod/
  main.lua
  manifest.lua
```

Most Lua mods can be added or edited while Sledders is running. Save the file and the runtime will hot-reload it.

## Make your first mod

Open the `Examples` folder beside this README. Copy a small example into `Sledders/LuaMods`, rename it, and edit it.

Good first examples:

- `HeadlightToggle.lua`
- `ForwardBoost.lua`
- `Refuel.lua`
- `SimpleHUD.lua`
- `TopSpeedTracker.lua`

The compact callable function list is `API.api` beside this README.

## Basic shape

```lua
local sled = sledders.player.getSled()

if sled then
    print(sled.getSpeed())
end
```

Callbacks are functions the runtime calls for you:

```lua
function onTick(dt)
end

function onDraw()
end

function onKey(key)
end
```

You only define the callbacks your script needs.

## Movement

For sled-local velocity:

```text
X = right / left
Y = up / down
Z = forward / backward
```

So a simple forward boost is:

```lua
sled.addVel(0, 0, 25)
```

Functions containing `WorldVel` use fixed map/world axes instead.

## Errors

Read the MelonLoader console when a script fails. Fix the Lua file and save it again.

A syntax error during hot reload leaves the last working copy running until the file is corrected.
