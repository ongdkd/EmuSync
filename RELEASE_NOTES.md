# EmuSync 1.1.0 — Google Drive integration candidate

This candidate adds Google Drive to Windows and Android alongside existing Wi-Fi sync:

- Connect the same Google account on both devices. Check saves on Play; ask on conflicts or incomplete progress.
- Preserve separate remote snapshots and local backups, including concurrent uploads from different devices.
- Optionally upload an ISO from Game settings. Missing games download, verify, and launch when you choose Download & Play.
- Windows syncs again after PPSSPP exits; Android asks you to confirm the game is closed before uploading progress.

**Google app registration is not configured yet.** Google sign-in remains disabled until the [one-time setup](https://github.com/ongdkd/EmuSync/blob/main/docs/google-drive-setup.md) is completed and both apps are rebuilt. The public Android 1.0.2 remains the Wi-Fi download. These candidate files are for review; they do not yet provide a verified working Google account connection.

The private Android signing key is unchanged, and version code 4 updates an original-key installation without clearing its data. The incompatible alternate-key APK from v1.0-preview.1 still cannot be replaced in place by an original-key APK. Do not uninstall it before preserving private data.

## Validation

64 automated checks cover synthetic save safety, HTTPS pairing, verified game transfers, Drive branches, cancelled conflicts, stale updates, delayed indexing, resumable chunks, hostile upload locations, duplicate ISOs, and corrupted downloads. UI checks cover both sync choices, configuration gating, optional game uploads, selection without syncing, and responsive layouts. Both platform implementations compile without warnings.

Google consent, token refresh/revocation, real Drive API behavior, and Windows-to-S24 transfers still require live validation after app registration. No user Google Drive files have been read or uploaded. The Android APK remains development-signed; the Windows installer is unsigned.
