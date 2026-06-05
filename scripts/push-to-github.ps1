param(
    [Parameter(Mandatory = $true)]
    [string]$RepoUrl,

    [string]$CommitMessage = "Initial open-source release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git was not found. Install Git for Windows first."
}

Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath ".git")) {
        git init
    }

    $name = git config user.name
    $email = git config user.email
    if (-not $name -or -not $email) {
        throw "Git user.name or user.email is not configured. Run: git config --global user.name `"Your Name`"; git config --global user.email `"you@example.com`""
    }

    git add .

    $hasHead = $true
    git rev-parse --verify HEAD *> $null
    if ($LASTEXITCODE -ne 0) {
        $hasHead = $false
    }

    if ($hasHead) {
        $changes = git status --porcelain
        if ($changes) {
            git commit -m $CommitMessage
        } else {
            Write-Host "No changes to commit."
        }
    } else {
        git commit -m $CommitMessage
    }

    git branch -M main

    $origin = git remote get-url origin 2>$null
    if ($LASTEXITCODE -eq 0 -and $origin) {
        git remote set-url origin $RepoUrl
    } else {
        git remote add origin $RepoUrl
    }

    git push -u origin main
}
finally {
    Pop-Location
}
