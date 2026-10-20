using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using EmuSync.Core;

namespace EmuSync.App;

static class GoogleDriveAuth
{
    static TaskCompletionSource<AuthorizationResult>? pending;
    public static async Task<string> Token(bool interactive=false,string? email=null)
    {
        await DriveOAuthSettings.Require();
        var activity=Platform.CurrentActivity??throw new IOException("Open EmuSync before connecting Google Drive.");
        var builder=AuthorizationRequest.InvokeBuilder().SetRequestedScopes([new Scope(DriveClient.Scope)]);
        if(email is not null) builder.SetAccount(new Android.Accounts.Account(email,"com.google"));
        var result=await Identity.GetAuthorizationClient(activity).Authorize(builder.Build()).AsAsync<AuthorizationResult>();
        if(result.HasResolution)
        {
            if(!interactive) throw new IOException("Reconnect Google Drive in Settings before syncing.");
            if(pending is not null) throw new IOException("Google sign-in is already open.");
            var request=new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending=request;
            try
            {
                activity.StartIntentSenderForResult(result.PendingIntent!.IntentSender,732,null,0,0,0);
                result=await request.Task.WaitAsync(TimeSpan.FromMinutes(5));
            }
            finally { pending=null; }
        }
        if(result.AccessToken is null || !result.GrantedScopes.Contains(DriveClient.Scope)) throw new IOException("Google Drive permission was not granted.");
        return result.AccessToken;
    }
    public static void Complete(Result resultCode,Intent? data)
    {
        var request=pending;
        if(request is null) return;
        if(resultCode!=Result.Ok || data is null) { request.TrySetException(new IOException("Google Drive sign-in was cancelled.")); return; }
        try { request.TrySetResult(Identity.GetAuthorizationClient(Platform.CurrentActivity!).GetAuthorizationResultFromIntent(data)); }
        catch(Exception) { request.TrySetException(new IOException("Google could not connect this app. Check its Android OAuth registration and reconnect.")); }
    }
    public static async Task Disconnect(string? email)
    {
        if(email is null) return;
        var request=RevokeAccessRequest.InvokeBuilder().SetAccount(new Android.Accounts.Account(email,"com.google")).SetScopes([new Scope(DriveClient.Scope)]).Build();
        await Identity.GetAuthorizationClient(Platform.CurrentActivity!).RevokeAccess(request).AsAsync();
    }
}
