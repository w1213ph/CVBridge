param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot "src\CVBridge\CVBridge.cs"
$dist = Join-Path $repoRoot "dist"
$out = Join-Path $dist "CVBridge.exe"

if (-not (Test-Path -LiteralPath $source)) {
    throw "Source file not found: $source"
}

$candidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw "csc.exe was not found. Install .NET Framework 4.x Developer Pack or build on Windows with .NET Framework."
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

& $csc /nologo /target:winexe /platform:anycpu /codepage:65001 `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /out:$out $source

Copy-Item -LiteralPath (Join-Path $repoRoot "CVBridge.example.ini") -Destination (Join-Path $dist "CVBridge.example.ini") -Force

Write-Host "Built: $out"

