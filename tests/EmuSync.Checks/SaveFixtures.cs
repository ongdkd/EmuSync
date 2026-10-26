using System.IO.Compression;
using EmuSync.Core;

static class SaveFixtures
{
    static readonly List<SaveBundle> owned=[];
    public static SaveBundle Create(Dictionary<string,byte[]> files,string id="ULUS12345")
    {
        using var data=new MemoryStream();
        using(var zip=new ZipArchive(data,ZipArchiveMode.Create,true))
            foreach(var (path,bytes) in files) { using var entry=zip.CreateEntry(path).Open(); entry.Write(bytes); }
        return Decode(data.ToArray(),id).GetAwaiter().GetResult();
    }
    public static async Task<SaveBundle> Decode(byte[] bytes,string id)
    { using var input=new MemoryStream(bytes); var bundle=await SaveBundle.FromZip(input,id); owned.Add(bundle); return bundle; }
    public static async Task<SaveBundle> Capture(IGameFolder folder,string id)
    { var bundle=await SaveBundle.Capture(folder,id); owned.Add(bundle); return bundle; }
    public static byte[] Zip(SaveBundle bundle)
    { using var input=bundle.OpenZip(); using var output=new MemoryStream(); input.CopyTo(output); return output.ToArray(); }
    public static byte[] Read(SaveBundle bundle,string path)
    { using var input=bundle.OpenZip(); using var zip=new ZipArchive(input); using var entry=zip.GetEntry(path)!.Open(); using var output=new MemoryStream(); entry.CopyTo(output); return output.ToArray(); }
    public static SaveBundle With(SaveBundle bundle,params (string Path,byte[] Data)[] changes)
    {
        var files=bundle.Paths.ToDictionary(path=>path,path=>Read(bundle,path));
        foreach(var (path,data) in changes) files[path]=data;
        return Create(files,bundle.Id);
    }
    public static void DisposeAll() { foreach(var bundle in owned) bundle.Dispose(); owned.Clear(); }
}
