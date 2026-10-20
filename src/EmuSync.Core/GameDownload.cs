using System.Security.Cryptography;

namespace EmuSync.Core;

static class GameDownload
{
    public static async Task<(string Path,string Hash)> Receive(HttpResponseMessage response,Game game,string digest,string cache,Action<string> progress,CancellationToken token)
    {
        SafePath.GameId(game.Id);
        if(digest.Length!=64 || !digest.All(Uri.IsHexDigit) || game.Size<=0 || game.Size>8L*1024*1024*1024 || response.Content.Headers.ContentLength!=game.Size)
            throw new IOException("Invalid game download information.");
        Directory.CreateDirectory(cache);
        var path=Path.Combine(cache,game.Id+"-"+Guid.NewGuid().ToString("N")+".iso");
        try
        {
            await using var input=await response.Content.ReadAsStreamAsync(token);
            using var received=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,1<<20,FileOptions.Asynchronous|FileOptions.SequentialScan))
            {
                var buffer=new byte[1<<20]; long total=0; int count,percent=-1;
                while((count=await input.ReadAsync(buffer,token))>0)
                {
                    if((total+=count)>game.Size) throw new IOException("The game download is larger than expected.");
                    received.AppendData(buffer,0,count);
                    await output.WriteAsync(buffer.AsMemory(0,count),token);
                    var next=(int)(total*100/game.Size);
                    if(next!=percent) { percent=next; progress($"Downloading game · {percent}%"); }
                }
                if(total!=game.Size) throw new IOException("The game download was interrupted. Try Play again.");
            }
            if(!string.Equals(Convert.ToHexString(received.GetHashAndReset()),digest,StringComparison.OrdinalIgnoreCase)) throw new IOException("The game failed verification. Try downloading again.");
            return (path,digest);
        }
        catch { if(File.Exists(path)) File.Delete(path); throw; }
    }
}
