using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EmuSync.Core;

static class DriveChecks
{
    public static async Task<int> Run(byte[] iso,string root)
    {
        var checks=0;
        void Check(bool condition,string name) { if(!condition) throw new Exception(name); checks++; }
        async Task Reject(Func<Task> action,string name) { try { await action(); } catch(IOException) { checks++; return; } throw new Exception(name); }
        var server=new DriveServer();
        DriveClient Client(Func<IReadOnlyList<DriveVersion>,Task<string?>>? choose=null)=>new(new HttpClient(server,false),"Test device",choose??(_=>Task.FromResult<string?>(null)));
        var first=new SaveBundle(); first.Files["SAVEDATA/ULUS12345/DATA.BIN"]=[1,2,3];
        var second=new SaveBundle(); second.Files["SAVEDATA/ULUS12345/DATA.BIN"]=[4,5,6];
        using var a=Client(); using var b=Client();
        Check((await a.Account()).Id=="test-account","Drive account identity");
        Check((await a.Describe("ULUS12345")).Info.Hash is null,"Empty Drive is a new save");
        await a.UploadSave("ULUS12345",first,null);
        Check(server.Items.Count==1,"Initial cloud upload creates one immutable snapshot");
        Check((await b.Describe("ULUS12345")).Info.Hash==first.Info.Hash,"Second device sees the cloud save");
        Check((await b.DownloadSave("ULUS12345",first.Info.Hash!)).Info.Hash==first.Info.Hash,"Cloud download verifies content");
        await a.Describe("ULUS12345"); await a.UploadSave("ULUS12345",second,first.Info.Hash);
        await Reject(()=>b.UploadSave("ULUS12345",first,first.Info.Hash),"Stale Drive update accepted");
        Check(server.Items.Count==2,"Stale upload preserves cloud history");
        server.HideNewListing=true;
        await a.Describe("ULUS12345");
        await a.UploadSave("ULUS12345",first,second.Info.Hash);
        Check(server.Items.Count==3,"Confirmed upload remains valid while Drive search indexing lags");
        server.HideNewListing=false;
        await a.Describe("ULUS12345");
        await a.UploadSave("ULUS12345",second,first.Info.Hash);
        await b.Describe("ULUS12345");
        server.Race=first;
        await b.UploadSave("ULUS12345",first,second.Info.Hash);
        Check(server.Items.Count==6,"Racing uploads keep both copies");
        var asked=false;
        using var resolving=Client(versions=> { asked=true; return Task.FromResult<string?>(versions.Single(v=>v.Hash==first.Info.Hash).Id); });
        var info=await resolving.Describe("ULUS12345");
        // Both racing saves are identical, so no choice is needed and a later save joins both parents.
        Check(!asked && !resolving.HasBranches && info.Info.Hash==first.Info.Hash,"Identical concurrent progress is safe");
        await resolving.UploadSave("ULUS12345",second,first.Info.Hash);
        Check((await a.Describe("ULUS12345")).Info.Hash==second.Info.Hash,"Later upload joins identical branches");
        server.Race=first;
        await a.Describe("ULUS12345");
        await Reject(()=>a.UploadSave("ULUS12345",second,second.Info.Hash),"Different concurrent saves hidden");
        await Reject(()=>a.Describe("ULUS12345"),"Cancelled Drive conflict accepted");
        using var branch=Client(versions=> { asked=true; return Task.FromResult<string?>(versions.First(v=>v.Hash==first.Info.Hash).Id); });
        asked=false; var branched=await branch.Describe("ULUS12345");
        Check(asked && branch.HasBranches && branched.Info.Hash==first.Info.Hash,"Different concurrent saves require a choice");
        var chosen=await branch.DownloadSave("ULUS12345",branched.Info.Hash!);
        await branch.UploadSave("ULUS12345",chosen,branched.Info.Hash);
        Check((await a.Describe("ULUS12345")).Info.Hash==first.Info.Hash,"Explicit conflict resolution joins all branches");
        server.CorruptDownload=true;
        await Reject(()=>a.DownloadSave("ULUS12345",first.Info.Hash!),"Tampered cloud ZIP accepted");
        server.CorruptDownload=false;
        server.BadLocation=true;
        await Reject(()=>a.UploadSave("ULUS12345",second,first.Info.Hash),"Untrusted resumable upload URL accepted");
        Check(!server.ForeignRequest,"Upload URL cannot leak the bearer token to another host");
        server.BadLocation=false;
        var large=iso.Concat(new byte[5*1024*1024]).ToArray();
        var game=await IsoReader.Read(new MemoryStream(large),"test.iso");
        var chunks=server.Chunks;
        await a.UploadGame(game,new MemoryStream(large),_=>{});
        Check(server.Chunks-chunks==2,"Large ISO uploads in bounded resumable chunks");
        Check((await b.Games()).Single().Id==game.Id,"Other device browses optional Drive games");
        var cache=Path.Combine(root,"drive-cache");
        var downloaded=await b.DownloadGame(game,cache,_=>{});
        Check((await File.ReadAllBytesAsync(downloaded.Path)).SequenceEqual(large),"Drive game download verifies and preserves ISO bytes");
        File.Delete(downloaded.Path);
        var count=server.Items.Count;
        await a.UploadGame(game,new MemoryStream(large),_=>{});
        Check(server.Items.Count==count,"Identical ISO is not uploaded twice");
        server.CorruptDownload=true;
        await Reject(()=>b.DownloadGame(game,cache,_=>{}),"Corrupt game download accepted");
        Check(Directory.GetFiles(cache).Length==0,"Failed game download leaves no launchable partial ISO");
        server.CorruptDownload=false;
        server.Fail=true;
        await Reject(()=>a.Describe("ULUS12345"),"Expired cloud authorization accepted");
        checks+=await CleanupChecks(iso);
        return checks;
    }

