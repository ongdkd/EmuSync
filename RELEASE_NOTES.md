# EmuSync — Android startup fix

Android **1.0.1** fixes the immediate startup crash in the first preview. The original APK failed before the app UI with a missing `MauiApplication.n_onCreate` native callback. This build disables the affected native marshal optimization and regenerates the callback registration ([upstream issue](https://github.com/dotnet/maui/issues/35209)).

Download `EmuSync-Android.apk` and install it **over your existing app**. It keeps the same application ID and signing certificate, with a higher Android version code. Do not uninstall or clear app data to update. Games, saves, pairing, and settings are not reset by this update.

## Downloads

- **Windows:** `EmuSync-Setup-1.0.0-x64.exe` is unchanged from the first preview. Requires 64-bit Windows 10 version 1809 or later, or Windows 11. No PC reinstall is needed for this Android fix.
- **Android:** download and install `EmuSync-Android.apk`. Requires an ARM64 device with Android 8 or newer. Allow installation from your browser when prompted.
- `SHA256SUMS.txt` contains checksums for both downloads.

Install PPSSPP separately. No games or saves are included. See the [README](https://github.com/ongdkd/EmuSync#one-time-setup) for folder selection and pairing.

## Validation and preview limits

The original APK's native startup crash was reproduced on an Android 16 emulator. The regression workflow tests an in-place upgrade, actual native/web setup, and cold relaunch on Android 11 and Android 16. [View startup checks](https://github.com/ongdkd/EmuSync/actions/workflows/android-startup.yml).

All 39 automated core checks passed. The Windows installer retains its previously verified installation/uninstall behavior.

The Windows installer is unsigned, so Windows may warn. The Android APK uses a development signing key and is intended for evaluation. CSO/CHD, save states, internet/cloud sync, and automatic updates are not included.

Physical Android permissions and real PPSSPP launches with user games/saves have not been tested. Keep external backups while trying this preview.
