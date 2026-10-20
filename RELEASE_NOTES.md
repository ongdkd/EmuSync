# EmuSync 1.1.0 — Google Drive preview

Windows and Android now include Google app registration, so **Connect Google Drive** is available without creating your own Google Cloud project.

## Downloads and update

- **Windows:** close EmuSync and run `EmuSync-Setup-1.1.0-x64.exe`. It updates the existing per-user installation while preserving app settings, games, and saves.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. The original signing certificate is retained and Android version code is now **5**. Do not clear data or uninstall to update.
- `SHA256SUMS.txt` contains checksums for both downloads.

The alternate-key Android APK from `v1.0-preview.1` cannot be replaced in place by this original-key APK. Preserve its private data before considering removal.

## Use Google Drive

1. On each device, keep your local games folder, PPSSPP PSP folder, and emulator selected.
2. In Settings, choose **Google Drive** and **Connect Google Drive**. Use the same Google account on each device.
3. Select a locally installed game and open **Game settings → Upload game to Drive** to make its ISO available to other devices. Uploads are optional and consume your Drive quota.
4. Choose **Play** or **Download & Play**. Saves are checked first; conflicting or incomplete progress asks before replacement.
5. Windows syncs after PPSSPP exits. On Android, save and exit PPSSPP, return to EmuSync, and confirm **Game is closed · Sync**.

Your PC can be off while another device syncs through Drive. Wi-Fi pairing remains available as a separate choice. Files are kept in an EmuSync folder in My Drive; the app requests access to app-created/selected files rather than your entire Drive.

If the Google consent app is still in Testing mode, only its configured test users can sign in. Other accounts may see an access-blocked message until the project owner adds them or completes Google's publication requirements. [Usage and developer setup](https://github.com/ongdkd/EmuSync/blob/main/docs/google-drive-setup.md).

## Validation and limits

64 automated checks cover save backups/recovery, conflicts, stale writes, concurrent Drive snapshots, delayed indexing, resumable ISO uploads, verified downloads, and hostile input. UI and native Windows checks cover configuration gating and switching between Drive and Wi-Fi. Installer checks verify installed file hashes, shortcuts, and clean uninstall; runtime browser profiles are excluded from packages. ARM64 package checks verify original signing identity, version, and 16 KB alignment. Android startup checks use source builds on native x64 emulators, not the downloadable ARM64 package.

Both download packages were verified to contain the supplied native Google registration. The configured Windows executable enables Connect Google Drive; its installer passed verification of 761 installed files and clean uninstall. [Android 11 and Android 16 startup and cold-relaunch checks passed](https://github.com/ongdkd/EmuSync/actions/runs/39452697587), with no fatal startup markers in either emulator log. Uploaded file hashes and sizes match the checked local packages.

Live Google consent, token renewal/revocation, Drive transfers between real devices, and S24 Ultra behavior are not yet independently verified. This is a preview: keep external backups of important saves, finish syncing before switching devices, and avoid simultaneous play of the same game.

The Windows installer is unsigned; Android uses the original development signing key. Uncompressed PSP ISO files and regular SAVEDATA are supported. Save states, CSO/CHD, and automatic app updates are not included. Interrupted ISO uploads currently restart when retried.
