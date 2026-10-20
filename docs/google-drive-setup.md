# One-time Google Drive setup

The distributed **1.1.0 preview** includes Google app registration. If you downloaded that release, skip to **Use it** below; you do not need to create your own Google Cloud project. If the consent app is in Testing mode, its owner must add your Google account as a test user before sign-in can succeed.

The steps below are for maintainers or people building their own registered copy. Source builds require an OAuth configuration file from the same Google Cloud project for both platforms.

## Register the app

1. Open [Google Cloud Console](https://console.cloud.google.com/) and create or select a project named **EmuSync**. Enable the **Google Drive API** in that project.
2. Open **Google Auth Platform**. Set the app name to EmuSync, choose your support/developer email, and configure the audience. For personal testing, add the Google account you will use on both devices as a test user. Request only `https://www.googleapis.com/auth/drive.file`.
3. In **Clients**, create a **Desktop app** OAuth client for Windows. Save its client ID and the installed-app client secret locally.
4. In the **same project**, create an **Android** OAuth client:

   - Package name: `app.emusync.local`
   - SHA-1 of the original signing certificate: `D9:CE:45:D0:02:90:B9:5A:8D:CC:22:2B:E7:65:CF:59:43:C3:39:20`

5. Put these values in `.tools/google-oauth.json` on the build machine:

```json
{
  "desktopClientId": "YOUR_DESKTOP_CLIENT_ID.apps.googleusercontent.com",
  "desktopClientSecret": "YOUR_INSTALLED_DESKTOP_CLIENT_SECRET",
  "androidClientId": "YOUR_ANDROID_CLIENT_ID.apps.googleusercontent.com"
}
```

Use actual issued IDs, including their numeric project prefix. Both clients must belong to the same Google project so they can access the same app-created Drive files. Do not use a Web application client or a service-account key. The desktop client is an installed/public client: its shipped client secret is not a confidential server credential; PKCE protects the authorization flow.

The `.tools/` folder is ignored by Git. Do not paste refresh tokens, account passwords, service-account keys, or private signing keystores into chat or GitHub. The two **client IDs are public identifiers** and can be shared to identify the registration. Keep the downloaded client configuration local.

Rebuild both apps with `./build.ps1 -Target All`, then `./build.ps1 -Target Installer`. Preserve the original `.tools/signing/emusync.keystore`; Google Android registration is bound to that certificate. Builds without OAuth configuration leave Drive sign-in disabled and keep Wi-Fi working.

Testing-mode Google grants may expire and require reconnecting. Broader distribution needs the Google consent screen/publication requirements completed; it has not been verified here.

## Use it

1. Install the configured Windows and Android builds. Keep the original Android app data when upgrading.
2. On each device, choose the local games folder, PPSSPP's PSP folder, and PPSSPP as before.
3. In Settings, choose **Google Drive**, then **Connect Google Drive**. Sign in with the same Google account on both devices. Tokens stay in Windows secure storage or Google Play services, outside pairing codes and backup/settings exports.
4. For each ISO you want available remotely, select it, open **Game settings**, and choose **Upload game to Drive**. Uploads are optional. Missing games download on Play; disabling automatic downloads asks first.
5. Press **Play** to check and sync saves before PPSSPP opens. Windows syncs after PPSSPP exits. On Android, exit PPSSPP, return to EmuSync, and confirm **Game is closed · Sync**.

The PC can be off when Android syncs through Drive. You need internet, enough Drive quota, and enough local storage for the ISO download/cache and destination copy. Remote ISO artwork appears after the game is downloaded and its metadata is read locally.

## Save protection and limits

- Files are stored in an **EmuSync** folder in My Drive. The app only requests access to files it creates/uses, not your entire Drive.
- Saves are immutable ZIP snapshots with parent links. Simultaneous different uploads preserve both versions and ask which cloud copy to use; local-vs-cloud conflicts ask before replacement. This avoids relying on clocks or silently overwriting a concurrent save.
- Local backups and recovery records are retained. Drive snapshots are not automatically deleted; they consume quota. The first version stops for attention after 1,000 revisions per game.
- Save transfers retain the existing 128 MB/10,000-file limits; ISOs are limited to 8 GB. Expanded ZIPs, paths, game IDs, hashes, response sizes, and upload URLs are checked before use.
- Uploads use 4 MB resumable chunks. Interrupted uploads currently restart when retried; upload sessions are not persisted across app launches.
- If two different ISOs have the same PSP game ID, the app pauses cloud game access instead of choosing silently. Keep the intended copy in the Drive folder and remove the duplicate, then Refresh games. Existing local games/saves are retained.
- Disconnecting revokes Google access and may require reconnecting on other devices. Files remain on Drive. Account and Wi-Fi save baselines are kept separate.

Real Google consent, token renewal, and transfers between a configured Windows install and an S24 Ultra still require validation. Automated checks use a fake Drive server and synthetic saves/ISOs. No user Drive files have been read or uploaded during development.

References: [Google Android authorization](https://developer.android.com/identity/authorization), [desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Drive file scope](https://developers.google.com/workspace/drive/api/guides/api-specific-auth), [resumable uploads](https://developers.google.com/workspace/drive/api/guides/manage-uploads).
