# EmuSync 1.4.0 — Add games and return to your library

Add your own PSP ISO directly from the library, get PPSSPP from Settings, and return to a simpler phone layout after playing. The 1 GB save limit, Google Drive support, and EmuDeck save-folder fixes are retained.

## What's new

- **Add game:** select a local uncompressed `.iso`. If needed, choose a games folder. EmuSync stages the file in its cache, reads its title and artwork, copies it to a new filename, verifies the SHA-256 hash and game ID, then adds it to the library. Your source ISO and existing games are not replaced. Selecting a game already in the library selects its existing copy. Adding a game never launches PPSSPP or syncs saves.
- **Get PPSSPP:** Settings opens the [official Windows download page](https://www.ppsspp.org/download/) or [Android Play Store listing](https://play.google.com/store/apps/details?id=org.ppsspp.ppsspp). Install it, then choose/detect PPSSPP in Settings. Emulator binaries are not bundled or rehosted.
- **Windows return:** after the launched PPSSPP process exits, EmuSync restores and activates its window before backing up and syncing the save. A session started offline retries its configured sync connection on exit.
- **Android return:** launches include PPSSPP's documented `--pause-menu-exit` option. After saving, exit from PPSSPP's pause menu and confirm **Game is closed · Sync** in EmuSync. An active session remains available through **Finished playing · Sync save** if a transfer fails, or after EmuSync restarts. Google reconnect stays available for a pending session.
- **Phone navigation:** the top bar stays visible while scrolling, with a Back button on game details. Android Back closes Settings, returns to the library while preserving its scroll position, clears search, then backgrounds the app at the library root. Settings keeps its heading and Done button visible. Play remains pinned near the bottom.

Android does not provide a reliable way to confirm that another app's game has stopped. Returning via Home/Recents is not treated as proof; choose **Still playing** if PPSSPP remains active. PPSSPP versions and device task handling can affect returning to the launcher. EmuSync must remain available to finish syncing; force-stopping apps cannot guarantee an automatic return or background sync. See [PPSSPP's integration guide](https://dev.ppsspp.org/docs/reference/front-end-integration/).

## Install or update

- **Windows:** close EmuSync and run `EmuSync-Setup-1.4.0-x64.exe` over the current installation. Settings, games, saves, and backups are preserved.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **12**. Do not uninstall or clear app data.
- The alternate-key APK from `v1.0-preview.1` cannot be updated in place. Preserve its private data before considering removal.
- `SHA256SUMS.txt` contains checksums for both downloads. Windows remains unsigned; Android uses the original development key.

## Validation and limits

86 standard checks, 87 checks with Windows RedirectionGuard enforced, and the Windows authentication regression passed. Import checks verify ISO metadata, identical copied bytes and SHA-256, rejection of invalid ISO data, and cleanup after rejection. Browser regression checks cover Add game/Get PPSSPP commands, session retry controls, fixed headers, Back navigation, search, modal focus, safe artwork/titles, and layouts from 320 to 1440 pixels. Browser tests use a simulated native bridge.

Windows and Android Release builds completed. The installer reports 1.4.0. The Android package passed original-certificate, package-identity, version-code 12, and 16 KB alignment checks. Both packages' UI assets match the browser-tested source, and both contain the configured Google OAuth asset. Installation over a live user installation was not tested.

Physical S24 Ultra behavior, actual PPSSPP exit/foreground behavior on a user's device, and live Google account transfers have not been independently verified. The original 1 GB save-limit checks include a real 129 MB synthetic save; a full 1 GB phone transfer remains untested. No user games or saves were used in these tests.

Imports need temporary app-cache space plus destination storage, and support uncompressed PSP ISO files up to 8 GB. Saves remain limited to 1 GB per game and 10,000 files; larger save bundles use more RAM and backup storage. Save states, CSO/CHD support, and automatic app updates are not included.
