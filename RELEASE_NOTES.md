# EmuSync 1.4.1 — Automatic Drive save cleanup

After every successful Google Drive save sync, EmuSync automatically keeps **the latest save plus two older backups per game**. No new setting or manual cleanup step is needed. Existing history is cleaned the next time that game completes a successful sync in this version.

- Cleanup runs after sync and local baseline persistence succeed, including uploads, downloads, and already-matching saves.
- Before deletion, the latest cloud ZIP is downloaded and verified against its expected hash, game ID, file count, size, and completeness. The cloud heads are checked again before deleting anything.
- Older ancestors are permanently deleted first, preserving the remaining history if cleanup is interrupted. Conflicting cloud branches are not pruned while unresolved; matching concurrent uploads can be joined safely before pruning.
- Only EmuSync `save-v1` archives for the synced game are eligible. Game ISOs, other games, local saves, local backups, and recovery records are not removed.
- Network/permission/validation failures during cleanup leave the successful sync intact. A warning appears in Settings → Library details, and cleanup retries after the next successful sync.
- The existing 1,000-entry listing protection remains: oversized legacy histories pause for attention rather than deleting from an incomplete listing.

**Older Drive save archives removed by this policy are permanently deleted, not moved to Trash.** The retained three copies and existing local backups remain available. Install the update on both devices for the same automatic behavior.

## Update

- **Windows:** close EmuSync and install `EmuSync-Setup-1.4.1-x64.exe` over the existing version. Settings, games, saves, and local backups are preserved.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **13**. Do not uninstall or clear app data.
- The alternate-key APK from `v1.0-preview.1` cannot update in place; preserve its private data before considering removal.
- Windows remains unsigned and Android uses the original development signing key. Checksums are in `SHA256SUMS.txt`.

## Validation and limits

All 102 standard checks, 103 checks with Windows RedirectionGuard enforced, and the Windows authentication regression passed. Synthetic Drive tests exercise exact three-save retention, corrupt-latest rejection, interrupted deletion and retry, ancestor ordering, preserved ISOs/other games, repeated no-op cleanup, unresolved and resolved conflicts, matching concurrent copies, a new cloud head arriving during verification, and rejection of cyclic history before any deletion. Existing sync, backup, rollback, import, account-switching, TLS, and transfer regressions remain in place.

Windows and Android Release builds completed. The installer reports 1.4.1. Android passed original-signing-certificate, package-identity, version-code 13, and 16 KB alignment checks. Both packages include the configured Google OAuth asset. Installation over a live user installation was not tested.

Live cleanup against a real Google account and physical S24 Ultra behavior have not been tested. No real user Drive files, games, or saves were deleted during development. The 1 GB save limit, 8 GB ISO limit, launcher layout, and PPSSPP integration remain unchanged.
