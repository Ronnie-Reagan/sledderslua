# Development

This is maintainer documentation. Players installing the runtime or writing Lua mods should use the main README and website instead.

## Design target

Sledders Lua should be able to act as the default general-purpose modding framework for Sledders:

- trivial mods should fit in one readable Lua file;
- normal mods should use semantic Sledders APIs rather than reflection;
- writable state should normally have a matching setter/mutator;
- vehicle definition, live physics and derived telemetry must not be conflated;
- advanced tuning/graphics/HUD/camera/audio/world mods must have supported stable surfaces;
- `sledders.dev` is the version-sensitive escape hatch, not the normal development path.

Promote a binding to the stable API only when its current-build anchor and semantics are understood. Keep obfuscated/uncertain behavior in developer discovery until validated.

## Build

Requirements:

- Windows
- .NET 8 SDK
- PowerShell 7

```powershell
pwsh ./scripts/build.ps1
```

The runtime targets .NET Framework 4.7.2 for the current Sledders/MelonLoader environment. Game and Unity assemblies are not compiled into or copied into the project; runtime access stays reflection/metadata driven.

Install a local build:

```powershell
pwsh ./scripts/install-dev.ps1 -GameDir "C:\Path\To\Sledders"
```

## Package

```powershell
pwsh ./scripts/package.ps1
```

The release ZIP contains the runtime DLL, MoonSharp, API reference, examples and license notices. It must not contain MelonLoader or Sledders game assemblies.

## Audit a Sledders update

The repository contains a metadata contract for the currently audited game assembly, but not the DLL itself.

```powershell
python tools/audit_assembly.py `
  "C:\Path\To\Sledders_Data\Managed\Assembly-CSharp.dll" `
  tests/bindings/current.json
```

The contract verifies current exact fields and method signatures used by stable bindings. A green metadata audit proves shape compatibility only; it does not prove units, ownership or live behavior.

When Sledders updates:

1. run the contract audit against the new DLL;
2. investigate every failure before changing the contract;
3. update exact bindings/fallbacks deliberately;
4. run both manual in-game smoke tests;
5. only then bless the new contract/fingerprint.

## API layering

Preferred layers:

1. **Convenience semantic API** — common sled/player/world operations.
2. **Stable subsystem wrappers** — body, vehicle, tuning, structure, renderers/materials, HUD, camera, audio, scene, physics, assets.
3. **Semantic property bags** — enumerable named advanced values without exposing obfuscated game names to Lua.
4. **Developer reflection** — investigation and genuinely unusual/version-sensitive work.

Avoid adding a 150-method god object when a subsystem object is clearer.

## Setter rule

For each getter, classify the value as:

- writable state — provide a setter/mutator if the current build has a reliable path;
- derived telemetry — read-only;
- action/transition — expose a verb rather than pretending it is a property.

Examples:

- `vehicle.getWeight/setWeight` = vehicle-definition value;
- `body.getMass/setMass` = live Rigidbody mass;
- `sled.getSpeed` = derived read-only telemetry;
- `sled.teleport` = action.

## Runtime safety rules

Stable APIs should:

- reject NaN/infinity before values reach Unity/game state;
- return `nil` for unavailable read values where appropriate;
- return `false` for unsupported/failed mutations;
- use handle-safe wrappers for scene objects;
- clean runtime-created objects/resources on unload/scene change;
- sandbox mod file access under the mod directory;
- avoid holding proprietary game assemblies in the repo.

## Hot reload

The last working VM remains active while a candidate is syntax checked and top-level executed. Content fingerprints are used instead of timestamp-only change detection.

Storage is flushed before preparation and the old VM's final `onUnload` storage snapshot is merged into the candidate. Keys changed by the candidate win. Temporary file I/O failures are retried.

## Developer API

Developer reflection requires:

- `permissions = { "dev" }` in the mod manifest; and
- `EnableDevApi = true` in `UserData/SleddersLua/config.json`.

Do not weaken this gate merely to make a stable feature easier to implement. Promote a semantic wrapper instead.

## Release gate

Before an RC/stable tag:

1. Green CI on the exact source commit.
2. Runtime build has zero errors; investigate warnings rather than normalizing them.
3. Lua syntax/examples validation passes.
4. Docs/site build passes.
5. Package dry run verifies the archive file list and hashes.
6. `tools/audit_assembly.py` passes against the current locally owned Sledders `Assembly-CSharp.dll`.
7. `tests/manual/RuntimeSmokeTest.lua` passes its safe API sweep in game.
8. Permissioned `tests/manual/DevSmokeTest` passes against the same game build.
9. Exercise at least one representative mod from each major framework category touched by the release.

Metadata and CI cannot replace live-game tests.

## Versioning

Current development line:

- runtime `0.5.0-dev`
- API `3.2`

Set a release version with:

```powershell
python tools/version.py set 0.5.0
```

Tag the exact tested commit, for example `v0.5.0` or `v0.5.0-rc.1`.

## Documentation site

Preview locally:

```powershell
pwsh ./scripts/serve-docs.ps1
```

Or build directly:

```text
python tools/build_site.py --root . --output site-dist --repository Ronnie-Reagan/sledderslua
```
