using Android.Content;
using Android.Provider;
using EmuSync.Core;
using Microsoft.Win32.SafeHandles;
using Uri=Android.Net.Uri;

namespace EmuSync.App;

public static class DeviceFiles
{
    public static IGameFolder OpenFolder(string uri)=>new SafFolder(uri);
    public static Task<string?> PickFolder()
    {
        if(MainActivity.FolderRequest is not null) throw new IOException("A folder picker is already open.");
        var request=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        MainActivity.FolderRequest=request;
        var intent=new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission|ActivityFlags.GrantWriteUriPermission|ActivityFlags.GrantPersistableUriPermission|ActivityFlags.GrantPrefixUriPermission);
        Platform.CurrentActivity!.StartActivityForResult(intent,731);
        return request.Task;
    }
    public static Task<string?> PickEmulator()
    {
        var manager=Android.App.Application.Context.PackageManager!;
        foreach(var package in new[]{"org.ppsspp.ppsspp","org.ppsspp.ppssppgold"})
        {
            var intent=manager.GetLaunchIntentForPackage(package);
            if(intent is not null) return Task.FromResult<string?>(package);
        }
        throw new IOException("Install PPSSPP or PPSSPP Gold, then select Detect PPSSPP again.");
    }
    public static bool EmulatorRunning()=>false;
    public static string GamePath(string root,string relative)=>new SafFolder(root).GetUri(relative).ToString()!;
    public static Task Launch(string executable,string file)
    {
        if(executable is not ("org.ppsspp.ppsspp" or "org.ppsspp.ppssppgold")) throw new IOException("Detect PPSSPP again in Settings.");
        var intent=new Intent(Intent.ActionView);
        intent.SetClassName(executable,"org.ppsspp.ppsspp.PpssppActivity");
        intent.SetDataAndType(Uri.Parse(file),"application/octet-stream");
        intent.PutExtra("org.ppsspp.ppsspp.Args","--pause-menu-exit");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission|ActivityFlags.NewTask);
        intent.ClipData=ClipData.NewRawUri("PSP game",Uri.Parse(file));
        Platform.CurrentActivity!.StartActivity(intent);
        return Task.CompletedTask;
    }
}

public sealed class SafFolder(string tree) : IGameFolder
{
    readonly ContentResolver resolver=Android.App.Application.Context.ContentResolver!;
    readonly Uri root=DocumentsContract.BuildDocumentUriUsingTree(Uri.Parse(tree),DocumentsContract.GetTreeDocumentId(Uri.Parse(tree)))!;
    // Every Android folder query is a slow cross-process call, so remember the documents already found.
    // A SafFolder is created per operation; changes to the folder tree drop what was remembered.
    readonly Dictionary<string,Uri> known=[];
    public Uri GetUri(string path,bool create=false,bool directory=false)
    {
        if(path.Length==0) return root;
        if(known.TryGetValue(path,out var cached)) return cached;
        SafePath.Validate(path);
        var current=root;
        var parts=path.Split('/');
        for(var i=0;i<parts.Length;i++)
        {
            var prefix=string.Join('/',parts[..i]);
            var key=string.Join('/',parts[..(i+1)]);
            if(known.TryGetValue(key,out var seen)) { current=seen; continue; }
            var next=Children(current,prefix).FirstOrDefault(e=>e.Name==parts[i]);
            if(next is not null) { current=next.Uri; continue; }
            if(!create) throw new FileNotFoundException("This folder is no longer accessible. Select it again in Settings.",path);
            current=DocumentsContract.CreateDocument(resolver,current,i<parts.Length-1 || directory?DocumentsContract.Document.MimeTypeDir:"application/octet-stream",parts[i])
                ?? throw new IOException("Android could not create the save file.");
            known[key]=current;
        }
        return current;
    }
    // Confirms Android still lets EmuSync use this folder, with a message that says what to do if not.
    public void CheckAccess()
    {
        var granted=resolver.PersistedUriPermissions.Any(p=>p.Uri?.ToString()==tree && p.IsReadPermission && p.IsWritePermission);
        if(!granted) throw new IOException("Android no longer allows EmuSync to use this folder. Open Settings and select it again.");
        Children(root,"");
    }
    sealed record Document(string Name,bool Directory,long Size,Uri Uri);
    List<Document> Children(Uri directory,string prefix)
    {
        var uri=DocumentsContract.BuildChildDocumentsUriUsingTree(Uri.Parse(tree),DocumentsContract.GetDocumentId(directory));
        Android.Database.ICursor? cursor;
        try { cursor=resolver.Query(uri!,new[]{DocumentsContract.Document.ColumnDocumentId,DocumentsContract.Document.ColumnDisplayName,DocumentsContract.Document.ColumnMimeType,DocumentsContract.Document.ColumnSize},null,null,null); }
        catch(Java.Lang.SecurityException) { throw new IOException("Android no longer allows EmuSync to use this folder. Open Settings and select it again."); }
        var result=new List<Document>();
        if(cursor is null) throw new IOException("Android could not read this folder. Select it again in Settings.");
        using(cursor)
            while(cursor.MoveToNext())
            {
                var name=cursor.GetString(1)??"";
                var document=new Document(name,cursor.GetString(2)==DocumentsContract.Document.MimeTypeDir,cursor.IsNull(3)?0:cursor.GetLong(3),DocumentsContract.BuildDocumentUriUsingTree(Uri.Parse(tree),cursor.GetString(0))!);
                result.Add(document);
                if(name.Length>0 && !name.Contains('/')) known[prefix.Length==0?name:prefix+"/"+name]=document.Uri;
            }
        return result;
    }
    public Task<IReadOnlyList<FolderEntry>> List(string path="")
    {
        try { return Task.FromResult<IReadOnlyList<FolderEntry>>(Children(GetUri(path),path).Select(d=>new FolderEntry(d.Name,d.Directory,d.Size)).ToArray()); }
        catch(FileNotFoundException) { return Task.FromResult<IReadOnlyList<FolderEntry>>([]); }
    }
    public Task<Stream> Read(string path)
    {
        using var descriptor=resolver.OpenFileDescriptor(GetUri(path),"r") ?? throw new IOException("Android could not open this file.");
        Stream stream=new FileStream(new SafeFileHandle((IntPtr)descriptor.DetachFd(),true),FileAccess.Read);
        return Task.FromResult(stream);
    }
    public async Task Write(string path,Stream input)
    {
        using var descriptor=resolver.OpenFileDescriptor(GetUri(path,true),"wt") ?? throw new IOException("Android could not write this file.");
        await using var output=new FileStream(new SafeFileHandle((IntPtr)descriptor.DetachFd(),true),FileAccess.Write);
        await input.CopyToAsync(output); output.Flush(true);
    }
    public void CompleteGameDownload(string source,string target)
    {
        SafePath.Validate(source); SafePath.Validate(target);
        if(source.Contains('/') || target.Contains('/') || Children(root,"").Any(d=>d.Name==target))
            throw new IOException("The game download could not be finalized safely.");
        var renamed=DocumentsContract.RenameDocument(resolver,GetUri(source),target);
        known.Clear();
        if(renamed is null) throw new IOException("Android could not finish the game download. Use a local games folder.");
    }
    public Task Delete(string path)
    {
        try { if(!DocumentsContract.DeleteDocument(resolver,GetUri(path))) throw new IOException("Android could not remove the old save file."); }
        catch(FileNotFoundException) { }
        finally { known.Clear(); }
        return Task.CompletedTask;
    }
}
