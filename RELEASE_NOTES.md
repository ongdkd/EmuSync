# EmuSync 1.0.0 — first preview

A simple PPSSPP library and save-sync launcher for Windows and Android. Your PC hosts the library on the same Wi-Fi; no cloud account is required.

## Downloads

- **Windows:** download and run `EmuSync-Setup-1.0.0-x64.exe`. Requires 64-bit Windows 10 version 1809 or later, or Windows 11. Installs for your account and adds a Start menu shortcut. Uninstall preserves game/save folders and app settings/backups.
- **Android:** download and install `EmuSync-Android.apk`. Requires an ARM64 device with Android 8 or newer. Allow installation from your browser when prompted.
- `SHA256SUMS.txt` contains checksums for both downloads.

Install PPSSPP separately. No games or saves are included. See the [README](https://github.com/ongdkd/EmuSync#one-time-setup) for folder selection and pairing.

## What works

- Read game titles, thumbnails, and backgrounds from uncompressed PSP ISO files.
- Select a game and press Play to check saves, sync, and launch PPSSPP.
- Download missing games from the PC to Android, with hash verification.
- Ask before resolving conflicting or suspiciously missing/empty saves; back up saves before replacement.
- After playing on Android, exit the game, return to EmuSync, and confirm **Game is closed · Sync** before continuing on PC.

## Preview limits and validation

The Windows installer is unsigned, so Windows may warn. The Android APK uses a development signing key and is intended for evaluation. CSO/CHD, save states, internet/cloud sync, and automatic updates are not included.

The 39 automated app checks passed, covering ISO parsing, save decisions, backup/recovery, TLS pinning, authentication, stale uploads, and verified transfers. Responsive UI and native Windows startup checks passed. A clean installer test verified all 955 installed files, Start menu/uninstall entries, and uninstall cleanup.

Physical Android permissions and real PPSSPP launches with user games/saves have not been tested. Keep external backups while trying this preview.
