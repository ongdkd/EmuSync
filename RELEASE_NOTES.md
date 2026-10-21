# EmuSync 1.3.1 — Larger game saves

The per-game save-data limit is raised from 128 MB to **1 GB** for Windows and Android. This allows larger game-data folders to pass the save check before PPSSPP opens. The current launcher-style library and previous sync protections are retained.

- Local capture, backup/recovery, Wi-Fi save transfers, Google Drive downloads, and Drive save metadata use the same expanded-data limit.
- ZIP transfers allow an additional 16 MB for archive overhead, while decompressed data is still limited to 1 GB.
- Wi-Fi save upload/download and Drive response reads allow up to 15 minutes for larger transfers. Existing connect and metadata request timeouts remain bounded.
- The 10,000-file protection stays in place and now has a separate error. A size-limit error identifies the file that exceeded the remaining allowance.

## Update both devices

- **Windows:** close EmuSync and run `EmuSync-Setup-1.3.1-x64.exe` over the current installation. Existing settings, games, saves, and backups are preserved.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **11** and the original signing identity is retained. Do not uninstall or clear app data.
- **Both devices must use 1.3.1 or newer** to exchange saves above the old 128 MB limit. Older apps will still reject larger data.
- `SHA256SUMS.txt` contains checksums for both downloads.

The alternate-key Android APK from `v1.0-preview.1` cannot be updated in place with the original-key APK. Preserve its private data before considering removal.

## Validation and limits

A real 129 MB synthetic save is captured, ZIP-encoded, decoded, and verified by hash. Tests also reject a declared file above 1 GB before reading it, distinguish the 10,000-file limit, and reject a forged ZIP whose expanded entry exceeds 1 GB. All 83 standard checks, 84 checks with Windows RedirectionGuard enforced, and the Windows authentication regression passed. Existing save backups, restore/rollback, conflicts, path validation, EmuDeck link handling, authentication, and transfer checks are retained.

Save bundles are currently buffered in memory. Larger saves use more RAM, local backup storage, and Google Drive quota; a full 1 GB real-device transfer has not been tested. Naruto Impact's actual save/game-data files and physical S24 Ultra behavior still need verification. No user saves were read, moved, or deleted during these tests.

Windows and Android Release builds completed. The installer version is 1.3.1; the Android package passed original-certificate, package-identity, version-code 11, and 16 KB alignment checks. Both packages contain the configured Google OAuth asset. Installing over a live user installation was not tested.

The Windows installer is unsigned; Android uses the original development signing key. Game ISO limits are unchanged at 8 GB. Automatic app updates and save-state sync are not included.
