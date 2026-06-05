# Troubleshooting

## SSH Test

Run this from Windows PowerShell:

```powershell
ssh -i C:\Users\YOU\.ssh\id_ed25519 -o IdentitiesOnly=yes user@host
```

If this fails, fix SSH before debugging CV Bridge.

## Private Key Permissions

Windows OpenSSH may reject keys with:

```text
WARNING: UNPROTECTED PRIVATE KEY FILE
```

Fix it:

```powershell
icacls C:\Users\YOU\.ssh\id_ed25519 /inheritance:r
icacls C:\Users\YOU\.ssh\id_ed25519 /grant:r "$env:USERDOMAIN\$env:USERNAME:R"
```

## Remote Ctrl+V Does Not Work

Check the helper log on Linux:

```bash
cat ~/.server_clipboard_bridge/cvbridge_x11_clipboard.log
```

If the helper cannot access X11, CV Bridge still writes:

```bash
cat ~/.server_clipboard_bridge/clipboard.txt
```

## Logs

The app writes logs next to the executable:

```text
CVBridge.log
CVBridge.log.old
```

