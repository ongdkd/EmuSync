# EmuSync 1.2.0 — A simpler, artwork-led library

EmuSync now opens to a cover-card library inspired by a desktop game launcher, with a dedicated game page and a layout built for Windows and Android.

- Browse artwork cards, see which games are on this device, and search by title.
- Select a game to open its banner and a clear Play or Download & Play action. Selection and search never start save sync.
- On phones, a large Play button stays within thumb reach. Back to Library does not trigger the on-screen keyboard.
- Settings opens in a focused panel with folder setup, Google Drive, and Wi-Fi pairing together. Errors and connection progress stay visible in that panel.
- Game options are tucked under the Play area. Names, icons, and banners still come from local ISOs; missing artwork uses a quiet fallback. Cloud-only artwork appears after downloading the game.

The sync engine, conflict prompts, local backups, Google account switching, and EmuDeck/RedirectionGuard fixes are retained. Sync still starts on Play and after a session ends using the existing platform flow.

## Update

- **Windows:** close EmuSync, then run `EmuSync-Setup-1.2.0-x64.exe` over the existing installation. Keep your current folders, PPSSPP selection, Google account, and saves.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **9**, with the same package and signing identity. Do not uninstall or clear app data to update.
- `SHA256SUMS.txt` contains checksums for both downloads.

The alternate-key Android APK from `v1.0-preview.1` cannot be updated in place with the original-key APK. Preserve its private data before considering removal.

## Validation and limits

Browser checks cover gallery/detail navigation, local search and empty results, safe text/artwork rendering, selection without sync, Play, Google configuration gating, reset/disconnect, optional ISO upload, settings error visibility, keyboard focus containment, and responsive layouts at 320, 390, 736, and 1024 pixels. Artwork previews use synthetic sample images and example titles only; they are not bundled as games or game artwork. The app loads actual artwork from your ISOs.

78 standard save/transfer checks, 79 checks with Windows RedirectionGuard enforced, and the Windows authentication regression check passed. Both platform builds and Windows installer compilation passed. Android package checks verified version 1.2.0/code 9, original signing identity, and 16 KB alignment. The HTML, CSS, JavaScript, and native Google registration in both packages match their final source by SHA-256. The backend sync behavior is unchanged by this redesign.

Physical S24 Ultra layout, this redesigned UI in a real PPSSPP session, and real Google Drive transfers still need validation. Native Windows UI and installer install/uninstall checks are not repeated against the user's running installation. Earlier Android emulator startup checks apply to the preceding 1.1.0 source, not this APK.

The Windows installer is unsigned; Android uses the original development signing key. No games or commercial artwork are included. Automatic app updates, save-state sync, and CSO/CHD files are not included.
