# EmuSync 1.3.0 — Launcher-style library, simpler on phones

The library and game page have been redesigned to feel like a desktop game launcher, and the phone layout has been simplified so everything is one thumb away.

- Games are grouped into **Ready to play** (on this device) and **Download to play** (on Google Drive or your PC). The game you opened last leads each group as a wide featured card with a large Play or Download tile.
- Other games use tall box-art covers. Games you still need to download carry a small download badge. Without artwork, a cover shows the full title.
- On desktop, each group is a row you can scroll with arrows. On phones, the library is a plain two-column grid.
- The game page opens with a full-width banner and a Play bar showing save sync, location, and size. Download & Play is blue so it is easy to tell apart from Play.
- On phones, the banner is shorter, game details are three small labels, and the Play button stays pinned at the bottom of the screen.
- Selecting or searching still never starts save sync. Sync starts on Play and after a session ends, as before.

The sync engine, conflict prompts, local backups, Settings, Google Drive, and Wi-Fi pairing are unchanged.

## Update

- **Windows:** close EmuSync, then run `EmuSync-Setup-1.3.0-x64.exe` over the existing installation. Your folders, PPSSPP selection, Google account, and saves are kept.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **10**, with the same package and signing identity. Do not uninstall or clear app data to update.
- `SHA256SUMS.txt` contains checksums for both downloads.

The alternate-key Android APK from `v1.0-preview.1` cannot be updated in place with the original-key APK. Preserve its private data before considering removal.

## Validation and limits

Browser checks cover library/game page navigation, search and empty results, safe text and artwork rendering, selection without sync, Play, Google configuration gating, reset/disconnect, optional ISO upload, settings error visibility, keyboard focus, and layouts at 320, 390, 736, and 1024 pixels. Preview artwork in checks is synthetic and not bundled.

The standard save/transfer checks, the Windows RedirectionGuard checks, and the Windows authentication check passed. The Android package check verifies version 1.3.0/code 10, the original signing identity, and 16 KB alignment.

The new layout has not yet been tried on a physical phone or in a real PPSSPP session. The Windows installer is unsigned; Android uses the original development signing key. No games, saves, or PPSSPP binaries are included.
