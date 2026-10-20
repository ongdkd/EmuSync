# EmuSync

A small Windows + Android launcher for PPSSPP. Choose a game and press **Play**: check and sync the save, ask if progress conflicts, then open PPSSPP.

This is an independent implementation inspired by [emu-sync/EmuSync](https://github.com/emu-sync/EmuSync). The published 1.0.2 build uses your PC as the shared library on local Wi-Fi. The 1.1.0 source adds optional Google Drive sync; it needs Google app registration before sign-in is available.

## Try the builds

[Download Android 1.0.2](https://github.com/ongdkd/EmuSync/releases/tag/v1.0.2-preview.1). It retains the startup fix and latest app changes, restores the original signing certificate, and increases the version code. Install over the original app without uninstalling or clearing data. The APK from `v1.0-preview.1` used a different key and cannot be updated in place with the original key; preserve its private data before considering removal. [Windows downloads](https://github.com/ongdkd/EmuSync/releases/tag/v1.0-preview.1) are unchanged by this Android signing fix. This is a preview build; physical Android testing is still pending. No games, saves, or PPSSPP binaries are included.

- **Windows:** run `dist/EmuSync-Setup-1.0.0-x64.exe`. It installs for your Windows account, adds a Start menu shortcut, and includes an uninstaller. The installer is unsigned, so Windows may display a warning. The portable build at `dist/Windows/EmuSync.App.exe` also works; keep that entire folder together.
- **Android:** install `dist/EmuSync-Android.apk` on a 64-bit ARM device running Android 8 or newer. This is a development-signed sideload build, not a Play Store release.

PPSSPP must be installed separately on each device.

Windows uninstall removes the installed app only. Your selected games/save folders and private app settings/backups are preserved. Close EmuSync and finish any game session before installing an update or uninstalling.

## One-time setup

For the Google Drive preview, complete the [Google registration and build setup](docs/google-drive-setup.md) first. It supports save sync plus optional ISO uploads/downloads, with no PC required to stay on. Live Google sign-in and real-device cloud transfers have not yet been tested. The Wi-Fi setup below continues to work.

On **Windows**, open Settings and choose:

1. A folder containing your uncompressed PSP `.iso` files.
2. PPSSPP's **PSP** folder, containing `SAVEDATA`. Find it in PPSSPP's Settings → System → Memory Stick folder. Select the `PSP` directory, not its parent and not `SAVEDATA` itself.
3. Your PPSSPP `.exe`. A nearby Memory Stick folder is suggested when it can be detected.

Then click **Copy pairing code**. This is your main PC. To use a second PC, open Settings → Connect to another PC there and paste the same code. Transfer that code privately to your Android device. If Windows Firewall asks, allow the app on your private/home network. Keep the PC app open; it listens on HTTPS port 42387. Nothing needs to be exposed to the internet.

On **Android**, open Settings:

1. Choose a local games folder. Downloaded games will be saved here.
2. Choose the same shared **PSP** folder configured inside Android PPSSPP. Avoid `Android/data`; use a folder PPSSPP and EmuSync can both access.
3. Click **Detect PPSSPP**. Standard PPSSPP and PPSSPP Gold are recognized.
4. Paste the PC pairing code and click **Connect**.

Game names, IDs, thumbnails (`ICON0.PNG`), and available backgrounds (`PIC1.PNG`) are read directly from ISO metadata. Missing artwork uses a text fallback. No artwork service is contacted.

## Playing and syncing

- Opening the app or selecting a game does **not** sync saves.
- Pressing **Play** on Android downloads the game if needed, verifies it, and checks saves against the PC and the last successful sync.
- Changes made on only one device transfer automatically. Different progress on both devices asks which copy to keep. Missing or suspiciously empty save files pause the operation.
- Launch waits until sync succeeds or you explicitly choose to play offline. An offline launch keeps progress locally.
- On Windows, saves are already in the shared PC folder. The launcher backs them up, launches PPSSPP, and keeps them available for Android after exit.
- On Android, save and exit the game, then return to EmuSync. Confirm **Game is closed · Sync** to upload your progress. Returning to the launcher while PPSSPP is still playing must use **Still playing**.
- Finish syncing on Android before continuing on the PC. Avoid playing the same game simultaneously on multiple devices; conflicts preserve backups but cannot merge game progress.

For a game not installed on Android, the per-game checkbox controls automatic game downloads. If disabled, Play asks before downloading. Games transfer from PC to Android; save progress transfers both ways.

## Save protection and pairing

Transfers use HTTPS pinned to the certificate included in the pairing code, plus a random authentication key. Do not share that code publicly: it grants access to the library and saves. PC and Android must be on the same local network; guest-network isolation can prevent connecting.

Save comparisons use file contents, not device clocks. Uploads check the expected PC save hash before replacing anything. Backups are created before replacements; an interrupted apply leaves a recovery record and blocks another launch until recovery is confirmed.

Backups are kept under the app's data directory in `backups`; Settings → Library details shows the exact location. They are not automatically deleted. Keep external backups of important progress while evaluating this first build. Before uninstalling the Android app, copy out private backups if you want to retain them.

If the PC's address changes, copy a fresh pairing code. To revoke existing pairing, close the PC app and remove `pairing.key` from the app data directory shown in Settings. Restart and pair again. Your saves are unaffected.

## Current limits

- Uncompressed `.iso` games and regular `SAVEDATA` only. CSO/CHD and save states are not implemented. Google Drive requires the preview setup above; other cloud providers are not implemented.
- Game discovery scans up to four subfolders deep and 1,000 game IDs; duplicate IDs use the first copy found.
- Saves are limited to 128 MB per game and 10,000 files. Symbolic links and paths that cannot transfer safely to Windows are rejected.
- Android must use local folder storage that supports seeking. Some cloud document providers cannot expose ISO files this way.
- Windows x64 and Android ARM64 packages are supplied. There is no auto-updater yet.

## Build and verify

Requires .NET SDK 10.0.300 (or a compatible patch), the `maui-windows` and `maui-android` workloads, and the Android SDK/JDK. An isolated toolchain was installed in `.tools/` on the development machine; it is not part of the source or distributable.

```powershell
.\build.ps1 -Target All
.\build.ps1 -Target Windows
.\build.ps1 -Target Android
.\build.ps1 -Target Checks
.\build.ps1 -Target Installer
```

`build.ps1` prefers the isolated toolchain when available. `Directory.Build.props` locates the project-local Android SDK and JDK. Otherwise install the workloads and dependencies using Microsoft's MAUI installation instructions.

Android release builds require the original private signing keystore at `.tools/signing/emusync.keystore` (the original development key's `androiddebugkey` alias and development passwords). Back it up privately; never commit or upload it. A fresh machine must restore that key rather than generate another. `build.ps1` explicitly selects it and runs `tests/android-package-check.ps1` before copying the APK into `dist`; the check pins the original public certificate, verifies the package/version, and checks 16 KB alignment. `release.ps1` uses the Android display version for the release tag.

The `Installer` target packages the existing `dist/Windows` build using Inno Setup 7 (installed normally or under `.tools/inno`). Publish Windows again before packaging changes. `tests/installer-check.ps1` verifies a silent installation, installed file hashes, Start menu/uninstall entries, and removal in an isolated test directory; it stops if this Windows account already has EmuSync installed.

The assert-based checks in `tests/EmuSync.Checks` exercise synthetic ISO parsing, sync decisions, malicious paths, backup/rollback, interrupted recovery, certificate pinning, invalid pairing, stale uploads, running-game protection, and verified transfers. They do not read any real game or save files.

`tests/ui-check.cjs` checks the actual shipped web interface with a native-call fixture at desktop and mobile widths. It requires Playwright and Edge; set `PLAYWRIGHT_MODULE` if the package is elsewhere. `tests/native-windows-check.cjs` verifies the published native Windows bridge using a deliberately enabled local WebView debugging port; that port is not enabled by normal launches.

Physical Android folder permissions and real PPSSPP launches still require device testing. The builds have not been tested with your own games or saves.

The manual **Android startup check** GitHub Actions workflow builds the selected Git tag or branch for the emulator's native x64 CPU, then checks launch, the real JavaScript-to-.NET setup call through persisted app-private settings, and cold relaunch on Android 11 and Android 16. Screenshots and crash logs are saved for review. ARM APK signing identity, version, and native callback registration are checked separately; emulation is not a substitute for testing real phone hardware. The original Android 1.0 build reproduces a missing `MauiApplication.n_onCreate` callback; Android 1.0.1 disables the affected native marshal optimization ([upstream issue](https://github.com/dotnet/maui/issues/35209)). Switching that build setting requires deleting the app's generated Android `bin/Release/net10.0-android` and `obj/Release/net10.0-android` directories before publishing to avoid stale Java wrappers.
