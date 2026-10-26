using System.Buffers.Binary;
using System.IO.Compression;
using EmuSync.Core;
using EmuSync.App;

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
        using(var file=File.Create(Path.Combine(directory,"INSTALL.BIN")))
        {
            var block=new byte[256*1024]; new Random(47).NextBytes(block);
            for(var offset=0;offset<size;offset+=block.Length) file.Write(block,0,Math.Min(block.Length,size-offset));
        }
        var memory=GC.GetTotalAllocatedBytes(true);
        var updates=new List<string>();
        using var captured=await SaveBundle.Capture(new LocalFolder(Path.Combine(root,"large-save")),"ULUS12345",updates.Add);
        Check(captured.Info.Bytes==size,"Save capture accepts real data above the former 128 MB limit");
        using var zip=captured.OpenZip();
        Check(zip.Length>128L*1024*1024,"Large test ZIP is incompressible and exceeds 128 MB on disk");
        using var restored=await SaveBundle.FromZip(zip,"ULUS12345",updates.Add,zip.Length);
        Check(restored.Info.Bytes==size && restored.Info.Hash==captured.Info.Hash,"Larger save archives decompress and verify correctly");
        var allocated=GC.GetTotalAllocatedBytes(true)-memory;
        Check(allocated<32L*1024*1024,"Large capture and decode allocate less than 32 MB of managed memory");
        Console.WriteLine($"Streaming 129 MB capture/decode: {allocated/1024.0/1024:F1} MB managed allocations.");
        Check(updates.Any(s=>s=="Reading save · 100%") && updates.Any(s=>s=="Verifying save · 100%"),"Large save stages report completed progress");
        var games=new LocalFolder(Path.Combine(root,"large-games"));
        await using(var host=new WifiHost())
        {
            await host.Start(new LocalFolder(Path.Combine(root,"large-save")),games,()=>[new Game("ULUS12345","Large test","test.iso",4,null,null)],()=>false,Path.Combine(root,"large-host"));
            using var client=new WifiClient(new Pairing("127.0.0.1",WifiHost.Port,host.CertificateHash!,host.Key!));
            memory=GC.GetTotalAllocatedBytes(true);
            using var received=await client.DownloadSave("ULUS12345",captured.Info.Hash!,updates.Add);
            await client.UploadSave("ULUS12345",received,received.Info.Hash,updates.Add);
            Check(received.Info.Hash==captured.Info.Hash,"129 MB save streams over pinned TLS in both directions and verifies after apply");
            Check(GC.GetTotalAllocatedBytes(true)-memory<96L*1024*1024,"Large Wi-Fi download/upload and server backup/apply use bounded managed allocations");
            Check(updates.Any(s=>s=="Uploading save · 100%"),"Wi-Fi uploads report byte progress");
        }
        var limitRoot=Path.Combine(root,"limit-save");
        Directory.CreateDirectory(Path.Combine(limitRoot,"SAVEDATA","ULUS12346"));
        using(var file=File.Create(Path.Combine(limitRoot,"SAVEDATA","ULUS12346","DATA.BIN"))) file.SetLength(SaveBundle.Limit);
        using(var atLimit=await SaveBundle.Capture(new LocalFolder(limitRoot),"ULUS12346"))
        using(var archive=atLimit.OpenZip())
        using(var verified=await SaveBundle.FromZip(archive,"ULUS12346"))
            Check(atLimit.Info.Bytes==SaveBundle.Limit && verified.Info.Hash==atLimit.Info.Hash,"A full 1 GB synthetic save captures and verifies without whole-save arrays");
        using(var input=captured.OpenZip())
        {
            var scratch=Path.GetDirectoryName(((FileStream)input).Name)!;
            var count=Directory.GetFiles(scratch,"save-*.tmp").Length;
            using var cancelled=new CancellationTokenSource();
            try { using var unexpected=await SaveBundle.FromZip(input,"ULUS12345",_=>cancelled.Cancel(),input.Length,cancelled.Token); throw new Exception("Cancelled download accepted"); }
            catch(OperationCanceledException) { checks++; }
            Check(Directory.GetFiles(scratch,"save-*.tmp").Length==count,"Cancelled staging removes its temporary ZIP and leaves live snapshots intact");
        }
        await Reject(()=>SaveFixtures.Capture(new DeclaredFolder((long)SaveBundle.Limit+1,1),"ULUS12345"),"1 GB");
        await Reject(()=>SaveFixtures.Capture(new DeclaredFolder(0,10001),"ULUS12345"),"10,000 files");
        using var forged=new MemoryStream();
        using(var archive=new ZipArchive(forged,ZipArchiveMode.Create,true))
        { using var entry=archive.CreateEntry("SAVEDATA/ULUS12345/TOO_BIG.BIN").Open(); entry.WriteByte(1); }
        var bytes=forged.ToArray();
        var central=-1;
        for(var offset=0;offset<=bytes.Length-46;offset++)
            if(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset))==0x02014b50) { central=offset; break; }
        if(central<0) throw new Exception("Missing ZIP fixture directory.");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central+24),(uint)SaveBundle.Limit+1);
        await Reject(()=>SaveFixtures.Decode(bytes,"ULUS12345"),"expanded save");
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
