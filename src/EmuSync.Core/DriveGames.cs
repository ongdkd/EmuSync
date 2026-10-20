using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmuSync.Core;

public sealed partial class DriveClient
{
    async Task<string> Folder()
    {
        if(folderId is not null) return folderId;
        var folders=await ListFiles("trashed=false and mimeType='application/vnd.google-apps.folder' and appProperties has { key='emusync' and value='folder-v1' }");
        if(folders.Count>0) return folderId=folders.OrderBy(f=>f.Id,StringComparer.Ordinal).First().Id;
        using var response=await client.PostAsJsonAsync("drive/v3/files?fields=id",new {name="EmuSync",mimeType="application/vnd.google-apps.folder",appProperties=new {emusync="folder-v1"}});
        using var json=JsonDocument.Parse(await Bytes(response,64000));
        var id=json.RootElement.GetProperty("id").GetString();
        if(!ValidId(id)) throw new IOException("Drive did not create the EmuSync folder.");
        return folderId=id!;
    }
    async Task<List<FileEntry>> GameFiles()
    {
        var files=await ListFiles("trashed=false and appProperties has { key='emusync' and value='game-v1' }");
        foreach(var file in files)
        {
            if(file.AppProperties is null || file.AppProperties.GetValueOrDefault("emusync")!="game-v1" || file.Size is <=0 or >8L*1024*1024*1024 ||
                !Regex.IsMatch(file.AppProperties.GetValueOrDefault("hash")??"","^[A-F0-9]{64}$") || file.Description is null || file.Description.Length>4096)
                throw new IOException("Drive game information is invalid. No game will be launched.");
            SafePath.GameId(file.AppProperties.GetValueOrDefault("game")??"");
        }
        if(files.GroupBy(f=>f.AppProperties["game"]).Any(g=>g.Select(f=>f.AppProperties["hash"]).Distinct().Count()>1))
            throw new IOException("Drive has different ISO copies for the same game ID. Keep the intended ISO in the EmuSync folder and remove the duplicate there, then Refresh games.");
        return files;
    }
    public async Task<List<Game>> Games()
    {
        var files=await GameFiles(); var result=new List<Game>();
        foreach(var file in files.DistinctBy(f=>f.AppProperties["game"]))
        {
            using var json=JsonDocument.Parse(file.Description!);
            var title=json.RootElement.GetProperty("title").GetString();
            if(string.IsNullOrWhiteSpace(title) || title.Length>512) throw new IOException("Drive returned an invalid game title.");
            var id=file.AppProperties["game"];
            result.Add(new(id,title,id+".iso",file.Size,null,null));
        }
        return result;
    }
    public async Task UploadGame(Game game,Stream input,Action<string> progress)
    {
        SafePath.GameId(game.Id);
        if(!input.CanSeek || input.Length!=game.Size || game.Size is <=0 or >8L*1024*1024*1024) throw new IOException("Choose a local, seekable ISO file of at most 8 GB.");
        input.Position=0;
        var actual=await IsoReader.Read(input,game.FileName);
        if(actual.Id!=game.Id || actual.Size!=game.Size) throw new IOException("The ISO changed. Refresh your library before uploading.");
        if(string.IsNullOrWhiteSpace(actual.Title) || actual.Title.Length>512) throw new IOException("This ISO has an invalid game title.");
        progress("Verifying ISO before upload…"); input.Position=0;
        using var timeout=new CancellationTokenSource(TimeSpan.FromHours(2));
        var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,timeout.Token)); input.Position=0;
        var existing=(await GameFiles()).Where(f=>f.AppProperties["game"]==game.Id).ToArray();
        if(existing.Any(f=>f.AppProperties["hash"]!=hash)) throw new IOException("Drive already has a different ISO for this game ID. Keep the intended copy in the EmuSync folder before uploading another.");
        if(existing.Length>0) { progress("This game is already on Google Drive."); return; }
        var metadata=new {name=game.Id+".iso",parents=new[]{await Folder()},appProperties=new {emusync="game-v1",game=game.Id,hash},description=JsonSerializer.Serialize(new {title=actual.Title},Json)};
        await Upload(metadata,input,game.Size,progress,hash);
        await GameFiles();
        progress("Game uploaded to Google Drive.");
    }
    public async Task<(string Path,string Hash)> DownloadGame(Game game,string cache,Action<string> progress)
    {
        SafePath.GameId(game.Id);
        var file=(await GameFiles()).FirstOrDefault(f=>f.AppProperties["game"]==game.Id)??throw new IOException("This ISO is no longer on Google Drive. Refresh games.");
        if(file.Size!=game.Size) throw new IOException("The Drive library changed. Refresh games before downloading.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var response=await client.GetAsync($"drive/v3/files/{file.Id}?alt=media",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        Ensure(response);
        return await GameDownload.Receive(response,game,file.AppProperties["hash"],cache,progress,timeout.Token);
    }
    async Task<string> Upload(object metadata,Stream input,long length,Action<string> progress,string? expectedHash=null)
    {
        // shortcut: interrupted uploads restart; persist resumable sessions if cross-launch resume is needed.
        using var start=new HttpRequestMessage(HttpMethod.Post,"upload/drive/v3/files?uploadType=resumable&fields=id");
        start.Headers.Add("X-Upload-Content-Type","application/octet-stream"); start.Headers.Add("X-Upload-Content-Length",length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Content=JsonContent.Create(metadata);
        using var opened=await client.SendAsync(start); Ensure(opened);
        var location=opened.Headers.Location;
        if(location is null || !location.IsAbsoluteUri || location.Scheme!="https" || location.Host!="www.googleapis.com" || location.Port!=443 || location.UserInfo.Length>0 || !location.AbsolutePath.StartsWith("/upload/drive/v3/files",StringComparison.Ordinal))
            throw new IOException("Google returned an invalid upload location. No credentials were sent there.");
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=new byte[4*1024*1024]; long position=0;
        while(position<length)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var count=(int)Math.Min(buffer.Length,length-position);
            await input.ReadExactlyAsync(buffer.AsMemory(0,count),timeout.Token);
            digest.AppendData(buffer,0,count);
            if(position+count==length && expectedHash is not null)
            {
                if(Convert.ToHexString(digest.GetHashAndReset())!=expectedHash || await input.ReadAsync(new byte[1],timeout.Token)!=0)
                    throw new IOException("The ISO changed while uploading. The incomplete upload was not committed.");
            }
            using var chunk=new HttpRequestMessage(HttpMethod.Put,location) {Content=new ByteArrayContent(buffer,0,count)};
            chunk.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");
            chunk.Content.Headers.ContentRange=new ContentRangeHeaderValue(position,position+count-1,length);
            using var response=await client.SendAsync(chunk,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            position+=count;
            if(position<length)
            {
                if((int)response.StatusCode!=308 || !response.Headers.TryGetValues("Range",out var ranges) || ranges.SingleOrDefault()!=$"bytes=0-{position-1}")
                { Ensure(response); throw new IOException("Drive did not confirm this upload chunk. Your local file is safe; try uploading again."); }
            }
            else
            {
                using var result=JsonDocument.Parse(await Bytes(response,64000));
                var id=result.RootElement.GetProperty("id").GetString();
                if(!ValidId(id)) throw new IOException("Drive did not confirm the completed upload.");
                progress("Upload complete · 100%"); return id!;
            }
            progress($"Uploading · {position*100/length}%");
        }
        throw new IOException("An empty file cannot be uploaded.");
    }
}
