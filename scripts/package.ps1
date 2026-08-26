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

function Assert-ArchiveMatchesStage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StageDirectory,
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $expected = @{}
    foreach ($file in Get-ChildItem -LiteralPath $StageDirectory -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($StageDirectory, $file.FullName).Replace('\', '/')
        $expected[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $actual = @{}
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }

            $relative = $entry.FullName.Replace('\', '/')
            if ($actual.ContainsKey($relative)) {
                throw "Packaging guard: duplicate archive entry '$relative'."
            }
            $actual[$relative] = $entry
        }

        $missing = @($expected.Keys | Where-Object { -not $actual.ContainsKey($_) } | Sort-Object)
        $unexpected = @($actual.Keys | Where-Object { -not $expected.ContainsKey($_) } | Sort-Object)
        if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
            $parts = @()
            if ($missing.Count -gt 0) { $parts += "missing: $($missing -join ', ')" }
            if ($unexpected.Count -gt 0) { $parts += "unexpected: $($unexpected -join ', ')" }
            throw "Packaging guard: archive file list does not match the staged release tree ($($parts -join '; '))."
        }

        foreach ($relative in $expected.Keys) {
            $entry = $actual[$relative]
            $stream = $entry.Open()
            $sha256 = [Security.Cryptography.SHA256]::Create()
            try {
                $entryHash = [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
            } finally {
                $sha256.Dispose()
                $stream.Dispose()
            }

            if ($entryHash -ne $expected[$relative]) {
                throw "Packaging guard: archive content differs from staged file '$relative'."
            }
        }
    } finally {
        $archive.Dispose()
    }

    Write-Host "Verified release archive: $($expected.Count) files match the staged release tree."
}

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
    Assert-ArchiveMatchesStage -StageDirectory $stage -ArchivePath $zip

    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -Path $checksum -Encoding ascii
    Write-Host $zip
    Write-Host $checksum
} finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
}
