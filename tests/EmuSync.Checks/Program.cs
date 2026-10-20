using EmuSync.Core;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using EmuSync.App;

int checks=0;
void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; }
void Reject(Action action) { try { action(); } catch(Exception e) when(e is InvalidDataException or FormatException) { checks++; return; } throw new Exception("Unsafe input accepted."); }
Check(SyncPlan.Decide("A","B","A",false)==SyncAction.Download,"Remote-only progress");
Check(SyncPlan.Decide("B","A","A",false)==SyncAction.Upload,"Local-only progress");
Check(SyncPlan.Decide("B","C","A",false)==SyncAction.Conflict,"Both changed");
Check(SyncPlan.Decide("B","C",null,false)==SyncAction.Conflict,"First sync never guesses");
Check(SyncPlan.Decide(null,"A","A",false)==SyncAction.Suspicious,"Missing save");
Check(SyncPlan.Decide("A",null,"A",false)==SyncAction.Suspicious,"Missing PC save");
Check(SyncPlan.Decide(null,"A",null,false)==SyncAction.Download,"New device");
Check(SyncPlan.Decide("A","A","A",true)==SyncAction.Suspicious,"Empty files");
Reject(()=>SafePath.Validate("../outside")); Reject(()=>SafePath.Validate("SAVEDATA/x/C:bad"));
Reject(()=>SafePath.GameId("../bad")); Reject(()=>IsoReader.ReadSfo(new byte[21]));
var pair=new Pairing("192.168.1.5",42387,new string('A',64),new string('B',64));
Check(Pairing.Parse(pair.Encode())==pair,"Pairing roundtrip");
Reject(()=>Pairing.Parse(new Pairing("8.8.8.8",42387,pair.Certificate,pair.Key).Encode()));
var root=Path.Combine(Path.GetTempPath(),"emusync-check-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    checks+=await LinkedSaveChecks.Run(root);
    var folder=new LocalFolder(Path.Combine(root,"PSP"));
    await folder.Write("SAVEDATA/ULUS12345/DATA.BIN",new MemoryStream([1,2,3]));
    await folder.Write("SAVEDATA/ULUS99999/DATA.BIN",new MemoryStream([9]));
    var before=await SaveBundle.Capture(folder,"ULUS12345");
    Check(before.Info.Files==1,"Only this game's saves");
    var after=SaveBundle.FromZip(before.Zip(),"ULUS12345");
    after.Files["SAVEDATA/ULUS12345/DATA.BIN"]=[4,5,6];
    await SaveBundle.Apply(folder,"ULUS12345",after,Path.Combine(root,"backups"));
    Check((await SaveBundle.Capture(folder,"ULUS12345")).Info.Hash==after.Info.Hash,"Verified replacement");
    Check(Directory.GetFiles(Path.Combine(root,"backups")).Length==1,"Backup exists");
    Check((await SaveBundle.Capture(folder,"ULUS99999")).Files.Values.Single()[0]==9,"Other game's save untouched");
    await SaveBundle.Apply(folder,"ULUS12345",before,Path.Combine(root,"backups"));
    Check((await SaveBundle.Capture(folder,"ULUS12345")).Info.Hash==before.Info.Hash,"Restore works");
    var recoveryDir=Path.Combine(root,"recovery"); Directory.CreateDirectory(recoveryDir);
    await File.WriteAllBytesAsync(Path.Combine(recoveryDir,"ULUS12345-original.zip"),before.Zip());
    await File.WriteAllTextAsync(Path.Combine(recoveryDir,"pending-save.json"),"{\"Id\":\"ULUS12345\",\"Backup\":\"ULUS12345-original.zip\"}");
    await folder.Write("SAVEDATA/ULUS12345/DATA.BIN",new MemoryStream([7,8]));
    await folder.Write("SAVEDATA/ULUS12345/PARTIAL.BIN",new MemoryStream([1]));
    Check(SaveBundle.RecoveryNeeded(recoveryDir),"Interrupted transaction detected");
    await SaveBundle.Recover(folder,recoveryDir);
    Check(!SaveBundle.RecoveryNeeded(recoveryDir) && (await SaveBundle.Capture(folder,"ULUS12345")).Info.Hash==before.Info.Hash,"Interrupted sync restored original save");
    var broken=new FailingFolder(folder);
    var extra=SaveBundle.FromZip(after.Zip(),"ULUS12345"); extra.Files["SAVEDATA/ULUS12345/NEW.BIN"]=[1];
    try { await SaveBundle.Apply(broken,"ULUS12345",extra,Path.Combine(root,"rollback")); throw new Exception("Failed write accepted"); }
    catch(IOException) { checks++; }
    Check((await SaveBundle.Capture(folder,"ULUS12345")).Info.Hash==before.Info.Hash,"Write failure rolled back all files");
    var beforeHash=before.Info.Hash;
    await using(var host=new WifiHost())
    {
        var busy=false;
        await File.WriteAllBytesAsync(Path.Combine(root,"test.iso"),[1,2,3,4]);
        await host.Start(folder,new LocalFolder(root),()=>[new Game("ULUS12345","Test","test.iso",4,null,null)],()=>busy,Path.Combine(root,"host"));
        var pairing=new Pairing("127.0.0.1",WifiHost.Port,host.CertificateHash!,host.Key!);
        using var client=new WifiClient(pairing);
        Check((await client.Games()).Count==1,"Authenticated TLS library");
        Check((await client.Describe("ULUS12345")).Info.Hash==beforeHash,"Remote save metadata");
        Check((await client.DownloadSave("ULUS12345",beforeHash!)).Info.Hash==beforeHash,"Verified save transfer");
        await client.UploadSave("ULUS12345",after,beforeHash);
        Check((await SaveBundle.Capture(folder,"ULUS12345")).Info.Hash==after.Info.Hash,"TLS upload applied");
        try { await client.UploadSave("ULUS12345",before,beforeHash); throw new Exception("Stale update accepted"); }
        catch(IOException) { checks++; }
        Check((await SaveBundle.Capture(folder,"ULUS12345")).Info.Hash==after.Info.Hash,"Stale upload did not overwrite progress");
        busy=true;
        try { await client.Describe("ULUS12345"); throw new Exception("Running emulator not protected"); } catch(IOException) { checks++; }
        busy=false;
        using var unpaired=new WifiClient(pairing with {Key=new string('0',64)});
        try { await unpaired.Games(); throw new Exception("Unpaired client accepted"); } catch(IOException) { checks++; }
        using var impostor=new WifiClient(pairing with {Certificate=new string('0',64)});
        try { await impostor.Games(); throw new Exception("Untrusted server accepted"); } catch(HttpRequestException) { checks++; }
        var download=await client.DownloadGame(new Game("ULUS12345","Test","test.iso",4,null,null),Path.Combine(root,"cache"),_=>{});
        Check((await File.ReadAllBytesAsync(download.Path)).SequenceEqual(new byte[]{1,2,3,4}),"Verified game transfer");
    }
    Reject(()=>SaveBundle.FromZip(before.Zip(),"ULUS99999"));
    using var bad=new MemoryStream();
    using(var zip=new ZipArchive(bad,ZipArchiveMode.Create,true)) { using var entry=zip.CreateEntry("../outside").Open(); entry.WriteByte(1); }
    Reject(()=>SaveBundle.FromZip(bad.ToArray(),"ULUS12345"));
    // A synthetic ISO verifies actual directory traversal and SFO parsing, with no commercial game fixture.
    var iso=new byte[24*2048];
    void Both32(int at,uint value) { BinaryPrimitives.WriteUInt32LittleEndian(iso.AsSpan(at),value); BinaryPrimitives.WriteUInt32BigEndian(iso.AsSpan(at+4),value); }
    int Record(int at,uint sector,uint size,string name,bool directory)
    {
        var text=Encoding.ASCII.GetBytes(name); var length=33+text.Length+(text.Length%2==0?1:0);
        iso[at]=(byte)length; Both32(at+2,sector); Both32(at+10,size); iso[at+25]=(byte)(directory?2:0); iso[at+32]=(byte)text.Length; text.CopyTo(iso,at+33); return length;
    }
    var pvd=16*2048; iso[pvd]=1; Encoding.ASCII.GetBytes("CD001").CopyTo(iso,pvd+1); iso[pvd+6]=1;
    BinaryPrimitives.WriteUInt16LittleEndian(iso.AsSpan(pvd+128),2048);
    Record(pvd+156,20,2048,"\0",true); Record(20*2048,21,2048,"PSP_GAME",true);
    var sfo=new byte[128]; new byte[]{0,80,83,70}.CopyTo(sfo,0);
    void U32(int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(at),value);
    U32(8,52); U32(12,72); U32(16,2);
    BinaryPrimitives.WriteUInt16LittleEndian(sfo.AsSpan(22),0x0204); U32(24,10); U32(28,10); U32(32,0);
    BinaryPrimitives.WriteUInt16LittleEndian(sfo.AsSpan(36),8); BinaryPrimitives.WriteUInt16LittleEndian(sfo.AsSpan(38),0x0204); U32(40,10); U32(44,10); U32(48,12);
    Encoding.ASCII.GetBytes("DISC_ID\0TITLE\0").CopyTo(sfo,52); Encoding.ASCII.GetBytes("ULUS12345\0").CopyTo(sfo,72); Encoding.ASCII.GetBytes("Test Game\0").CopyTo(sfo,84);
    Record(21*2048,22,(uint)sfo.Length,"PARAM.SFO;1",false); sfo.CopyTo(iso,22*2048);
    var game=await IsoReader.Read(new MemoryStream(iso),"test.iso");
    Check(game.Id=="ULUS12345" && game.Title=="Test Game" && game.Icon is null,"ISO metadata and missing thumbnail fallback");
    var icon=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=");
    var firstRecord=iso[21*2048];
    Record(21*2048+firstRecord,23,(uint)icon.Length,"ICON0.PNG;1",false); icon.CopyTo(iso,23*2048);
    var illustrated=await IsoReader.Read(new MemoryStream(iso),"test.iso");
    checks+=await DriveChecks.Run(iso,root);
    Check(illustrated.Icon=="data:image/png;base64,"+Convert.ToBase64String(icon),"ISO thumbnail extracted from ICON0.PNG");
    BinaryPrimitives.WriteUInt32BigEndian(iso.AsSpan(23*2048+16),65535);
    Check((await IsoReader.Read(new MemoryStream(iso),"test.iso")).Icon is null,"Oversized thumbnail rejected");
    try { await IsoReader.Read(new MemoryStream(iso[..100]),"bad.iso"); throw new Exception("Truncated ISO accepted"); } catch(InvalidDataException) { checks++; }
}
finally
{
    var resolved=Path.GetFullPath(root);
    var tempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
    if(Path.GetDirectoryName(resolved)!=tempRoot || !Path.GetFileName(resolved).StartsWith("emusync-check-",StringComparison.Ordinal))
        throw new IOException("Refusing cleanup outside the test directory.");
    Directory.Delete(resolved,true);
}
Console.WriteLine($"PASS: {checks} checks (sync decisions, linked save folders, backups, ISO metadata, TLS pinning, authentication, stale-update protection, Drive history/conflicts and game transfer).");

sealed class FailingFolder(IGameFolder inner) : IGameFolder
{
    bool failed;
    public Task<IReadOnlyList<FolderEntry>> List(string path="")=>inner.List(path);
    public Task<Stream> Read(string path)=>inner.Read(path);
    public Task Delete(string path)=>inner.Delete(path);
    public Task Write(string path,Stream stream)
    {
        if(path.EndsWith("NEW.BIN") && !failed) { failed=true; throw new IOException("Simulated storage failure"); }
        return inner.Write(path,stream);
    }
}
