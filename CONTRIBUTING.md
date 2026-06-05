# Contributing

Thanks for improving CV Bridge.

## Development

Build on Windows:

```powershell
.\scripts\build.ps1
```

Package a release zip:

```powershell
.\scripts\package.ps1
```

## Style

- Keep the app dependency-light.
- Do not require admin privileges on the Linux side.
- Do not log clipboard contents.
- Keep SSH behavior explicit and inspectable.

