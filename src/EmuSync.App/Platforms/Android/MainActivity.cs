using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;

namespace EmuSync.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    internal static TaskCompletionSource<string?>? FolderRequest;
    protected override void OnActivityResult(int requestCode,Result resultCode,Intent? data)
    {
        base.OnActivityResult(requestCode,resultCode,data);
        if(requestCode==732) { GoogleDriveAuth.Complete(resultCode,data); return; }
        if(requestCode!=731 || FolderRequest is null) return;
        var request=FolderRequest; FolderRequest=null;
        if(resultCode!=Result.Ok || data?.Data is null) { request.TrySetResult(null); return; }
        try {
            ContentResolver!.TakePersistableUriPermission(data.Data,data.Flags & (ActivityFlags.GrantReadUriPermission|ActivityFlags.GrantWriteUriPermission));
            request.TrySetResult(data.Data.ToString());
        } catch(Exception e) { request.TrySetException(e); }
    }
}
