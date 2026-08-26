# Manual runtime test

Copy `RuntimeSmokeTest.lua` into `Sledders/LuaMods` and run it on the current game build.

Check startup, hot reload, input chords, local velocity, teleport, drawing, and the printed sled state. `L` and `Ctrl+L` should trigger separately.

For callback fault isolation, temporarily add `error("smoke test")` inside `onDraw()`. The error should be logged once and only `onDraw` should remain suspended until the next successful reload.

For syntax-error hot reload, introduce a Lua syntax error and save. The last working script should stay loaded. Fix the syntax and save again.

Use the MelonLoader log when reporting a failed binding.
