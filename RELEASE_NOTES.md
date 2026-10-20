# EmuSync 1.1.1 — Google account switching fix

Windows could save an OAuth token before Drive finished verifying the account. If Drive then refused the request, the app showed only **Connect**, hid Disconnect, and silently reused the same token on the next attempt.

- **Connect/Reconnect on Windows now opens Google's account chooser**, requests fresh consent, and keeps PKCE enabled. Background syncing still reuses valid tokens.
- Settings offers **Reset connection** even before a Drive connection succeeds. Once connected, the button becomes **Disconnect**. Resetting keeps local saves, settings, and Drive files.
- An already invalid/revoked Windows token no longer blocks resetting the connection.

## Update

- **Windows:** close EmuSync, then run `EmuSync-Setup-1.1.1-x64.exe` over the current installation. Existing settings, games, saves, and backups are preserved.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **6**, with the same package and original signing certificate. Do not uninstall or clear app data to update. The shared interface gains Reset connection; the native Android authorization flow is unchanged.
- `SHA256SUMS.txt` contains checksums for both downloads.

After updating Windows, choose **Settings → Google Drive → Connect Google Drive** and select the account you want. If needed, use **Reset connection** first. Use the same account on both devices. Revoking Google access may require reconnecting other devices; Drive files remain in their original account.

The alternate-key Android APK from `v1.0-preview.1` cannot be updated in place with this original-key APK. Preserve its private data before considering removal.

## Validation and limits

64 core checks cover save safety, backups/recovery, conflicts, verified transfers, and Drive snapshots/game transfers. A new check executes the production Windows authentication code with isolated storage and Google's SDK: valid background token reuse, interactive reconnection after cached sign-in, account chooser/consent parameters, offline refresh-token request, PKCE, and cancellation cleanup. The test stops before Google consent or token exchange. UI checks cover resetting before account verification, connection status, provider/configuration gating, and responsive layouts.

Both platform builds and Windows installer compilation passed. Android package checks verified the original signing identity, version 1.1.1/code 6, and 16 KB alignment. Both packages contain the configured Google registration and final UI assets, verified against the local source by SHA-256.

This fixes account switching; it does not change the Google project's API configuration, consent audience, or account storage limits. If Google Drive API is disabled, enable it in the same Cloud project that owns the OAuth clients. If the consent app is in Testing mode, the chosen account must be an allowed test user. [Setup guide](https://github.com/ongdkd/EmuSync/blob/main/docs/google-drive-setup.md).

Live Google consent, token renewal/revocation, real Drive transfers, and physical S24 Ultra behavior still require validation. Native Android startup checks passed for the preceding 1.1.0 source on Android 11/16; those emulator checks are not a test of this updated APK. Installer install/uninstall and native Windows UI checks have not been repeated against the user's running installation.

The Windows installer is unsigned; Android uses the original development signing key. Keep external backups of important saves. Automatic app updates are not included.
