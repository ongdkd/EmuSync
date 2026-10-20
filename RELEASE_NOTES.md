# EmuSync — Android update compatibility fix

Android **1.0.2** restores the original EmuSync signing certificate and raises the Android version code to **3**. It includes the app changes from `v1.0-preview.1` and retains the startup-crash and older-WebView fixes.

Download `EmuSync-Android.apk` and install it **over the original app or Android 1.0.1 from `v1.0.1-preview.3`**. No uninstall or data clearing is needed. The package ID remains `app.emusync.local`.

The APK published in `v1.0-preview.1` used a different signing key. Android cannot replace that installation with an APK signed by the original key. If you installed that particular APK, do not uninstall it before preserving its app-private settings/backups. Selecting a new key does not recover access to another key's private data.

## Downloads

- **Android:** `EmuSync-Android.apk`, for ARM64 devices with Android 8 or newer.
- `SHA256SUMS.txt` contains the APK checksum.
- **Windows:** no update is needed for this signing fix. The [latest existing Windows portable build](https://github.com/ongdkd/EmuSync/releases/tag/v1.0-preview.1) remains available separately.

Install PPSSPP separately. No games or saves are included.

## Validation and preview limits

The build now explicitly uses the original keystore and checks the finished APK's public certificate, package ID, version, and 16 KB alignment before accepting it. The incompatible APK from `v1.0-preview.1` is rejected by the same check. The private key is excluded from Git and release assets.

All 39 core checks and the UI regression check passed. The tagged source passed startup, the real JavaScript-to-.NET setup call, and cold relaunch with persisted settings on Android 11 and Android 16 native x64 emulators. Both setup screenshots were inspected and neither emulator logged a fatal startup error. The downloadable ARM64 package's signature, version, alignment, and startup callback were checked separately. [View passing startup checks](https://github.com/ongdkd/EmuSync/actions/runs/39436860562).

This remains a development-signed preview. Physical S24 Ultra installation, Android folder permissions, and real PPSSPP launches/sync with user games and saves have not been tested.
