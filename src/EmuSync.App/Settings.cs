using System.Text.Json;
using EmuSync.Core;

namespace EmuSync.App;

public record Baseline(string? Hash,string[] Paths);
public sealed class Settings
{
    public string? GamesFolder { get; set; }
    public string? PspFolder { get; set; }
    public string? Emulator { get; set; }
    public string? PairCode { get; set; }
    public string? Selected { get; set; }
    public string? PendingSession { get; set; }
    public string SyncProvider { get; set; }="wifi";
    public string? DriveAccountId { get; set; }
    public string? DriveEmail { get; set; }
    public Dictionary<string,Baseline> Baselines { get; set; }=[];
    public Dictionary<string,SaveSyncStatus> SaveStatus { get; set; }=[];
    public Dictionary<string,bool> SyncGames { get; set; }=[];
    public List<Game> CachedGames { get; set; }=[];
    public List<Game> CachedDriveGames { get; set; }=[];
    static string PathName=>Path.Combine(FileSystem.AppDataDirectory,"settings.json");
    public static Settings Load()
    {
        if(!File.Exists(PathName)) return new();
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName)) ?? throw new InvalidDataException("Empty settings."); }
        catch(Exception e) when(e is JsonException or InvalidDataException)
        { throw new InvalidDataException("Settings could not be read. Keep settings.json for recovery instead of resetting it.",e); }
    }
    public async Task Save()
    {
        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        var temp=PathName+".tmp";
        await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(this));
        File.Move(temp,PathName,true);
    }
}
