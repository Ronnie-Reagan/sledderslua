[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionLine = Select-String -Path (Join-Path $root "src/SleddersLuaRuntime/Core/BuildInfo.cs") -Pattern 'RuntimeVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
    if (-not $versionLine) { throw "Could not read runtime version from BuildInfo.cs." }
    $Version = $versionLine.Matches[0].Groups[1].Value
}

if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$') {
    throw "Version contains characters that are unsafe for a release filename: $Version"
}

$buildDir = Join-Path $root "src/SleddersLuaRuntime/bin/$Configuration/net472"
$runtimeDll = Join-Path $buildDir "SleddersLuaRuntime.dll"
$moonSharp = Join-Path $buildDir "MoonSharp.Interpreter.dll"
if (-not (Test-Path $runtimeDll)) { throw "Missing build output: $runtimeDll. Run scripts/build.ps1 first." }
if (-not (Test-Path $moonSharp)) { throw "Missing MoonSharp runtime dependency: $moonSharp." }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stage = Join-Path $OutputDirectory ".stage-$([guid]::NewGuid().ToString('N'))"
$zip = Join-Path $OutputDirectory "SleddersLuaRuntime-$Version.zip"
$checksum = "$zip.sha256"

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $stage "Mods"), (Join-Path $stage "UserLibs"), (Join-Path $stage "LuaMods"), (Join-Path $stage "SleddersLua/Examples"), (Join-Path $stage "SleddersLua/third_party") | Out-Null

    Copy-Item $runtimeDll (Join-Path $stage "Mods/SleddersLuaRuntime.dll")
    Copy-Item $moonSharp (Join-Path $stage "UserLibs/MoonSharp.Interpreter.dll")
    Copy-Item (Join-Path $root "packaging/LuaMods_README.txt") (Join-Path $stage "LuaMods/_SleddersLua.txt")
    Copy-Item (Join-Path $root "packaging/README.md") (Join-Path $stage "SleddersLua/README.md")
    Copy-Item (Join-Path $root "docs/API.api") (Join-Path $stage "SleddersLua/API.api")
    Copy-Item (Join-Path $root "examples/*") (Join-Path $stage "SleddersLua/Examples") -Recurse
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "SleddersLua/LICENSE")
    Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") (Join-Path $stage "SleddersLua/THIRD_PARTY_NOTICES.md")
    Copy-Item (Join-Path $root "third_party/MoonSharp-LICENSE.txt") (Join-Path $stage "SleddersLua/third_party/MoonSharp-LICENSE.txt")

    $allowedDlls = @("SleddersLuaRuntime.dll", "MoonSharp.Interpreter.dll")
    $unexpectedDlls = Get-ChildItem $stage -Recurse -Filter "*.dll" | Where-Object { $allowedDlls -notcontains $_.Name }
    if ($unexpectedDlls) {
        throw "Packaging guard: unexpected DLL(s) would be redistributed: $($unexpectedDlls.Name -join ', ')"
    }

    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -Path $checksum -Encoding ascii
    Write-Host $zip
    Write-Host $checksum
} finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
}
