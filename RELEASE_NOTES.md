# EmuSync 1.1.3 — Windows mount-point fix

The EmuDeck support in 1.1.2 resolved SAVEDATA using final-target traversal. Windows RedirectionGuard can reject that operation with error 448: "The path cannot be traversed because it contains an untrusted mount point."

EmuSync now reads the junction's immediate destination without traversing it, validates that the destination is a real directory, and uses that pinned destination for save operations. Windows security policy is left enabled. Links inside saves and destinations that are themselves links remain blocked.

## Update

- **Windows:** close EmuSync and run `EmuSync-Setup-1.1.3-x64.exe` over the current installation. Keep EmuDeck's PPSSPP `memstick\PSP` folder selected. No save files or EmuDeck links need moving or recreating.
- **Android:** install `EmuSync-Android.apk` over an original-key installation. Version code is **8**, with the same package and original signing certificate. Its native folder access is unchanged. Do not uninstall or clear app data to update.
- `SHA256SUMS.txt` contains checksums for both downloads.

The Google account chooser and Reset connection fix is retained. Drive sync requires Google Drive API enabled in the OAuth project's Cloud Console and, while Google's consent app is in Testing mode, an allowed test account.

The alternate-key Android APK from `v1.0-preview.1` cannot be updated in place with the original-key APK. Preserve its private data before considering removal.

## Validation and limits

The exact error was reproduced before the fix using a non-admin-created Windows junction and an isolated test process with RedirectionGuard enforcement explicitly enabled and verified. The old implementation failed in `GetFinalLinkTarget` with error 448.

After the fix, 78 standard checks and 79 guarded checks pass. The guarded run first proves that raw traversal of the test junction is denied with error 448, then verifies EmuSync can capture, apply, verify, and restore synthetic saves through the pinned real destination. Nested-link rejection, retarget protection, other-game preservation, broken-link rejection, and the existing Drive/transfer checks still pass. Fixture links are unlinked before recursive cleanup, including failed reproductions. Windows builds now run the guarded suite as well as the standard suite.

Both platform builds, Windows installer compilation, and the Windows authentication regression check passed. Android package checks verified version 1.1.3/code 8, the original signing certificate, and 16 KB alignment. Both packages retain the supplied native Google registration, verified by SHA-256.

The user reports that closing the installer/app and reopening the previous version resolves their current session. This update also handles the reproduced enforced-policy case in code. Live Google transfers, this new build on the user's actual EmuDeck installation, and physical S24 Ultra behavior still require validation. Installer install/uninstall and native Windows UI checks are not repeated against the user's running installation. Earlier Android 11/16 emulator startup checks apply to the preceding 1.1.0 source, not this APK.

The Windows installer is unsigned; Android uses the original development signing key. Keep external backups of important saves. Automatic app updates and save-state sync are not included.

References: [Microsoft RedirectionGuard policy](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-process-mitigation-redirection-trust-policy), [EmuDeck PPSSPP save-link setup](https://github.com/EmuDeck/emudeck-we/blob/main/functions/EmuScripts/emuDeckPPSSPP.ps1).
