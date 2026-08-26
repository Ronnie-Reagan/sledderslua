# Sledders Lua

Lua modding for Sledders without compiling a DLL.

**Documentation:** https://donreagan.ca/sledderslua/

A Lua mod can be a single `.lua` file. Put it in `Sledders/LuaMods`, start the game, edit the file, save it, and the runtime hot-reloads it.

## Install the runtime

You need Sledders with MelonLoader 0.7.1 already installed.

1. Download a versioned release or a nightly build from the Releases page.
2. Close Sledders.
3. Extract the release ZIP into the Sledders game folder.
4. Start Sledders.

The ZIP installs:

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

The MelonLoader console should show `Sledders Lua Runtime` during startup.

## Install a Lua mod

Single-file mod:

```text
Sledders/LuaMods/MyMod.lua
```

Folder mod:

```text
Sledders/LuaMods/MyMod/
  main.lua
  manifest.lua
```

Most scripts can be added, edited, or removed while Sledders is running. The runtime scans `LuaMods` and reloads changed scripts automatically.

## Make your first mod

Start by copying something from `SleddersLua/Examples` into `LuaMods` and changing it.

Good first samples:

- `HeadlightToggle.lua`
- `ForwardBoost.lua`
- `Refuel.lua`
- `SimpleHUD.lua`
- `TopSpeedTracker.lua`

Lua files are plain text. Notepad works; a code editor such as VS Code or Notepad++ is easier to read.

A normal script gets the player's sled like this:

```lua
local sled = sledders.player.getSled()
```

From there, normal sled actions are directly on that object:

```lua
sled.getSpeed()
sled.getFuel()
sled.setHeadlights(true)
sled.addVel(0, 0, 25)
```

The compact function list is in [`docs/API.api`](docs/API.api). Runnable code belongs in [`examples/`](examples/).

## The few Lua basics you need first

A variable remembers a value:

```lua
local boost = 25
local enabled = true
local name = "My Mod"
```

A function is a piece of code you can run:

```lua
local function sayHello()
    print("hello")
end
```

The runtime calls certain functions for you:

```lua
function onLoad()
end

function onTick(dt)
end

function onDraw()
end

function onKey(key)
end
```

You only define the callbacks you need.

`nil` means there is no value. A sled can be `nil` while a level is loading, so scripts commonly check it:

```lua
local sled = sledders.player.getSled()
if not sled then return end
```

Positions and movement use values with `x`, `y`, and `z` components. Common setters also accept three separate numbers.

For sled-local velocity:

```text
X = right / left
Y = up / down
Z = forward / backward
```

That makes a forward boost simple even when the sled is turned, pitched, or rolled:

```lua
sled.addVel(0, 0, 25)
```

Functions containing `WorldVel` use fixed map/world axes instead.

## Errors

If a script has an error, read the MelonLoader console. Errors include the Lua filename and line when available.

A broken repeating callback such as `onDraw()` is suspended instead of printing the same failure every frame. Fix the file and save it; hot reload gives it another try.

If a changed script has a syntax error, the last working copy stays loaded. The runtime retries after the file changes again.

## Nightly and versioned releases

- **Versioned releases** are tagged builds such as `v0.4.0`.
- **Nightlies** are dated prereleases built automatically from the default branch when it changes.

Nightlies are for testing. Use a versioned release when you want a fixed build.

## Source and licensing

Sledders Lua Runtime is MIT licensed. See [`LICENSE`](LICENSE).

MoonSharp is redistributed in binary releases under its own BSD license. MelonLoader is an external requirement and is not bundled. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

Sledders Lua Runtime is an independent modding project and is not affiliated with Hanki Games, MelonLoader, or MoonSharp.
