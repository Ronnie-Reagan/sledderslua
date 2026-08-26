[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/SleddersLuaRuntime/SleddersLuaRuntime.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is required to build this repository. Install .NET 8 SDK or newer; this script does not download toolchains."
}

Push-Location $root
try {
    if (-not $NoRestore) {
        & dotnet restore $project --nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
    }

    & dotnet build $project -c $Configuration --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }
} finally {
    Pop-Location
}
