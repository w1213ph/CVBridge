# Publishing

This checklist is for the project owner before making the repository public.

## Before Push

Run these checks from the repository root:

```powershell
.\scripts\package.ps1
rg -n "password|private key|BEGIN OPENSSH|your_real_host|your_real_user" .
```

Make sure these files are not committed:

- `CVBridge.ini`
- `CVBridge.log`
- `CVBridge.log.old`
- private keys
- VPN profiles
- real server addresses if they are sensitive

## First Push

Create an empty GitHub repository, then run:

```powershell
git init
git add .
git commit -m "Initial open-source release"
git branch -M main
git remote add origin https://github.com/YOUR_NAME/CVBridge.git
git push -u origin main
```

Or use the helper:

```powershell
.\scripts\push-to-github.ps1 -RepoUrl https://github.com/YOUR_NAME/CVBridge.git
```

## Release

Create a GitHub Release and upload:

```text
CVBridge-release.zip
```

GitHub Actions also builds this zip on push and pull request.
