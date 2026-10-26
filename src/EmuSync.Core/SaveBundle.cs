using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmuSync.Core;

public record SaveInfo(string? Hash,int Files,long Bytes,bool Suspicious);
public sealed class SaveBundle : IDisposable
{
    public const int Limit=1024*1024*1024;
    public const int ArchiveLimit=Limit+16*1024*1024;
    const string Journal="pending-save.json";
    static readonly string Scratch=Path.Combine(Path.GetTempPath(),"EmuSync-save-work");
    record Recovery(string Id,string Backup);
    readonly FileStream archive;
    public string Id { get; }
    public SaveInfo Info { get; }
    public IReadOnlyList<string> Paths { get; }
    SaveBundle(FileStream archive,string id,SortedDictionary<string,(long Size,byte[] Hash)> files)
    {
        this.archive=archive; Id=id; Paths=files.Keys.ToArray();
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var (path,file) in files) { hash.AppendData(Encoding.UTF8.GetBytes(path+"\0")); hash.AppendData(file.Hash); }
        Info=new(files.Count==0?null:Convert.ToHexString(hash.GetHashAndReset()),files.Count,files.Values.Sum(f=>f.Size),files.Values.Any(f=>f.Size==0));
    }
    static SaveBundle()
    {
        Directory.CreateDirectory(Scratch);
        if(new DirectoryInfo(Scratch).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("The temporary save folder cannot be a linked folder.");
        // Only abandoned, exclusively lockable staging files are removed; live snapshots stay open.
        foreach(var path in Directory.EnumerateFiles(Scratch,"save-*.tmp"))
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),"^save-[a-f0-9]{32}\\.tmp$")) continue;
            try { if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) continue; using(var file=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { } File.Delete(path); }
            catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
    }
    static FileStream Temporary()=>new(Path.Combine(Scratch,"save-"+Guid.NewGuid().ToString("N")+".tmp"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read|FileShare.Delete,65536,FileOptions.Asynchronous|FileOptions.DeleteOnClose);
    public Stream OpenZip()=>new FileStream(archive.Name,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
    public void Dispose()=>archive.Dispose();
    public static bool RecoveryNeeded(string backups)=>File.Exists(Path.Combine(backups,Journal));
    internal static Action<long> Progress(Action<string>? report,string stage,long total)
    {
        var previous=-1;
        return bytes=> { var percent=total==0?100:(int)Math.Clamp(bytes*100/total,0,100); if(percent!=previous) { previous=percent; report?.Invoke($"{stage} · {percent}%"); } };
    }
    public static Task<SaveBundle> Capture(IGameFolder folder,string id,Action<string>? progress=null,CancellationToken token=default)
        =>Task.Run(()=>CaptureCore(folder,id,progress,token),token);
    static async Task<SaveBundle> CaptureCore(IGameFolder folder,string id,Action<string>? progress,CancellationToken token)
    {
        SafePath.GameId(id);
        var paths=new SortedDictionary<string,long>(StringComparer.Ordinal); long total=0;
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in await folder.List("SAVEDATA"))
            if(item.IsDirectory && item.Name.StartsWith(id,StringComparison.Ordinal)) await Visit("SAVEDATA/"+item.Name,0);
        var output=Temporary();
        try
        {
            var files=new SortedDictionary<string,(long Size,byte[] Hash)>(StringComparer.Ordinal);
            var report=Progress(progress,"Reading save",total); long done=0;
            using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
                foreach(var (path,size) in paths)
                {
                    token.ThrowIfCancellationRequested();
                    await using var input=await folder.Read(path);
                    using var target=zip.CreateEntry(path,CompressionLevel.Fastest).Open();
                    files.Add(path,(size,await CopyHash(input,target,size,n=>report(done+n),token))); done+=size;
                }
            await output.FlushAsync(token);
            if(output.Length>ArchiveLimit) throw new InvalidDataException("The save archive exceeds the transfer limit.");
            report(total); return new(output,id,files);
        }
        catch { output.Dispose(); throw; }
        async Task Visit(string path,int depth)
        {
            token.ThrowIfCancellationRequested();
            if(depth>16) throw new InvalidDataException("Save folders are nested too deeply.");
            foreach(var entry in await folder.List(path))
            {
                var name=SafePath.Validate(path+"/"+entry.Name);
                if(entry.IsDirectory) { await Visit(name,depth+1); continue; }
                if(paths.Count>=10000) throw new InvalidDataException("This game's save folders contain more than 10,000 files.");
                if(!names.Add(name)) throw new InvalidDataException("The save contains duplicate file paths.");
                if(entry.Size<0 || entry.Size>Limit-total) throw new InvalidDataException($"This game's save data exceeds the 1 GB limit at {name}.");
                total+=entry.Size; paths.Add(name,entry.Size);
            }
        }
    }
    public static Task<SaveBundle> FromZip(Stream input,string id,Action<string>? progress=null,long? length=null,CancellationToken token=default)
        =>Task.Run(()=>ReadArchive(input,id,progress,length,token),token);
    static async Task<SaveBundle> ReadArchive(Stream input,string id,Action<string>? progress,long? length,CancellationToken token)
    {
        SafePath.GameId(id);
        if(length>ArchiveLimit || input.CanSeek && input.Length-input.Position>ArchiveLimit) throw new InvalidDataException("Save download exceeds the safety limit.");
        var output=Temporary();
        try
        {
            await CopyLimited(input,output,ArchiveLimit,token,length is >0?Progress(progress,"Downloading save",length.Value):null);
            await output.FlushAsync(token); output.Position=0;
            using var zip=new ZipArchive(output,ZipArchiveMode.Read,true);
            if(zip.Entries.Count>10000) throw new InvalidDataException("Too many save files.");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total=0;
            foreach(var entry in zip.Entries)
            {
                var path=SafePath.Validate(entry.FullName); var parts=path.Split('/');
                if(parts.Length<3 || parts[0]!="SAVEDATA" || !parts[1].StartsWith(id,StringComparison.Ordinal) || !names.Add(path)) throw new InvalidDataException("The download contains unexpected save files.");
                if(entry.Length<0 || entry.Length>Limit || (total+=entry.Length)>Limit) throw new InvalidDataException("The expanded save exceeds the safety limit.");
            }
            var files=new SortedDictionary<string,(long Size,byte[] Hash)>(StringComparer.Ordinal);
            var report=Progress(progress,"Verifying save",total); long done=0;
            foreach(var entry in zip.Entries)
            {
                using var source=entry.Open();
                files.Add(entry.FullName,(entry.Length,await CopyHash(source,Stream.Null,entry.Length,n=>report(done+n),token))); done+=entry.Length;
            }
            report(total); return new(output,id,files);
        }
        catch { output.Dispose(); throw; }
    }
    static async Task<byte[]> CopyHash(Stream input,Stream output,long expected,Action<long> progress,CancellationToken token)
    {
        var buffer=ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long total=0; int count;
            while((count=await input.ReadAsync(buffer.AsMemory(0,65536),token))>0)
            {
                if((total+=count)>expected) throw new InvalidDataException("Save data changed or an archive entry has an invalid length. Close PPSSPP and retry.");
                hash.AppendData(buffer,0,count); await output.WriteAsync(buffer.AsMemory(0,count),token); progress(total);
            }
            if(total!=expected) throw new IOException("The save changed while reading. Close PPSSPP and try again.");
            return hash.GetHashAndReset();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    public async Task Backup(string directory,string name)
    { using var input=OpenZip(); await new LocalFolder(directory).Write(name,input); }
    static async Task Replace(IGameFolder folder,SaveBundle source,SaveBundle previous,Action<string>? progress=null)
    {
        using var input=source.OpenZip(); using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        var report=Progress(progress,"Applying save",source.Info.Bytes); long done=0;
        foreach(var entry in zip.Entries)
        { using var stream=entry.Open(); await folder.Write(entry.FullName,stream); done+=entry.Length; report(done); }
        foreach(var path in previous.Paths.Except(source.Paths,StringComparer.Ordinal)) await folder.Delete(path);
        report(source.Info.Bytes);
    }
    public static async Task Recover(IGameFolder folder,string backups)
    {
        var journal=Path.Combine(backups,Journal);
        if(!File.Exists(journal)) return;
        if(new FileInfo(journal).Length>2048) throw new IOException("The recovery record is invalid. Preserve the backup folder for recovery.");
        var recovery=JsonSerializer.Deserialize<Recovery>(await File.ReadAllTextAsync(journal))??throw new IOException("Invalid recovery record.");
        SafePath.GameId(recovery.Id); SafePath.Validate(recovery.Backup);
        if(recovery.Backup.Contains('/') || !recovery.Backup.StartsWith(recovery.Id+"-",StringComparison.Ordinal)) throw new IOException("Invalid backup location.");
        using var file=File.OpenRead(Path.Combine(backups,recovery.Backup));
        using var original=await FromZip(file,recovery.Id);
        using var current=await Capture(folder,recovery.Id);
        await current.Backup(backups,$"{recovery.Id}-before-recovery-{Guid.NewGuid():N}.zip");
        await Replace(folder,original,current);
        using var verify=await Capture(folder,recovery.Id);
        if(verify.Info.Hash!=original.Info.Hash) throw new IOException("Recovery did not verify. Keep the backup folder and do not launch this game yet.");
        File.Delete(journal);
    }
    public static async Task Apply(IGameFolder folder,string id,SaveBundle incoming,string backups,Action<string>? progress=null)
    {
        SafePath.GameId(id);
        if(incoming.Id!=id) throw new InvalidDataException("The download contains unexpected save files.");
        if(incoming.Info.Hash is null || incoming.Info.Suspicious) throw new InvalidDataException("Empty saves will not replace existing progress.");
        using var before=await Capture(folder,id,progress);
        Directory.CreateDirectory(backups);
        if(RecoveryNeeded(backups)) throw new IOException("An interrupted sync needs recovery before another save can be replaced.");
        var name=$"{id}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip"; var backup=Path.Combine(backups,name);
        await before.Backup(backups,name);
        using(var record=new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new Recovery(id,name)))) await new LocalFolder(backups).Write(Journal,record);
        try
        {
            await Replace(folder,incoming,before,progress);
            using var verify=await Capture(folder,id,progress);
            if(verify.Info.Hash!=incoming.Info.Hash) throw new IOException("Save verification failed.");
            File.Delete(Path.Combine(backups,Journal));
        }
        catch(Exception error)
        {
            try
            {
                await Replace(folder,before,incoming);
                using var verify=await Capture(folder,id);
                if(verify.Info.Hash!=before.Info.Hash) throw new IOException("Backup restore did not verify.");
                File.Delete(Path.Combine(backups,Journal));
            }
            catch(Exception rollback) { throw new IOException($"Save restore failed. Your original backup is at {backup}.",new AggregateException(error,rollback)); }
            throw new IOException("Sync failed; your original saves were restored. "+error.Message,error);
        }
    }
    public static async Task CopyLimited(Stream input,Stream output,long limit,CancellationToken token=default,Action<long>? progress=null)
    {
        var buffer=ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            long total=0; int count;
            while((count=await input.ReadAsync(buffer.AsMemory(0,65536),token))>0)
            {
                if((total+=count)>limit) throw new InvalidDataException("Transfer exceeds the safety limit.");
                await output.WriteAsync(buffer.AsMemory(0,count),token); progress?.Invoke(total);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
