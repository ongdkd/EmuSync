using EmuSync.App;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Newtonsoft.Json;

var cached=new TokenResponse {AccessToken="cached-test-token",RefreshToken="test-refresh",ExpiresInSeconds=3600,IssuedUtc=DateTime.UtcNow};
await SecureStorage.Default.SetAsync("emusync.google.tokens",JsonConvert.SerializeObject(cached));
if(await GoogleDriveAuth.Token()!=cached.AccessToken) throw new Exception("Background sync must reuse a valid cached token.");
var receiver=new AccountChooser();
try { await GoogleDriveAuth.Token(true,codeReceiver:receiver); throw new Exception("Interactive Connect reused a cached token."); }
catch(OperationCanceledException) { }
if(!receiver.Called) throw new Exception("Connect did not reach the account chooser.");
if(await SecureStorage.Default.GetAsync("emusync.google.tokens") is not null) throw new Exception("Cancelled reconnect retained the previous account's token.");
Console.WriteLine("PASS: background token reuse, reconnect after failed verification, account chooser, PKCE, and cancelled reconnect token cleanup.");

sealed class AccountChooser : ICodeReceiver
{
    public bool Called;
    public string RedirectUri=>"http://127.0.0.1:54321/authorize/";
    public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url,CancellationToken token)
    {
        Called=true;
        var query=Uri.UnescapeDataString(url.Build().Query.Replace("+"," "));
        if(!query.Contains("prompt=select_account consent")) throw new Exception("Google must show the account chooser and renew consent for an offline refresh token.");
        if(!query.Contains("access_type=offline")) throw new Exception("Reconnect must request an offline refresh token.");
        if(!query.Contains("code_challenge=") || !query.Contains("code_challenge_method=S256")) throw new Exception("Reconnect must retain PKCE.");
        throw new OperationCanceledException("Simulated user cancellation; no browser or Google request was made.");
    }
}

// Secure storage and app-package configuration stand-ins; the production Windows auth code is linked above.
namespace EmuSync.App
{
    static class SecureStorage
    {
        public static Store Default { get; }=new();
        public sealed class Store
        {
            readonly Dictionary<string,string> values=[];
            public Task SetAsync(string key,string value) { values[key]=value; return Task.CompletedTask; }
            public Task<string?> GetAsync(string key)=>Task.FromResult(values.GetValueOrDefault(key));
            public bool Remove(string key)=>values.Remove(key);
        }
    }
    sealed record DriveOAuthSettings(string DesktopClientId="test.apps.googleusercontent.com",string DesktopClientSecret="test")
    {
        public static Task<DriveOAuthSettings> Require()=>Task.FromResult(new DriveOAuthSettings());
    }
}
