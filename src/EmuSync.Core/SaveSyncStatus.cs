namespace EmuSync.Core;

public record SaveSyncStatus(string State="unknown",string Message="Save check on Play",DateTimeOffset? LastSynced=null,int? Percent=null)
{
    public bool SafeToSwitch=>State is "synced" or "shared";
    public SaveSyncStatus Change(string state,string message,int? percent=null)=>new(state,message,
        state is "checked" or "synced"?DateTimeOffset.UtcNow:LastSynced,percent is int value?Math.Clamp(value,0,100):null);
    public SaveSyncStatus AfterRestart()=>State is "playing" or "syncing"
        ?Change("pending","Previous session needs a save check. Press Play or finish syncing."):this;
}
