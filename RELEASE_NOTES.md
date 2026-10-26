# EmuSync 1.5.0 — Streamed large saves and clear sync status

Large saves now use temporary ZIP files and bounded transfer buffers instead of holding whole saves and duplicate ZIPs in RAM. Game details and library cards show save state, progress, and the last successful sync time.

## Large-save handling

- Capture streams each save file directly into a temporary ZIP while computing the existing SHA-256 save hash. Save downloads are staged to disk and verified one entry at a time.
- Google Drive and Wi-Fi save transfers, durable backups, replacement, rollback, and interrupted-sync recovery use streamed archives. Metadata remains bounded in memory. Compression and hashing run off the UI thread.
- Existing Drive saves, local backup ZIPs, Wi-Fi requests, and save hashes keep their format. The save limit stays at **1 GB per game / 10,000 files**, with 16 MB for ZIP overhead; ISO limits stay at 8 GB.
- Temporary archives close after use, failed/cancelled staging is removed, and abandoned staging files are cleaned on a later app start without deleting live snapshots. User saves and durable backups are separate from this staging area.

## Clear sync status

- Every game has a separate status and last successful sync time, scoped to the sync provider/account.
- **Syncing save** shows byte percentages for reading, upload, download, verification, and available apply stages. Stages without a useful percentage show an indeterminate progress bar.
- **Ready to play** means the pre-launch save check succeeded; it does not claim that a game session is finished.
- **Playing · Sync after exit** stays distinct from **Safe to switch devices**, which requires a successful post-game sync. Wi-Fi host-only sessions say the save is available on this PC, rather than claiming a cloud transfer.
- Failed, offline, cancelled, and interrupted sessions show that sync is still needed while retaining the previous successful time. Empty save folders show **No save yet**.
- The current phone layout, fixed Play button/header, Back navigation, ISO import, and PPSSPP setup/return behavior are retained.

Automatic Drive cleanup still keeps the latest save plus two older backups after successful sync. Unresolved conflicts remain protected. Older eligible Drive archives are permanently deleted under that previously introduced policy; local backups are retained.

## Update

- **Windows:** close EmuSync and run `EmuSync-Setup-1.5.0-x64.exe` over the current installation. Settings, games, saves, and local backups are preserved.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **14**. Do not uninstall or clear app data.
- Update both devices to use the new memory behavior on both sides of Wi-Fi sync. The protocol is compatible with existing previews, but older apps still have their previous buffering behavior.
- The alternate-key APK from `v1.0-preview.1` cannot update in place; preserve its private data before considering removal.
- Windows remains unsigned; Android uses the original development signing key. Checksums are in `SHA256SUMS.txt`.

## Validation and limits

All **116 standard checks**, **117 checks with Windows RedirectionGuard enforced**, Windows authentication regressions, and browser UI checks passed. Windows and Android Release builds completed. The installer reports 1.5.0; Android passed original-signing-certificate, package-identity, version-code 14, and 16 KB alignment checks. Both packages include the configured OAuth asset and UI files matching the tested source. Installation over a live user installation was not tested.

Tests capture and verify a full **1 GB synthetic save**. An incompressible **129 MB save** produces a ZIP larger than 128 MB and captures/decodes with about **1.8 MB of managed allocations** in this test run (enforced below 32 MB). A real pinned-TLS Wi-Fi download/upload, server backup/apply, and verification of that same 129 MB save stay below a separate 96 MB managed-allocation ceiling. These allocation checks measure the save pipeline, not total application/native/WebView RAM.

Regression checks cover the legacy save-hash fixture, path/size/file-count validation, forged ZIPs, interrupted cleanup and apply recovery, rollback, cancellation cleanup, Drive conflicts/retention, and authentication. Status tests distinguish pre-launch checks, playing, interrupted sessions, and completed sync. Browser tests verify progress, last sync time, per-game isolation, library badges, accessible controls, and phone/desktop layouts with a simulated native bridge.

Physical S24 Ultra behavior, live Google transfers/cleanup, and actual PPSSPP exit/foreground behavior still require device validation. No real user saves or Drive files were used or deleted during development. Streaming trades RAM for disk space: keep several GB free for staging and durable backups with saves near 1 GB. Android still asks you to confirm PPSSPP is closed before syncing; force-stopping apps cannot guarantee automatic return or background sync.