    static async Task<int> CleanupChecks(byte[] iso)
    {
        var checks=0;
        void Check(bool condition,string name) { if(!condition) throw new Exception(name); checks++; }
        async Task Reject(Func<Task> action,string name) { try { await action(); } catch(IOException) { checks++; return; } throw new Exception(name); }
        var server=new DriveServer();
        using var drive=new DriveClient(new HttpClient(server,false),"Cleanup test",versions=>Task.FromResult<string?>(versions[0].Id));
        var copies=new List<string>();
        SaveBundle Save(byte value,string id="ULUS12345") { var save=new SaveBundle(); save.Files[$"SAVEDATA/{id}/DATA.BIN"]=[value]; return save; }
        var latest=Save(6);
        for(byte i=1;i<=6;i++)
        {
            var before=await drive.Describe("ULUS12345");
            await drive.UploadSave("ULUS12345",Save(i),before.Info.Hash);
            copies.Add(server.Items.Last().Id);
        }
        await drive.Describe("ULUS99999"); await drive.UploadSave("ULUS99999",Save(9,"ULUS99999"),null);
        var other=server.Items.Last().Id;
        var game=await IsoReader.Read(new MemoryStream(iso),"test.iso");
        await drive.UploadGame(game,new MemoryStream(iso),_=>{});
        var gameFile=server.Items.Last().Id;
        await drive.Describe("ULUS12345");
        server.CorruptDownload=true;
        await Reject(()=>drive.CleanupSave("ULUS12345",latest.Info.Hash),"Cleanup accepted an unverified cloud save");
        Check(server.Deleted.Count==0,"A corrupt latest save prevents all cleanup");
        server.CorruptDownload=false; server.FailDeleteAfter=1;
        await Reject(()=>drive.CleanupSave("ULUS12345",latest.Info.Hash),"Cleanup deletion failure was ignored");
        Check(server.Deleted.SequenceEqual(copies.Take(1)),"Cleanup deletes the oldest ancestor first");
        Check((await drive.Describe("ULUS12345")).Info.Hash==latest.Info.Hash && !drive.HasBranches,"Interrupted cleanup does not resurrect older conflict heads");
        server.FailDeleteAfter=null;
        await drive.CleanupSave("ULUS12345",latest.Info.Hash);
        Check(server.Items.Where(i=>copies.Contains(i.Id)).Select(i=>i.Id).ToHashSet().SetEquals(copies.TakeLast(3)),"Cleanup keeps the latest save and exactly two older backups");
        Check(server.Items.Any(i=>i.Id==other) && server.Items.Any(i=>i.Id==gameFile),"Cleanup preserves other games and uploaded ISOs");
        Check((await drive.DownloadSave("ULUS12345",latest.Info.Hash!)).Info.Hash==latest.Info.Hash,"Retained latest save remains downloadable and verified");
        var deleted=server.Deleted.Count;
        await drive.CleanupSave("ULUS12345",latest.Info.Hash);
        Check(server.Deleted.Count==deleted,"Repeated sync cleanup is a no-op with three copies");
        server.Race=Save(7);
        await Reject(()=>drive.UploadSave("ULUS12345",Save(8),latest.Info.Hash),"Conflicting race accepted");
        await drive.CleanupSave("ULUS12345",latest.Info.Hash);
        Check(server.Deleted.Count==deleted,"Unresolved conflicting branches prevent cleanup");
        var chosen=await drive.Describe("ULUS12345");
        var resolved=await drive.DownloadSave("ULUS12345",chosen.Info.Hash!);
        await drive.UploadSave("ULUS12345",resolved,chosen.Info.Hash);
        await drive.CleanupSave("ULUS12345",resolved.Info.Hash);
        Check(server.Items.Count-2==3 && (await drive.Describe("ULUS12345")).Info.Hash==resolved.Info.Hash && !drive.HasBranches,"Resolved branches prune to three without creating a false conflict");
        await drive.UploadSave("ULUS12345",Save(10),resolved.Info.Hash);
        deleted=server.Deleted.Count;
        server.RaceOnDownload=Save(11);
        await drive.CleanupSave("ULUS12345",Save(10).Info.Hash);
        Check(server.Deleted.Count==deleted,"A new cloud head during verification cancels cleanup");
        await drive.Describe("ULUS12345");
        server.Race=Save(12);
        await drive.UploadSave("ULUS12345",Save(12),Save(11).Info.Hash);
        await drive.CleanupSave("ULUS12345",Save(12).Info.Hash);
        Check(server.Items.Count-2==3 && (await drive.Describe("ULUS12345")).Info.Hash==Save(12).Info.Hash,"Matching concurrent snapshots join safely before pruning to three");
        foreach(var (id,parent) in new[]{("drive-cycle-0001","drive-cycle-0002"),("drive-cycle-0002","drive-cycle-0001")})
        {
            var metadata=new {appProperties=new {emusync="save-v1",game="ULUS12345",hash=Save(12).Info.Hash},description=JsonSerializer.Serialize(new {parents=new[]{parent},device="Invalid history",files=1,bytes=1,suspicious=false})};
            server.Items.Add((id,JsonSerializer.SerializeToElement(metadata),Save(12).Zip()));
        }
        deleted=server.Deleted.Count;
        await Reject(()=>drive.CleanupSave("ULUS12345",Save(12).Info.Hash),"Cyclic history accepted for deletion");
        Check(server.Deleted.Count==deleted,"Invalid ancestry is detected before any deletion");
        return checks;
    }

