# EmuSync 1.1.2 — EmuDeck linked-save support

EmuDeck's Windows PPSSPP setup links `memstick\PSP\SAVEDATA` to its central save folder. EmuSync previously rejected that directory with "Linked folders are not supported for syncing," even when the selected PSP folder was correct.

Windows now resolves the top-level SAVEDATA link and pins its destination while that save folder is open. Save capture, verified replacement, restore/recovery, and Wi-Fi hosting all use the same mapping. Links inside individual saves remain blocked; capture pauses rather than silently omitting linked content. Games and backup folders do not gain link-following behavior.

## Update

- **Windows:** close EmuSync and run `EmuSync-Setup-1.1.2-x64.exe` over the existing installation. Keep the same PPSSPP `memstick\PSP` folder selected. There is no need to move saves, remove EmuDeck's links, or change PPSSPP's storage configuration.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **7**, with the same package and original signing certificate. Its native folder access is unchanged. Do not uninstall or clear app data to update.
- `SHA256SUMS.txt` contains checksums for both downloads.

The Windows Google account chooser and Reset connection fix from 1.1.1 is retained. Drive sync still needs Google Drive API enabled in the OAuth project's Cloud Console; Testing-mode Google apps require the chosen account in their test-user list.

The alternate-key Android APK from `v1.0-preview.1` cannot be updated in place with the original-key APK. Preserve its private data before considering removal.

## Validation and limits

78 checks cover the existing save/Drive/transfer protection plus isolated Windows junction fixtures matching EmuDeck's layout. New checks cover recognizing SAVEDATA, capture from the real destination, verified writes, backup/restore and deletion of replaced game files, preserving other games, rejecting nested-link reads/writes/deletes and incomplete capture, a link retargeted during an active sync, and broken-link rejection. Tests use synthetic files in a temporary fixture; no user saves or EmuDeck configuration are modified.

Both platform builds, Windows installer compilation, and the production Windows authentication regression check passed. Android package checks verified version 1.1.2/code 7, the original signing certificate, and 16 KB alignment. Both packages retain the supplied native Google registration, verified by SHA-256.

Live Google consent, real Drive transfers, the user's actual EmuDeck installation, and physical S24 Ultra behavior still require validation. Installer install/uninstall and native Windows UI checks are not repeated against the user's running installation. Earlier Android 11/16 startup checks apply to the preceding 1.1.0 source, not this APK.

The Windows installer is unsigned; Android uses the original development signing key. Keep external backups of important saves. Automatic app updates and save-state sync are not included.

Reference: [EmuDeck Windows PPSSPP save-link setup](https://github.com/EmuDeck/emudeck-we/blob/main/functions/EmuScripts/emuDeckPPSSPP.ps1).
