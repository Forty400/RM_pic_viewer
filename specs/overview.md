# Overview

## Product

Windows-only reader for RPG Maker MV/MZ encrypted images (`.rpgmvp`, `.png_`). Provides Explorer thumbnails and a simple WPF image viewer with keyboard navigation.

## In scope (current release)

- Decrypt MV/MZ encrypted PNG images using `encryptionKey` from `System.json`
- Windows Explorer large / extra-large icon thumbnails via `IThumbnailProvider`
- WPF viewer: open one file, Left/Right cycle sibling images in the same folder
- Plain `.png` openable in the viewer without decryption
- Installer with **custom install path** + configuration options
- In-app settings + uninstall (see [installer.md](installer.md))
- System tray host with exit to free memory

## Out of scope (current release)

- Export decrypted PNG / batch export UI
- Audio formats (`.rpgmvo`, `.ogg_`, etc.)
- Non-Windows platforms
- Image editing

## Deferred (spec + interface only)

- Export service: see [export-deferred.md](export-deferred.md) (`status: deferred`)

## Stack

- .NET 8 (Windows), self-contained win-x64 publish for end users
- Shared decrypt class library (`RpgMaker.Decrypt`)
- Shared install/uninstall helpers (`RpgMaker.Install`)
- WPF viewer EXE (+ tray; `Uninstall.exe` hard-links the same binary)
- WPF Setup EXE with embedded payload (repo ships compressed `.7z`)
- COM Shell thumbnail provider DLL