    sealed class DriveServer : HttpMessageHandler
    {
        public List<(string Id,JsonElement Metadata,byte[] Zip)> Items { get; }=[];
        public SaveBundle? Race,RaceOnDownload;
        public List<string> Deleted { get; }=[];
        public int? FailDeleteAfter;
        public bool CorruptDownload,Fail,BadLocation,ForeignRequest,HideNewListing;
        bool hideNext;
        public int Chunks;
        readonly Dictionary<string,(JsonElement Metadata,MemoryStream Data)> uploads=[];
        int next;
        static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(Fail) return new(HttpStatusCode.Unauthorized);
            if(request.RequestUri!.Host!="www.googleapis.com") { ForeignRequest=true; throw new IOException("Foreign request"); }
            var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Delete)
            {
                if(FailDeleteAfter is int limit && Deleted.Count>=limit) return new(HttpStatusCode.Forbidden);
                var index=Items.FindIndex(i=>path.EndsWith('/'+i.Id));
                if(index<0) return new(HttpStatusCode.NotFound);
                if(Items[index].Metadata.GetProperty("appProperties").GetProperty("emusync").GetString()!="save-v1") throw new Exception("Cleanup tried to delete a game ISO");
                Deleted.Add(Items[index].Id); Items.RemoveAt(index);
                return new(HttpStatusCode.NoContent);
            }
            if(path.EndsWith("/about")) return Reply(new {user=new {permissionId="test-account",emailAddress="test@example.invalid"}});
            if(request.Method==HttpMethod.Post)
            {
                var metadata=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)).RootElement.Clone();
                var session=Id(); uploads.Add(session,(metadata,new MemoryStream()));
                var response=new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.Location=new Uri(BadLocation?"https://attacker.invalid/upload":"https://www.googleapis.com/upload/drive/v3/files?upload_id="+session);
                return response;
            }
            if(request.Method==HttpMethod.Put)
            {
                Chunks++;
                var session=request.RequestUri.Query.Split('=')[1];
                var upload=uploads[session]; var metadata=upload.Metadata;
                var range=request.Content!.Headers.ContentRange!;
                if(range.From!=upload.Data.Length) throw new IOException("Incorrect resumable offset");
                await request.Content.CopyToAsync(upload.Data,token);
                if(upload.Data.Length<range.Length)
                {
                    var incomplete=new HttpResponseMessage((HttpStatusCode)308);
                    incomplete.Headers.TryAddWithoutValidation("Range",$"bytes=0-{upload.Data.Length-1}"); return incomplete;
                }
                var bytes=upload.Data.ToArray(); upload.Data.Dispose(); uploads.Remove(session);
                if(Race is not null)
                {
                    var racing=Race; Race=null;
                    var details=JsonDocument.Parse(metadata.GetProperty("description").GetString()!);
                    var changed=new {
                        appProperties=new {emusync="save-v1",game="ULUS12345",hash=racing.Info.Hash},
                        description=JsonSerializer.Serialize(new {parents=details.RootElement.GetProperty("parents"),device="Other device",files=racing.Info.Files,bytes=racing.Info.Bytes,suspicious=false})
                    };
                    Items.Add((Id(),JsonSerializer.SerializeToElement(changed),racing.Zip()));
                }
                var id=Id(); Items.Add((id,metadata,bytes)); hideNext=HideNewListing; return Reply(new {id});
            }
            if(path=="/drive/v3/files")
            {
                var query=Uri.UnescapeDataString(request.RequestUri.Query);
                if(query.Contains("folder-v1")) return Reply(new {files=new[]{new {id="drive-folder-0001",appProperties=new {emusync="folder-v1"}}}});
                var kind=query.Contains("game-v1")?"game-v1":"save-v1";
                var listed=hideNext?Items.Take(Items.Count-1).ToArray():Items.ToArray(); hideNext=false;
                var wanted=System.Text.RegularExpressions.Regex.Match(query,"key='game' and value='([^']+)'");
                return Reply(new {files=listed.Where(i=>i.Metadata.GetProperty("appProperties").GetProperty("emusync").GetString()==kind && (!wanted.Success || i.Metadata.GetProperty("appProperties").GetProperty("game").GetString()==wanted.Groups[1].Value)).Select(i=>new {id=i.Id,size=i.Zip.LongLength,description=i.Metadata.GetProperty("description").GetString(),appProperties=i.Metadata.GetProperty("appProperties"),createdTime=DateTimeOffset.UnixEpoch.AddSeconds(int.Parse(i.Id.Split('-')[^1]))})});
            }
            var item=Items.Single(i=>path.EndsWith('/'+i.Id));
            if(RaceOnDownload is { } racingDownload)
            {
                RaceOnDownload=null;
                var metadata=new {appProperties=new {emusync="save-v1",game="ULUS12345",hash=racingDownload.Info.Hash},description=JsonSerializer.Serialize(new {parents=new[]{item.Id},device="Other device",files=racingDownload.Info.Files,bytes=racingDownload.Info.Bytes,suspicious=false})};
                Items.Add((Id(),JsonSerializer.SerializeToElement(metadata),racingDownload.Zip()));
            }
            return new(HttpStatusCode.OK) {Content=new ByteArrayContent(CorruptDownload?new SaveBundle().Zip():item.Zip)};
        }
        string Id()=>"drive-file-"+(++next).ToString("D6");
        static HttpResponseMessage Reply(object value)=>new(HttpStatusCode.OK) {Content=JsonContent.Create(value,options:Json)};
    }
}
