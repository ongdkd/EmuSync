using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmuSync.Core;

public record SaveInfo(string? Hash, int Files, long Bytes, bool Suspicious);
public sealed class SaveBundle
{
    const string Journal="pending-save.json";
    record Recovery(string Id,string Backup);
    public static bool RecoveryNeeded(string backups)=>File.Exists(Path.Combine(backups,Journal));
    public static async Task Recover(IGameFolder folder,string backups)
    {
        var journal=Path.Combine(backups,Journal);
        if(!File.Exists(journal)) return;
        var info=new FileInfo(journal);
        if(info.Length>2048) throw new IOException("The recovery record is invalid. Preserve the backup folder for recovery.");
        var recovery=JsonSerializer.Deserialize<Recovery>(await File.ReadAllTextAsync(journal))??throw new IOException("Invalid recovery record.");
        SafePath.GameId(recovery.Id); SafePath.Validate(recovery.Backup);
        if(recovery.Backup.Contains('/') || !recovery.Backup.StartsWith(recovery.Id+"-",StringComparison.Ordinal)) throw new IOException("Invalid backup location.");
        var original=FromZip(await File.ReadAllBytesAsync(Path.Combine(backups,recovery.Backup)),recovery.Id);
        var current=await Capture(folder,recovery.Id);
        var preserved=$"{recovery.Id}-before-recovery-{Guid.NewGuid():N}.zip";
        using(var input=new MemoryStream(current.Zip())) await new LocalFolder(backups).Write(preserved,input);
        foreach(var (path,bytes) in original.Files) { using var input=new MemoryStream(bytes); await folder.Write(path,input); }
        foreach(var path in current.Files.Keys.Except(original.Files.Keys,StringComparer.Ordinal)) await folder.Delete(path);
        if((await Capture(folder,recovery.Id)).Info.Hash!=original.Info.Hash) throw new IOException("Recovery did not verify. Keep the backup folder and do not launch this game yet.");
        File.Delete(journal);
    }
    public const int Limit = 128 * 1024 * 1024;
    public SortedDictionary<string,byte[]> Files { get; } = new(StringComparer.Ordinal);
    public SaveInfo Info
    {
        get
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var (path,bytes) in Files)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(path+"\0"));
                hash.AppendData(SHA256.HashData(bytes));
            }
            return new(Files.Count == 0 ? null : Convert.ToHexString(hash.GetHashAndReset()),Files.Count,Files.Values.Sum(b=>(long)b.Length),Files.Values.Any(b=>b.Length==0));
        }
    }
    public static async Task<SaveBundle> Capture(IGameFolder folder,string id)
    {
        SafePath.GameId(id);
        var result = new SaveBundle();
        long totalBytes=0;
        foreach (var item in await folder.List("SAVEDATA"))
        {
            if (!item.IsDirectory || !item.Name.StartsWith(id,StringComparison.Ordinal)) continue;
            await Visit("SAVEDATA/"+item.Name,0);
        }
        return result;
        async Task Visit(string path,int depth)
        {
            if (depth > 16) throw new InvalidDataException("Save folders are nested too deeply.");
            foreach (var entry in await folder.List(path))
            {
                var name = SafePath.Validate(path+"/"+entry.Name);
                if (entry.IsDirectory) { await Visit(name,depth+1); continue; }
                if (result.Files.Count >= 10000 || entry.Size > Limit-totalBytes)
                    throw new InvalidDataException("This save is too large to sync safely (128 MB limit).");
                await using var input = await folder.Read(name);
                using var output = new MemoryStream();
                await CopyLimited(input,output,Limit-totalBytes);
                if (output.Length != entry.Size) throw new IOException("The save changed while reading. Close PPSSPP and try again.");
                totalBytes+=output.Length;
                result.Files.Add(name,output.ToArray());
            }
        }
    }
    public byte[] Zip()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output,ZipArchiveMode.Create,true))
            foreach (var (path,bytes) in Files)
            {
                using var stream = zip.CreateEntry(path,CompressionLevel.Fastest).Open();
                stream.Write(bytes);
            }
        return output.ToArray();
    }
    public static SaveBundle FromZip(byte[] data,string id)
    {
        SafePath.GameId(id);
        if (data.Length > Limit) throw new InvalidDataException("Save download exceeds the safety limit.");
        using var input = new MemoryStream(data);
        using var zip = new ZipArchive(input,ZipArchiveMode.Read);
        if (zip.Entries.Count > 10000) throw new InvalidDataException("Too many save files.");
        var bundle = new SaveBundle();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var path = SafePath.Validate(entry.FullName);
            var parts = path.Split('/');
            if (parts.Length < 3 || parts[0] != "SAVEDATA" || !parts[1].StartsWith(id,StringComparison.Ordinal) || !names.Add(path))
                throw new InvalidDataException("The download contains unexpected save files.");
            if (entry.Length > Limit || (total += entry.Length) > Limit)
                throw new InvalidDataException("The expanded save exceeds the safety limit.");
            using var source = entry.Open(); using var target = new MemoryStream();
            var buffer = new byte[65536]; int count;
            while ((count=source.Read(buffer))>0)
            {
                if (target.Length+count > entry.Length) throw new InvalidDataException("Invalid save archive length.");
                target.Write(buffer,0,count);
            }
            if (target.Length != entry.Length) throw new InvalidDataException("Incomplete save download.");
            bundle.Files.Add(path,target.ToArray());
        }
        return bundle;
    }
    public static async Task Apply(IGameFolder folder,string id,SaveBundle incoming,string backups)
    {
        // Validate every path before touching the user's files, including locally constructed bundles.
        incoming = FromZip(incoming.Zip(),id);
        if (incoming.Info.Hash is null || incoming.Info.Suspicious) throw new InvalidDataException("Empty saves will not replace existing progress.");
        var before = await Capture(folder,id);
        Directory.CreateDirectory(backups);
        if(RecoveryNeeded(backups)) throw new IOException("An interrupted sync needs recovery before another save can be replaced.");
        var backup = Path.Combine(backups,$"{id}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        var backupFolder=new LocalFolder(backups);
        using(var original=new MemoryStream(before.Zip())) await backupFolder.Write(Path.GetFileName(backup),original);
        using(var record=new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new Recovery(id,Path.GetFileName(backup))))) await backupFolder.Write(Journal,record);
        try
        {
            await Replace(incoming,before);
            if ((await Capture(folder,id)).Info.Hash != incoming.Info.Hash) throw new IOException("Save verification failed.");
            File.Delete(Path.Combine(backups,Journal));
        }
        catch (Exception error)
        {
            try
            {
                await Replace(before,incoming);
                if((await Capture(folder,id)).Info.Hash!=before.Info.Hash) throw new IOException("Backup restore did not verify.");
                File.Delete(Path.Combine(backups,Journal));
            }
            catch (Exception rollback) { throw new IOException($"Save restore failed. Your original backup is at {backup}.",new AggregateException(error,rollback)); }
            throw new IOException("Sync failed; your original saves were restored. "+error.Message,error);
        }
        async Task Replace(SaveBundle source,SaveBundle previous)
        {
            foreach (var (path,bytes) in source.Files)
            { using var stream = new MemoryStream(bytes); await folder.Write(path,stream); }
            foreach (var path in previous.Files.Keys.Except(source.Files.Keys,StringComparer.Ordinal)) await folder.Delete(path);
        }
    }
    public static async Task CopyLimited(Stream input,Stream output,long limit,CancellationToken token=default)
    {
        var buffer = new byte[65536]; long total=0; int count;
        while ((count=await input.ReadAsync(buffer,token))>0)
        {
            if ((total+=count)>limit) throw new InvalidDataException("Transfer exceeds the safety limit.");
            await output.WriteAsync(buffer.AsMemory(0,count),token);
        }
    }
}
