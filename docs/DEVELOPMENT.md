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

The release ZIP contains the runtime DLL, MoonSharp, the Lua API reference, examples, and license notices. It does not contain MelonLoader or Sledders files.

## API changes

Keep common operations on the object a Lua author already has. Prefer Sledders concepts over Unity internals: litres instead of a normalized fuel field, `sled.setHeadlights()` instead of a reflected light component, and local sled motion for `getVel`/`setVel`/`addVel`.

Use `sledders.dev` to investigate a system, then add a normal binding only after its behavior has been tested in game. Do not publish API entries that only work in theory or routinely return `nil` on the current game build.

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

## Nightly

`nightly.yml` runs daily and can also be started manually. When `main` has changed since the last nightly it creates a dated prerelease such as `nightly-20260826-1015-12abc34`. Previous nightlies are left intact.

## Documentation site

The Pages workflow builds `site/pages` with `tools/build_site.py`. Preview it locally with:

```powershell
pwsh ./scripts/serve-docs.ps1
```

GitHub Pages must be enabled once per repository under **Settings > Pages > Source: GitHub Actions**. Until then, the Pages workflow still builds the site but skips deployment instead of failing.

For the public repository named `sledderslua`, the project site can live at `https://donreagan.ca/sledderslua/` when `donreagan.ca` is already the custom domain of the owning GitHub Pages user/organization site. The project itself does not need a `CNAME` file in that setup.
