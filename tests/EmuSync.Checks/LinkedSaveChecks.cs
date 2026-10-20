using System.Diagnostics;
using System.Text;
using EmuSync.Core;

static class LinkedSaveChecks
{
    public static async Task<int> Run(string root)
    {
        var checks=0;
        void Check(bool condition,string name) { if(!condition) throw new Exception(name); checks++; }
        async Task Reject(Func<Task> action,string name) { try { await action(); } catch(IOException) { checks++; return; } throw new Exception(name); }
        var fixture=Path.Combine(root,"linked-saves");
        var psp=Path.Combine(fixture,"PSP");
        var actual=Path.Combine(fixture,"Emulation","saves","ppsspp","saves");
        var elsewhere=Path.Combine(fixture,"unrelated");
        try
        {
        Directory.CreateDirectory(psp); Directory.CreateDirectory(actual); Directory.CreateDirectory(elsewhere);
        var data=new LocalFolder(actual);
        await data.Write("ULUS12345/DATA.BIN",new MemoryStream([1,2,3]));
        await data.Write("ULUS99999/DATA.BIN",new MemoryStream([9]));
        await File.WriteAllBytesAsync(Path.Combine(elsewhere,"sentinel.bin"),[7]);
        var link=Path.Combine(psp,"SAVEDATA");
        Link(link,actual);
        if(WindowsRedirectionChecks.Enabled)
        {
            try { await File.ReadAllBytesAsync(Path.Combine(link,"ULUS12345","DATA.BIN")); throw new Exception("The guarded fixture must reject raw junction traversal."); }
            catch(IOException error) when((error.HResult&0xffff)==448) { checks++; }
        }
        await Reject(()=>SaveBundle.Capture(new LocalFolder(psp),"ULUS12345"),"Generic/game folders must still block linked children");
        var saves=new LocalFolder(psp,allowSaveDataLink:true);
        Check((await saves.List()).Any(e=>e.Name=="SAVEDATA" && e.IsDirectory),"PSP picker recognizes the mapped SAVEDATA folder");
        var original=await SaveBundle.Capture(saves,"ULUS12345");
        Check(original.Files["SAVEDATA/ULUS12345/DATA.BIN"].SequenceEqual(new byte[]{1,2,3}),"EmuDeck capture reads the real save target");
        var update=SaveBundle.FromZip(original.Zip(),"ULUS12345");
        update.Files["SAVEDATA/ULUS12345/DATA.BIN"]=[4,5,6];
        update.Files["SAVEDATA/ULUS12345/NEW.BIN"]=[8];
        await SaveBundle.Apply(saves,"ULUS12345",update,Path.Combine(fixture,"backups"));
        Check((await File.ReadAllBytesAsync(Path.Combine(actual,"ULUS12345","DATA.BIN"))).SequenceEqual(new byte[]{4,5,6}),"EmuDeck apply writes the actual save target");
        Check((await SaveBundle.Capture(saves,"ULUS12345")).Info.Hash==update.Info.Hash,"Linked save apply verifies identical portable bundle paths");
        await SaveBundle.Apply(saves,"ULUS12345",original,Path.Combine(fixture,"backups"));
        Check(!File.Exists(Path.Combine(actual,"ULUS12345","NEW.BIN")),"Linked save restore deletes only replaced game files");
        Check((await File.ReadAllBytesAsync(Path.Combine(actual,"ULUS99999","DATA.BIN")))[0]==9,"Other game progress stays intact");
        var nested=Path.Combine(actual,"ULUS12345","ESCAPE");
        Link(nested,elsewhere);
        try
        {
            await Reject(()=>SaveBundle.Capture(saves,"ULUS12345"),"Nested links must stop capture rather than silently omit files");
            await Reject(()=>saves.Read("SAVEDATA/ULUS12345/ESCAPE/sentinel.bin"),"Nested link read escaped the save root");
            await Reject(()=>saves.Write("SAVEDATA/ULUS12345/ESCAPE/sentinel.bin",new MemoryStream([0])),"Nested link write escaped the save root");
            await Reject(()=>saves.Delete("SAVEDATA/ULUS12345/ESCAPE/sentinel.bin"),"Nested link delete escaped the save root");
            Check((await File.ReadAllBytesAsync(Path.Combine(elsewhere,"sentinel.bin")))[0]==7,"Unrelated files remain untouched");
        }
        finally { Directory.Delete(nested); }
        Directory.Delete(link);
        Link(link,elsewhere);
        try
        {
            await saves.Write("SAVEDATA/ULUS12345/DATA.BIN",new MemoryStream([5]));
            Check((await File.ReadAllBytesAsync(Path.Combine(actual,"ULUS12345","DATA.BIN")))[0]==5 && !Directory.Exists(Path.Combine(elsewhere,"ULUS12345")),"Retargeting the PSP link cannot redirect an active sync");
        }
        finally { Directory.Delete(link); }
        var missing=Path.Combine(fixture,"missing"); Directory.CreateDirectory(missing);
        Link(link,missing); Directory.Delete(missing);
        try { await Reject(()=>Task.FromResult(new LocalFolder(psp,allowSaveDataLink:true)),"Broken SAVEDATA link must pause sync"); }
        finally { Directory.Delete(link); }
        return checks;
        }
        finally
        {
            // Unlink fixture junctions before recursive cleanup, including when guard enforcement stops the test.
            foreach(var item in new[]{Path.Combine(actual,"ULUS12345","ESCAPE"),Path.Combine(psp,"SAVEDATA")})
                if(new DirectoryInfo(item).LinkTarget is not null) Directory.Delete(item);
        }
    }
    static void Link(string path,string target)
    {
        if(!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(path,target); return; }
        // Windows junctions match EmuDeck and do not require the symbolic-link administrator privilege.
        var script="New-Item -ItemType Junction -Path '"+path.Replace("'","''")+"' -Target '"+target.Replace("'","''")+"' -ErrorAction Stop | Out-Null";
        var start=new ProcessStartInfo("powershell.exe") {UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process=Process.Start(start)??throw new IOException("Could not create isolated junction fixture.");
        if(!process.WaitForExit(30000) || process.ExitCode!=0) throw new IOException("Could not create isolated junction fixture.");
    }
}
