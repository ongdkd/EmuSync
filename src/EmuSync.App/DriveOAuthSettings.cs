using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmuSync.App;

public sealed record DriveOAuthSettings(string DesktopClientId="",string DesktopClientSecret="",string AndroidClientId="")
{
    public static async Task<DriveOAuthSettings> Load()
    {
        try
        {
            if(!await FileSystem.AppPackageFileExistsAsync("google-oauth.json")) return new();
            await using var input=await FileSystem.OpenAppPackageFileAsync("google-oauth.json");
            return await JsonSerializer.DeserializeAsync<DriveOAuthSettings>(input,new JsonSerializerOptions(JsonSerializerDefaults.Web))??new();
        }
        catch(Exception e) when(e is IOException or JsonException) { return new(); }
    }
    public bool Configured=>Valid(DesktopClientId) && Valid(AndroidClientId) && DesktopClientId.Split('-')[0]==AndroidClientId.Split('-')[0];
    static bool Valid(string value)=>value is not null && Regex.IsMatch(value,"^[0-9]+-[A-Za-z0-9_-]+\\.apps\\.googleusercontent\\.com$");
    public static async Task<DriveOAuthSettings> Require()
    {
        var config=await Load();
        if(!config.Configured) throw new IOException("Google Drive needs one-time app registration. See docs/google-drive-setup.md; this build has no Google OAuth configuration yet.");
        return config;
    }
}
