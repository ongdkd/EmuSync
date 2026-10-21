using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmuSync.Core;

public record DriveVersion(string Id,string Hash,string[] Parents,string Device,DateTimeOffset Created,int Files,long Bytes,bool Suspicious);
public sealed partial class DriveClient : ISyncPeer
{
    public const string Scope="https://www.googleapis.com/auth/drive.file";
    readonly HttpClient client;
    readonly Func<IReadOnlyList<DriveVersion>,Task<string?>> choose;
    readonly string device;
    List<DriveVersion>? observed;
    DriveVersion? selected;
    string? gameId;
    string? folderId;
    public bool HasBranches=>observed?.Select(v=>v.Hash).Distinct().Count()>1;
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    record Page(string? NextPageToken,FileEntry[]? Files);
    record FileEntry(string Id,string? Description,DateTimeOffset CreatedTime,Dictionary<string,string> AppProperties,string? Name=null,[property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] long Size=0);
    record Details(string[] Parents,string Device,int Files,long Bytes,bool Suspicious);
    public DriveClient(string token,string device,Func<IReadOnlyList<DriveVersion>,Task<string?>> choose)
        :this(new HttpClient(new SocketsHttpHandler {AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(10)}) {Timeout=TimeSpan.FromMinutes(3)},device,choose)
        =>client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    public DriveClient(HttpClient client,string device,Func<IReadOnlyList<DriveVersion>,Task<string?>> choose)
    {
        this.client=client; this.choose=choose; this.device=device.Length>64?device[..64]:device;
        client.BaseAddress=new Uri("https://www.googleapis.com/");
    }
    public async Task<(string Id,string Email)> Account()
    {
        using var response=await client.GetAsync("drive/v3/about?fields=user(permissionId,emailAddress)");
        using var json=JsonDocument.Parse(await Bytes(response,64*1024));
        var user=json.RootElement.GetProperty("user");
        var id=user.GetProperty("permissionId").GetString();
        var email=user.GetProperty("emailAddress").GetString();
        if(string.IsNullOrWhiteSpace(id) || id.Length>200 || string.IsNullOrWhiteSpace(email) || email.Length>320)
            throw new IOException("Google Drive returned an invalid account. Connect again in Settings.");
        return (id,email);
    }
    public async Task<SaveDescriptor> Describe(string id)
    {
        gameId=SafePath.GameId(id);
        observed=Heads(await List(id)); selected=null;
        if(observed.Count==0) return new(new(null,0,0,false),[]);
        if(HasBranches)
        {
            var choice=await choose(observed);
            selected=observed.SingleOrDefault(v=>v.Id==choice)??throw new IOException("Drive conflict was cancelled. No save was replaced.");
        }
        else selected=observed[0];
        return new(new(selected.Hash,selected.Files,selected.Bytes,selected.Suspicious),[]);
    }
    public async Task<SaveBundle> DownloadSave(string id,string hash)
    {
        RequireObserved(id,hash);
        if(selected is null) throw new IOException("This game has no save on Google Drive.");
        using var response=await client.GetAsync($"drive/v3/files/{selected.Id}?alt=media",HttpCompletionOption.ResponseHeadersRead);
        var bundle=SaveBundle.FromZip(await Bytes(response,SaveBundle.ArchiveLimit),id);
        if(bundle.Info.Hash!=hash || bundle.Info.Files!=selected.Files || bundle.Info.Bytes!=selected.Bytes || bundle.Info.Suspicious!=selected.Suspicious)
            throw new IOException("The Drive save failed verification. Nothing was replaced.");
        return bundle;
    }
    public async Task UploadSave(string id,SaveBundle bundle,string? expected)
    {
        RequireObserved(id,expected);
        bundle=SaveBundle.FromZip(bundle.Zip(),id);
        var info=bundle.Info;
        if(info.Hash is null || info.Suspicious) throw new IOException("An empty or incomplete save will not be uploaded to Drive.");
        var heads=Heads(await List(id));
        if(!SameHeads(heads,observed!)) throw new IOException("Drive changed during sync. Both saves are safe. Press Play to check again.");
        // Immutable snapshots preserve simultaneous uploads; parent IDs make concurrent branches visible.
        var metadata=new {
            name=$"{id}-{Guid.NewGuid():N}.zip",parents=new[]{await Folder()},
            appProperties=new {emusync="save-v1",game=id,hash=info.Hash},
            description=JsonSerializer.Serialize(new Details(observed!.Select(v=>v.Id).ToArray(),device,info.Files,info.Bytes,info.Suspicious),Json)
        };
        using var data=new MemoryStream(bundle.Zip());
        var createdId=await Upload(metadata,data,data.Length,_=>{});
        var listed=await List(id);
        // Drive search may lag behind the confirmed upload; include the acknowledged snapshot before finding heads.
        if(!listed.Any(v=>v.Id==createdId)) listed.Add(new(createdId,info.Hash,observed!.Select(v=>v.Id).ToArray(),device,DateTimeOffset.UtcNow,info.Files,info.Bytes,info.Suspicious));
        var after=Heads(listed);
        if(after.Any(v=>v.Hash!=info.Hash))
            throw new IOException("Another device synced at the same time. Both Drive copies were kept. Press Play again to choose.");
        observed=after; selected=after[0];
    }
    void RequireObserved(string id,string? expected)
    {
        SafePath.GameId(id);
        if(gameId!=id || observed is null || selected?.Hash!=expected)
            throw new IOException("Check the current Drive save before transferring progress.");
    }
    async Task<List<DriveVersion>> List(string id)
    {
        SafePath.GameId(id);
        var query=$"trashed=false and appProperties has {{ key='emusync' and value='save-v1' }} and appProperties has {{ key='game' and value='{id}' }}";
        var result=new List<DriveVersion>();
        foreach(var file in await ListFiles(query))
        {
                if(!ValidId(file.Id) || file.Description is null || file.Description.Length>64000 || file.AppProperties is null ||
                    file.AppProperties.GetValueOrDefault("game")!=id || file.AppProperties.GetValueOrDefault("emusync")!="save-v1" ||
                    !Regex.IsMatch(file.AppProperties.GetValueOrDefault("hash")??"","^[A-F0-9]{64}$"))
                    throw new IOException("Drive save metadata looks invalid. Sync paused; no files were changed.");
                var data=JsonSerializer.Deserialize<Details>(file.Description,Json)??throw new IOException("Invalid Drive save history.");
                if(data.Parents is null || data.Parents.Length>128 || data.Parents.Any(p=>!ValidId(p) || p==file.Id) ||
                    data.Device is null || data.Device.Length>64 || data.Files is <1 or >10000 || data.Bytes is <0 or >SaveBundle.Limit)
                    throw new IOException("Invalid Drive save history. No save was replaced.");
                result.Add(new(file.Id,file.AppProperties["hash"],data.Parents,data.Device,file.CreatedTime,data.Files,data.Bytes,data.Suspicious));
        }
        return result;
    }
    async Task<List<FileEntry>> ListFiles(string query)
    {
        var result=new List<FileEntry>(); string? next=null;
        do
        {
            var uri="drive/v3/files?spaces=drive&pageSize=100&fields=nextPageToken,files(id,name,size,description,createdTime,appProperties)&q="+Uri.EscapeDataString(query);
            if(next is not null) uri+="&pageToken="+Uri.EscapeDataString(next);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            var page=JsonSerializer.Deserialize<Page>(await Bytes(response,1024*1024),Json)??throw new IOException("Invalid Drive listing.");
            var files=page.Files??[];
            if(files.Length>100 || files.Any(f=>!ValidId(f.Id))) throw new IOException("Invalid Drive listing.");
            result.AddRange(files);
            // shortcut: keep remote backups; add explicit archive management after 1,000 revisions per game.
            if(result.Count>1000 || result.Select(f=>f.Id).Distinct().Count()!=result.Count) throw new IOException("Drive history needs attention. No files were deleted.");
            next=page.NextPageToken;
            if(next?.Length>4096) throw new IOException("Invalid Drive page token.");
        } while(next is not null);
        return result;
    }
    static List<DriveVersion> Heads(List<DriveVersion> versions)
    {
        var parents=versions.SelectMany(v=>v.Parents).ToHashSet(StringComparer.Ordinal);
        var heads=versions.Where(v=>!parents.Contains(v.Id)).OrderBy(v=>v.Id,StringComparer.Ordinal).ToList();
        if(heads.Count>128 || versions.Count>0 && heads.Count==0) throw new IOException("Drive save history is inconsistent. No save was replaced.");
        return heads;
    }
    static bool SameHeads(List<DriveVersion> left,List<DriveVersion> right)=>left.Select(v=>(v.Id,v.Hash)).ToHashSet().SetEquals(right.Select(v=>(v.Id,v.Hash)));
    static bool ValidId(string? id)=>id is not null && Regex.IsMatch(id,"^[A-Za-z0-9_-]{10,200}$");
    static async Task<byte[]> Bytes(HttpResponseMessage response,int limit)
    {
        Ensure(response);
        if(response.Content.Headers.ContentLength>limit) throw new IOException("Drive response exceeds the safety limit.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var input=await response.Content.ReadAsStreamAsync(timeout.Token); using var output=new MemoryStream();
        await SaveBundle.CopyLimited(input,output,limit,timeout.Token); return output.ToArray();
    }
    static void Ensure(HttpResponseMessage response)
    {
        if(!response.IsSuccessStatusCode) throw new IOException(response.StatusCode switch {
            HttpStatusCode.Unauthorized=>"Google sign-in expired. Reconnect Google Drive in Settings.",
            HttpStatusCode.Forbidden=>"Google Drive denied access or storage is full. Check your Google account and reconnect in Settings.",
            HttpStatusCode.TooManyRequests=>"Google Drive is busy. Your local save is safe; try again shortly.",
            _=>$"Google Drive could not complete sync ({(int)response.StatusCode}). Your local progress is safe."
        });
    }
    public void Dispose()=>client.Dispose();
}
