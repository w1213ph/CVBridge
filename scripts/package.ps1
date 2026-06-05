$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repoRoot "dist"
$zip = Join-Path $repoRoot "CVBridge-release.zip"

& (Join-Path $PSScriptRoot "build.ps1")

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $dist "README.md") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $dist "LICENSE") -Force

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Compress-Archive -Path (Join-Path $dist "*") -DestinationPath $zip
Write-Host "Packaged: $zip"

