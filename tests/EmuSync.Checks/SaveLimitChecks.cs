using System.Buffers.Binary;
using System.IO.Compression;
using EmuSync.Core;

static class SaveLimitChecks
{
    public static async Task<int> Run(string root)
    {
        var checks=0;
        void Check(bool condition,string name) { if(!condition) throw new Exception(name); checks++; }
        async Task Reject(Func<Task> action,string message)
        {
            try { await action(); }
            catch(InvalidDataException error) when(error.Message.Contains(message,StringComparison.Ordinal)) { checks++; return; }
            throw new Exception("Expected a save limit error containing: "+message);
        }
        var directory=Path.Combine(root,"large-save","SAVEDATA","ULUS12345");
        Directory.CreateDirectory(directory);
        const int size=129*1024*1024;
        using(var file=File.Create(Path.Combine(directory,"INSTALL.BIN"))) file.SetLength(size);
        var captured=await SaveBundle.Capture(new LocalFolder(Path.Combine(root,"large-save")),"ULUS12345");
        Check(captured.Info.Bytes==size,"Save capture accepts real data above the former 128 MB limit");
        var zip=captured.Zip();
        var restored=SaveBundle.FromZip(zip,"ULUS12345");
        Check(restored.Info.Bytes==size && restored.Info.Hash==captured.Info.Hash,"Larger save archives decompress and verify correctly");
        await Reject(()=>SaveBundle.Capture(new DeclaredFolder((long)SaveBundle.Limit+1,1),"ULUS12345"),"1 GB");
        await Reject(()=>SaveBundle.Capture(new DeclaredFolder(0,10001),"ULUS12345"),"10,000 files");
        using var forged=new MemoryStream();
        using(var archive=new ZipArchive(forged,ZipArchiveMode.Create,true))
        { using var entry=archive.CreateEntry("SAVEDATA/ULUS12345/TOO_BIG.BIN").Open(); entry.WriteByte(1); }
        var bytes=forged.ToArray();
        var central=-1;
        for(var offset=0;offset<=bytes.Length-46;offset++)
            if(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset))==0x02014b50) { central=offset; break; }
        if(central<0) throw new Exception("Missing ZIP fixture directory.");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central+24),(uint)SaveBundle.Limit+1);
        await Reject(()=>Task.FromResult(SaveBundle.FromZip(bytes,"ULUS12345")),"expanded save");
        return checks;
    }
    sealed class DeclaredFolder(long size,int count) : IGameFolder
    {
        public Task<IReadOnlyList<FolderEntry>> List(string path="")=>Task.FromResult<IReadOnlyList<FolderEntry>>(
            path=="SAVEDATA"?[new("ULUS12345",true,0)]:Enumerable.Range(0,count).Select(i=>new FolderEntry($"FILE{i:D5}.BIN",false,size)).ToArray());
        public Task<Stream> Read(string path)=>Task.FromResult<Stream>(new MemoryStream());
        public Task Write(string path,Stream data)=>throw new NotSupportedException();
        public Task Delete(string path)=>throw new NotSupportedException();
    }
}
