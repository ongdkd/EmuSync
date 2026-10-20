using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using EmuSync.Core;
using Newtonsoft.Json;

namespace EmuSync.App;

static class GoogleDriveAuth
{
    const string User="EmuSync";
    sealed class TokenStore : IDataStore
    {
        const string Key="emusync.google.tokens";
        public Task StoreAsync<T>(string key,T value)=>SecureStorage.Default.SetAsync(Key,JsonConvert.SerializeObject(value));
        public async Task<T> GetAsync<T>(string key)
        {
            var value=await SecureStorage.Default.GetAsync(Key);
            return value is null?default!:JsonConvert.DeserializeObject<T>(value)!;
        }
        public Task DeleteAsync<T>(string key) { SecureStorage.Default.Remove(Key); return Task.CompletedTask; }
        public Task ClearAsync()=>DeleteAsync<TokenResponse>(User);
    }
    public static async Task<string> Token(bool interactive=false,string? email=null)
    {
        var config=await DriveOAuthSettings.Require();
        var store=new TokenStore();
        var initializer=new GoogleAuthorizationCodeFlow.Initializer {ClientSecrets=new ClientSecrets {ClientId=config.DesktopClientId,ClientSecret=config.DesktopClientSecret},DataStore=store,Scopes=[DriveClient.Scope]};
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(interactive?5:1));
        try
        {
            if(interactive)
            {
                var credential=await GoogleWebAuthorizationBroker.AuthorizeAsync(initializer,[DriveClient.Scope],User,true,timeout.Token,store);
                using(credential.Flow) return await credential.GetAccessTokenForRequestAsync(cancellationToken:timeout.Token);
            }
            var saved=await store.GetAsync<TokenResponse>(User)??throw new IOException("Connect Google Drive in Settings first.");
            using var flow=new GoogleAuthorizationCodeFlow(initializer);
            return await new UserCredential(flow,User,saved).GetAccessTokenForRequestAsync(cancellationToken:timeout.Token);
        }
        catch(TokenResponseException) { throw new IOException("Google sign-in expired or permission was denied. Reconnect Google Drive in Settings."); }
    }
    public static async Task Disconnect(string? email)
    {
        var store=new TokenStore();
        var saved=await store.GetAsync<TokenResponse>(User);
        if(saved is not null)
        {
            using var client=new HttpClient(new SocketsHttpHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(30)};
            using var form=new FormUrlEncodedContent(new Dictionary<string,string> {{"token",saved.RefreshToken??saved.AccessToken}});
            using var response=await client.PostAsync("https://oauth2.googleapis.com/revoke",form);
            if(!response.IsSuccessStatusCode) throw new IOException("Google could not revoke access. Reconnect or remove EmuSync access in your Google account settings.");
        }
        await store.ClearAsync();
    }
}
