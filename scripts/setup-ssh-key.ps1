param(
    [Parameter(Mandatory = $true)]
    [string]$HostName,

    [Parameter(Mandatory = $true)]
    [string]$UserName,

    [int]$Port = 22,

    [string]$KeyPath = "$env:USERPROFILE\.ssh\cvb_ed25519"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    throw "ssh was not found. Install Windows OpenSSH Client."
}
if (-not (Get-Command ssh-keygen -ErrorAction SilentlyContinue)) {
    throw "ssh-keygen was not found. Install Windows OpenSSH Client."
}

$sshDir = Split-Path -Parent $KeyPath
if (-not (Test-Path -LiteralPath $sshDir)) {
    New-Item -ItemType Directory -Force -Path $sshDir | Out-Null
}

if (-not (Test-Path -LiteralPath $KeyPath)) {
    ssh-keygen -t ed25519 -f $KeyPath -N '""' -C "cvbridge" | Out-Null
}

icacls $KeyPath /inheritance:r | Out-Null
icacls $KeyPath /grant:r "$env:USERDOMAIN\$env:USERNAME:R" | Out-Null

$id = [Guid]::NewGuid().ToString("N")
$remotePub = "/tmp/cvbridge_pubkey_$id.pub"
$remoteScript = "/tmp/cvbridge_setup_key_$id.sh"
$localScript = Join-Path ([IO.Path]::GetTempPath()) "cvbridge_setup_key_$id.sh"

$script = @"
#!/usr/bin/env bash
set -e

pub="$remotePub"
auth="`$HOME/.ssh/authorized_keys"

mkdir -p "`$HOME/.ssh"
chmod 700 "`$HOME/.ssh"
touch "`$auth"
chmod 600 "`$auth"

if ! grep -qxF -f "`$pub" "`$auth" 2>/dev/null; then
    cat "`$pub" >> "`$auth"
fi

rm -f "`$pub" "$remoteScript"
"@

Set-Content -LiteralPath $localScript -Value $script -Encoding UTF8

Write-Host "Uploading public key. If prompted, enter the Linux password for $UserName."
scp -P $Port "$KeyPath.pub" "$UserName@${HostName}:$remotePub"
scp -P $Port $localScript "$UserName@${HostName}:$remoteScript"
Remove-Item -LiteralPath $localScript -Force
ssh -p $Port "$UserName@$HostName" "bash $remoteScript"

Write-Host "Testing key login..."
ssh -p $Port -i $KeyPath -o IdentitiesOnly=yes "$UserName@$HostName" "whoami; hostname -I"

Write-Host ""
Write-Host "Key ready:"
Write-Host $KeyPath
