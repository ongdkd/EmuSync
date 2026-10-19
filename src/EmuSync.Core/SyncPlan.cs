namespace EmuSync.Core;

public enum SyncAction { None, Upload, Download, Conflict, Suspicious }
public static class SyncPlan
{
    public static SyncAction Decide(string? local,string? remote,string? baseline,bool suspicious)
    {
        if (suspicious) return SyncAction.Suspicious;
        if (local == remote) return SyncAction.None;
        if (local is null) return baseline is null ? SyncAction.Download : SyncAction.Suspicious;
        if (remote is null) return baseline is null ? SyncAction.Upload : SyncAction.Suspicious;
        if (local == baseline) return SyncAction.Download;
        if (remote == baseline) return SyncAction.Upload;
        return SyncAction.Conflict;
    }
}
