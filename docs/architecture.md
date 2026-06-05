# Architecture

CV Bridge is a Windows WinForms application that talks to a remote Linux desktop over SSH.

## Data Flow

```text
Windows UI
  -> ssh/scp
  -> remote ~/.server_clipboard_bridge/clipboard.txt
  -> optional X11 clipboard helper
  -> remote desktop Ctrl+V
```

## Remote Clipboard Strategy

CV Bridge always writes text to:

```text
~/.server_clipboard_bridge/clipboard.txt
```

Then it tries to update the graphical clipboard in this order:

1. `xclip`
2. `xsel`
3. Python `ctypes` helper using `libX11`

The helper is created under:

```text
~/.server_clipboard_bridge/cvbridge_x11_clipboard.py
```

It is a user-mode process. It does not need root privileges.

## Why SSH

SSH is already available in many remote development environments, avoids opening a custom server, and gives us file transfer via `scp`.

