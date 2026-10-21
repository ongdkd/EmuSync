using System.Security.Cryptography;

namespace EmuSync.Core;

public static class GameImport
{
    public static async Task<(string Path,Game Game,string Hash)> Stage(Stream input,string cache)
    {
        const long limit=8L*1024*1024*1024;
        if(input.CanSeek && (input.Length==0 || input.Length>limit)) throw new IOException("Choose a PSP ISO no larger than 8 GB.");
        Directory.CreateDirectory(cache);
        var path=System.IO.Path.Combine(cache,"import-"+Guid.NewGuid().ToString("N")+".iso");
        try
        {
            await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,1<<20,true))
                await SaveBundle.CopyLimited(input,output,limit);
            await using var file=File.OpenRead(path);
            var game=await IsoReader.Read(file,"import.iso");
            file.Position=0;
            return (path,game,Convert.ToHexString(await SHA256.HashDataAsync(file)));
        }
        catch { File.Delete(path); throw; }
    }
}
