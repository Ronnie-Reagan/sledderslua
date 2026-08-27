# Development

This is maintainer documentation. Players installing the runtime or writing Lua mods should use the main README and the website instead.

## Build

Requirements:

- Windows
- .NET 8 SDK
- PowerShell 7

```powershell
pwsh ./scripts/build.ps1
```

The project targets .NET Framework 4.7.2 because that is the runtime used by the current Sledders/MelonLoader setup. Game and Unity assemblies are not copied into this repository; game access is resolved at runtime through the compatibility layer.

To install a local build into Sledders:

```powershell
pwsh ./scripts/install-dev.ps1 -GameDir "C:\Path\To\Sledders"
```

## Package

```powershell
pwsh ./scripts/package.ps1
```

## Audit a Sledders update

The repository does not store Sledders assemblies. To verify a locally owned/current `Assembly-CSharp.dll` against the binding contract:

```powershell
dotnet run --project tools/SleddersAssemblyAudit/SleddersAssemblyAudit.csproj -- `
  "C:\Path\To\Sledders_Data\Managed\Assembly-CSharp.dll" `
  tests/bindings/current.json
```

The command reports the assembly SHA-256, module MVID, metadata counts, and every required type/field/method contract check. A missing member or method parameter-count mismatch exits non-zero. This is a compatibility gate, not a substitute for in-game smoke tests: metadata cannot prove runtime object ownership, units, or behavior.


The release ZIP contains the runtime DLL, MoonSharp, the Lua API reference, examples, and license notices. It does not contain MelonLoader or Sledders files.

## API changes

Keep common operations on the object a Lua author already has. Prefer Sledders concepts over Unity internals: litres instead of a normalized fuel field, `sled.setHeadlights()` instead of a reflected light component, and local sled motion for `getVel`/`setVel`/`addVel`.

Use `sledders.dev` to investigate a system, then add a normal binding only after its behavior has been tested in game. Do not publish API entries that only work in theory or routinely return `nil` on the current game build.

Stable bindings should prefer deterministic entry points from the current Sledders assembly, resolved through `SleddersBindingResolver`. Broad object/type discovery belongs to compatibility fallback or `sledders.dev`, not the primary stable path. The current local-sled path is `Controller.Instance.SnowmobileController`; the current local-player path starts at `NetClient.Instance.LocalPlayer`.

## Versioned releases

The source version must match the release tag.

Set the version:

```powershell
python tools/version.py set 0.4.0
```

Commit that change, then tag the same commit:

```powershell
git tag v0.4.0
git push origin v0.4.0
```

`release.yml` builds, checks the Lua examples, packages the runtime, and creates the GitHub release. Tags with a suffix such as `v0.4.0-rc.1` are prereleases.

After a stable release, set the next development version, for example:

```powershell
python tools/version.py set 0.5.0-dev
```

## Release gate

Before cutting an RC or stable tag:

1. Require green CI on the exact commit being released. The package dry run must complete; scripts/package.ps1 verifies that the ZIP file list and every archived file hash match the staged release tree.
2. Run tests/manual/RuntimeSmokeTest.lua on the current Sledders build and require a fully passing Ctrl+Shift+5 safe API sweep.
3. Run the permissioned tests/manual/DevSmokeTest folder test and require the read-only Ctrl+Shift+9 reflection sweep to pass.
4. Only then set the release version, commit it, and create an RC or stable tag from that tested commit.

A green build is not a substitute for the two in-game smoke tests; runtime reflection bindings can only be validated against the current game assemblies and live objects.

## Nightly

`nightly.yml` runs daily and can also be started manually. When `main` has changed since the last nightly it creates a dated prerelease such as `nightly-20260826-1015-12abc34`. Previous nightlies are left intact.

## Documentation site

The Pages workflow builds `site/pages` with `tools/build_site.py`. Preview it locally with:

```powershell
pwsh ./scripts/serve-docs.ps1
```

GitHub Pages must be enabled once per repository under **Settings > Pages > Source: GitHub Actions**. Until then, the Pages workflow still builds the site but skips deployment instead of failing.

For the public repository named `sledderslua`, the project site can live at `https://donreagan.ca/sledderslua/` when `donreagan.ca` is already the custom domain of the owning GitHub Pages user/organization site. The project itself does not need a `CNAME` file in that setup.
