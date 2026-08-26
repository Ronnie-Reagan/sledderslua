[CmdletBinding()]
param([ValidateRange(1, 65535)][int]$Port = 8080)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$python = Get-Command python -ErrorAction SilentlyContinue
$pythonArgs = @()
if (-not $python) {
    $python = Get-Command py -ErrorAction SilentlyContinue
    if ($python) { $pythonArgs = @("-3") }
}
if (-not $python) { throw "Python 3 is required for the local documentation preview." }

& $python.Source @pythonArgs (Join-Path $root "tools/build_site.py") --root $root --output (Join-Path $root "site-dist")
if ($LASTEXITCODE -ne 0) { throw "Site build failed." }

Push-Location (Join-Path $root "site-dist")
try {
    Write-Host "Documentation preview: http://localhost:$Port/"
    & $python.Source @pythonArgs -m http.server $Port
} finally { Pop-Location }
