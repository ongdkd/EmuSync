namespace EmuSync.Core;

public sealed class LocalFolder(string root,bool allowSaveDataLink=false) : IGameFolder
{
    public string Root { get; } = Path.GetFullPath(root);
    readonly string? saveRoot=SaveTarget(root,allowSaveDataLink);
    static string? SaveTarget(string root,bool allow)
    {
        if(!allow) return null;
        var link=new DirectoryInfo(Path.Combine(Path.GetFullPath(root),"SAVEDATA"));
        if(!link.Exists && link.LinkTarget is null) return null;
        if(!link.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
        // EmuDeck links this one save directory; pin its destination for the lifetime of this sync.
        var target=link.ResolveLinkTarget(true) as DirectoryInfo;
        if(target is null || !target.Exists || target.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("The SAVEDATA link has no usable destination. Check the save location in EmuDeck.");
        return target.FullName;
    }
    string Resolve(string path)
    {
        if (path.Length == 0) return Root;
        SafePath.Validate(path);
        var parts=path.Split('/');
        var mapped=saveRoot is not null && parts[0]=="SAVEDATA";
        var current=mapped?saveRoot!:Root;
        if(mapped && Linked(current))
            throw new IOException("The save destination changed into a link. Sync paused; no save was replaced.");
        foreach (var part in parts.Skip(mapped?1:0))
        {
            current = Path.Combine(current,part);
            if (Linked(current))
                throw new IOException("Linked folders are not supported for syncing.");
        }
        return current;
    }
    static bool Linked(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch(FileNotFoundException) { return false; }
        catch(DirectoryNotFoundException) { return false; }
    }
    public Task<IReadOnlyList<FolderEntry>> List(string path = "")
    {
        var directory = Resolve(path);
        var entries=new List<FolderEntry>();
        if(Directory.Exists(directory)) foreach(var item in Directory.EnumerateFileSystemEntries(directory))
        {
            var name=Path.GetFileName(item);
            var mapped=path.Length==0 && saveRoot is not null && name=="SAVEDATA";
            if(File.GetAttributes(item).HasFlag(FileAttributes.ReparsePoint) && !mapped)
            {
                if(allowSaveDataLink && (path=="SAVEDATA" || path.StartsWith("SAVEDATA/",StringComparison.Ordinal)))
                    throw new IOException($"A link inside SAVEDATA is not supported: {name}. Sync paused to avoid an incomplete save.");
                continue;
            }
            entries.Add(new(name,mapped || Directory.Exists(item),File.Exists(item)?new FileInfo(item).Length:0));
        }
        return Task.FromResult<IReadOnlyList<FolderEntry>>(entries);
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
