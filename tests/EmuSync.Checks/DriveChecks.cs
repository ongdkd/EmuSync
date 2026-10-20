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
        return checks;
    }

    sealed class DriveServer : HttpMessageHandler
    {
        public List<(string Id,JsonElement Metadata,byte[] Zip)> Items { get; }=[];
        public SaveBundle? Race;
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
                return Reply(new {files=listed.Where(i=>i.Metadata.GetProperty("appProperties").GetProperty("emusync").GetString()==kind).Select(i=>new {id=i.Id,size=i.Zip.LongLength,description=i.Metadata.GetProperty("description").GetString(),appProperties=i.Metadata.GetProperty("appProperties"),createdTime=DateTimeOffset.UtcNow})});
            }
            var item=Items.Single(i=>path.EndsWith('/'+i.Id));
            return new(HttpStatusCode.OK) {Content=new ByteArrayContent(CorruptDownload?new SaveBundle().Zip():item.Zip)};
        }
        string Id()=>"drive-file-"+(++next).ToString("D6");
        static HttpResponseMessage Reply(object value)=>new(HttpStatusCode.OK) {Content=JsonContent.Create(value,options:Json)};
    }
}
