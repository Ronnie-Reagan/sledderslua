[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$game = (Resolve-Path $GameDir).Path
if (-not (Test-Path (Join-Path $game "Sledders.exe"))) {
    throw "GameDir does not look like a Sledders installation: $game"
}

$buildDir = Join-Path $root "src/SleddersLuaRuntime/bin/$Configuration/net472"
$runtimeDll = Join-Path $buildDir "SleddersLuaRuntime.dll"
$moonSharp = Join-Path $buildDir "MoonSharp.Interpreter.dll"
if (-not (Test-Path $runtimeDll) -or -not (Test-Path $moonSharp)) {
    throw "Build output is missing. Run scripts/build.ps1 first."
}

New-Item -ItemType Directory -Force -Path (Join-Path $game "Mods"), (Join-Path $game "UserLibs"), (Join-Path $game "LuaMods") | Out-Null
Copy-Item $runtimeDll (Join-Path $game "Mods/SleddersLuaRuntime.dll") -Force
Copy-Item $moonSharp (Join-Path $game "UserLibs/MoonSharp.Interpreter.dll") -Force
Write-Host "Installed development build into $game"
