namespace EmuSync.Core;

public sealed class LocalFolder(string root) : IGameFolder
{
    public string Root { get; } = Path.GetFullPath(root);
    string Resolve(string path)
    {
        if (path.Length == 0) return Root;
        SafePath.Validate(path);
        var current = Root;
        foreach (var part in path.Split('/'))
        {
            current = Path.Combine(current,part);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Linked folders are not supported for syncing.");
        }
        return current;
    }
    public Task<IReadOnlyList<FolderEntry>> List(string path = "")
    {
        var directory = Resolve(path);
        IReadOnlyList<FolderEntry> entries = !Directory.Exists(directory) ? [] : Directory.EnumerateFileSystemEntries(directory)
            .Where(p => !File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))
            .Select(p => new FolderEntry(Path.GetFileName(p),Directory.Exists(p),File.Exists(p)?new FileInfo(p).Length:0)).ToArray();
        return Task.FromResult(entries);
    }
    public Task<Stream> Read(string path) => Task.FromResult<Stream>(File.Open(Resolve(path),FileMode.Open,FileAccess.Read,FileShare.Read));
    public async Task Write(string path,Stream data)
    {
        var target = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target+".emusync-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await using (var output = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { await data.CopyToAsync(output); output.Flush(true); }
            File.Move(temp,target,true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public Task Delete(string path) { File.Delete(Resolve(path)); return Task.CompletedTask; }
}
