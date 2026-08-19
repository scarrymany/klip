# Klip

Personal text and image clipboard history for Windows 10/11. Captures `Win + Shift + S` screenshots, stores PNG files locally, and restores them to the clipboard with one click.

See the [Russian README](README.md) for full docs, badges and installers.

| Asset | Role |
| --- | --- |
| `Klip-Setup-1.2.3.exe` | Inno Setup installer |
| `Klip-1.2.3-win-x64.msi` | Per-machine MSI |
| `Klip-Portable-win-x64.zip` | No-install zip |

Hotkey: `Ctrl + Shift + V`. The window is layered WPF (not DWM Mica). SQLite metadata and PNG images stay in `%APPDATA%\Klip`. The only network use is checking and downloading GitHub Releases.
