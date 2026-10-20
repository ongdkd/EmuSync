using EmuSync.Core;
using System.Security.Cryptography;
using System.Text.Json;

namespace EmuSync.App;

public sealed class LibraryPage : ContentPage
{
    readonly HybridWebView web=new() { HybridRoot="wwwroot",DefaultFile="index.html" };
    readonly SemaphoreSlim gate=new(1,1);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,Game> local=[];
    readonly List<string> warnings=[];
    Settings settings;
    bool loaded;
    string? session;
#if ANDROID
    bool leftForGame;
#endif
    string status="Choose a game, then press Play.";
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
#if WINDOWS
    WifiHost? host;
#endif
    public LibraryPage()
    {
        BackgroundColor=Color.FromArgb("#171D25");
        settings=Settings.Load();
        Content=web;
        web.SetInvokeJavaScriptTarget(new Bridge(this));
    }
    public sealed class Bridge(LibraryPage page)
    {
        public Task<string> Invoke(string command,string value)=>MainThread.InvokeOnMainThreadAsync(()=>page.Invoke(command,value));
    }
    async Task<string> Invoke(string command,string value)
    {
        await gate.WaitAsync();
        try
        {
            if(session is not null && command is "games-folder" or "psp-folder" or "emulator" or "pair" or "refresh")
                throw new IOException("Finish your PPSSPP session before changing setup.");
            if(command=="psp-folder" && SaveBundle.RecoveryNeeded(Path.Combine(FileSystem.AppDataDirectory,"backups")))
                throw new IOException("Restore the interrupted sync using Refresh games before changing your PSP folder.");
            switch(command)
            {
                case "load": if(!loaded) { await Load(); loaded=true; } break;
                case "refresh": await Load(); break;
                case "games-folder":
                    var games=await DeviceFiles.PickFolder();
                    if(games is not null) { settings.GamesFolder=games; await RestartHost(); await Load(); }
                    break;
                case "psp-folder":
                    var psp=await DeviceFiles.PickFolder();
                    if(psp is not null)
                    {
                        var entries=await DeviceFiles.OpenFolder(psp).List();
                        if(entries.Any(e=>e.IsDirectory && e.Name=="PSP")) throw new IOException("Open the PSP folder inside that location and select it. It should contain SAVEDATA.");
                        if(!entries.Any(e=>e.Name=="SAVEDATA") && entries.Any(e=>e.IsDirectory && System.Text.RegularExpressions.Regex.IsMatch(e.Name,"^[A-Z]{4}[0-9]{5}")))
                            throw new IOException("That looks like SAVEDATA. Select its parent PSP folder instead.");
                        if(!entries.Any(e=>e.IsDirectory && e.Name is "SAVEDATA" or "SYSTEM" or "PPSSPP_STATE") &&
                            !await DisplayAlertAsync("Use this PSP folder?","Choose the same PSP folder shown in PPSSPP → Settings → System → Memory Stick folder. A different folder will not share your game progress.","Use folder","Choose again")) break;
                        settings.PspFolder=psp; await RestartHost(); await StartHost();
                    }
                    break;
                case "emulator": settings.Emulator=await DeviceFiles.PickEmulator()??settings.Emulator; await DetectSaveFolder(); break;
                case "pair":
                    var code=value.Trim();
                    using(var peer=new WifiClient(Pairing.Parse(code))) settings.CachedGames=await peer.Games();
                    settings.PairCode=code;
                    status="Connected to your PC.";
                    break;
                case "paste-pair":
                    var clipboard=await Clipboard.Default.GetTextAsync();
                    return JsonSerializer.Serialize(new {code=clipboard??""},Json);
                case "copy-pair": await CopyPairing(); break;
                case "select": settings.Selected=SafePath.GameId(value); status="Save check on Play"; break;
                case "sync-game":
                    var option=JsonSerializer.Deserialize<GameOption>(value,Json)??throw new IOException("Invalid game setting.");
                    settings.SyncGames[SafePath.GameId(option.Id)]=option.Enabled; break;
                case "play": await Play(SafePath.GameId(value)); break;
                default: throw new IOException("Unknown action.");
            }
            await settings.Save();
            return JsonSerializer.Serialize(new {state=State()},Json);
        }
        catch(Exception error)
        {
            if(command=="play") session=null;
            status=error.Message;
            return JsonSerializer.Serialize(new {state=State(),error=error.Message},Json);
        }
        finally { gate.Release(); }
    }
    record GameOption(string Id,bool Enabled);
    object State()
    {
        var games=local.Values.Concat(settings.CachedGames).DistinctBy(g=>g.Id).OrderBy(g=>g.Title).ToArray();
        if(!games.Any(g=>g.Id==settings.Selected)) settings.Selected=games.FirstOrDefault()?.Id;
        return new {
            platform=DeviceInfo.Platform==DevicePlatform.WinUI?"windows":"android",games,settings.Selected,
            installed=local.Keys.ToArray(),settings.SyncGames,status,warnings=warnings.ToArray(),
            gamesFolder=settings.GamesFolder,pspFolder=settings.PspFolder,emulator=settings.Emulator,
            paired=settings.PairCode is not null,session,backups=Path.Combine(FileSystem.AppDataDirectory,"backups")
        };
    }
    async Task Load()
    {
        warnings.Clear();
        var backups=Path.Combine(FileSystem.AppDataDirectory,"backups");
        if(settings.PspFolder is not null && SaveBundle.RecoveryNeeded(backups))
        {
            if(DeviceFiles.EmulatorRunning() || !await DisplayAlertAsync("Restore interrupted sync?","Close PPSSPP first. A previous save transfer stopped before finishing. Restore its original backup before continuing?","Restore backup","Cancel"))
                throw new IOException("An interrupted sync needs recovery. Close PPSSPP and use Refresh games in Settings.");
            await SaveBundle.Recover(DeviceFiles.OpenFolder(settings.PspFolder),backups);
        }
        if(settings.GamesFolder is not null)
        {
            await Status("Reading your game library…");
            local.Clear();
            var folder=DeviceFiles.OpenFolder(settings.GamesFolder);
            await Task.Run(()=>Scan(folder,"",0));
        }
        if(settings.PairCode is not null)
        {
            try { using var peer=new WifiClient(Pairing.Parse(settings.PairCode)); settings.CachedGames=await peer.Games(); }
            catch(Exception e) when(e is IOException or HttpRequestException or TaskCanceledException)
            { warnings.Add("Main PC unavailable. Showing your saved library; Play will check the connection again."); }
        }
        await StartHost();
        status=local.Count+settings.CachedGames.Count==0?"Add your games folder in Settings to get started.":"Choose a game, then press Play.";
    }
    async Task Scan(IGameFolder folder,string path,int depth)
    {
        // shortcut: scan four folders deep and 1,000 games, use indexed discovery if larger libraries are needed.
        if(depth>4 || local.Count>=1000) return;
        foreach(var entry in await folder.List(path))
        {
            var name=path.Length==0?entry.Name:path+"/"+entry.Name;
            if(entry.IsDirectory) { await Scan(folder,name,depth+1); continue; }
            if(!entry.Name.EndsWith(".iso",StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                await using var stream=await folder.Read(name);
                var game=await IsoReader.Read(stream,name);
                if(!local.TryAdd(game.Id,game)) warnings.Add($"{entry.Name}: another copy of {game.Title} has the same game ID. The first copy is used.");
            }
            catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException)
            { warnings.Add(entry.Name+": "+e.Message); }
        }
    }
    async Task Play(string id)
    {
        if(SaveBundle.RecoveryNeeded(Path.Combine(FileSystem.AppDataDirectory,"backups"))) throw new IOException("An interrupted sync needs recovery. Close PPSSPP and use Refresh games in Settings.");
        if(session is not null || DeviceFiles.EmulatorRunning()) throw new IOException("Close the running PPSSPP game before starting another session.");
        if(settings.GamesFolder is null || settings.PspFolder is null || settings.Emulator is null)
            throw new IOException("Open Settings and select your games folder, PSP folder and PPSSPP first.");
        var game=local.GetValueOrDefault(id)??settings.CachedGames.SingleOrDefault(g=>g.Id==id)??throw new IOException("Select a game from your library.");
        var saves=DeviceFiles.OpenFolder(settings.PspFolder);
#if ANDROID
        ((SafFolder)saves).CheckAccess();
#endif
        var offline=false;
        await Status("Checking your save…");
        // Android always syncs with a PC. A Windows PC does too once it has been paired with another PC.
        var client=settings.PairCode is not null;
#if ANDROID
        client=true;
#endif
        if(client)
        {
        try
        {
#if ANDROID
            if(!Connectivity.Current.ConnectionProfiles.Any(p=>p is ConnectionProfile.WiFi or ConnectionProfile.Ethernet))
                throw new HttpRequestException("Connect to the same Wi-Fi as your PC.");
#endif
            if(settings.PairCode is null) throw new IOException("Connect to your PC in Settings first.");
            using var peer=new WifiClient(Pairing.Parse(settings.PairCode));
            if(!local.ContainsKey(id))
            {
                if(!settings.SyncGames.GetValueOrDefault(id,true) && !await DisplayAlertAsync("Download game?",$"{game.Title} is not on this device. Download it from your PC?","Download","Cancel")) return;
                await Status("Preparing game download…");
                var downloaded=await peer.DownloadGame(game,FileSystem.CacheDirectory,message=>_ = MainThread.InvokeOnMainThreadAsync(()=>Status(message)));
                try
                {
                    var target=id+"-"+Guid.NewGuid().ToString("N")[..8]+".iso";
                    var folder=DeviceFiles.OpenFolder(settings.GamesFolder);
                    var partial=target+".part";
                    try
                    {
                        await using(var input=File.OpenRead(downloaded.Path)) await folder.Write(partial,input);
                        Game imported;
                        await using(var verify=await folder.Read(partial))
                        {
                            if(Convert.ToHexString(await SHA256.HashDataAsync(verify))!=downloaded.Hash)
                                throw new IOException("The game copy failed verification. Nothing will be launched.");
                            verify.Position=0;
                            imported=await IsoReader.Read(verify,target);
                            if(imported.Id!=id) throw new IOException("The downloaded game ID did not match. Nothing will be launched.");
                        }
                        CompleteGameDownload(folder,settings.GamesFolder,partial,target);
                        local[id]=imported;
                    }
                    catch
                    {
                        try { await folder.Delete(partial); } catch(Exception cleanup) { warnings.Add("An unfinished .part download remains in your games folder: "+cleanup.Message); }
                        throw;
                    }
                }
                finally { File.Delete(downloaded.Path); }
            }
            if(!await Sync(id,peer,saves)) return;
        }
#if ANDROID
        catch(Java.Lang.SecurityException)
        { throw new IOException("Android no longer allows EmuSync to use your PSP folder. Open Settings and select it again."); }
#endif
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException)
        {
            if(!local.ContainsKey(id)) throw new IOException("The main PC is unavailable and this game is not downloaded. Keep the PC app open on the same Wi-Fi and try again.",e);
            if(!await DisplayAlertAsync("Play offline?","The main PC could not be reached. Your latest save cannot be checked. You can use your local save and sync it later.","Play offline","Cancel")) return;
            offline=true;
        }
        }
#if WINDOWS
        else
        {
        if(host is null) await StartHost();
        if(!await host!.PreparePlay(async()=> {
            if(DeviceFiles.EmulatorRunning()) throw new IOException("Close PPSSPP before syncing.");
            var snapshot=await SaveBundle.Capture(saves,id);
            if(snapshot.Info.Suspicious && !await DisplayAlertAsync("Save needs attention","Some save files are empty. A backup will be kept. Continue with this local save?","Continue","Cancel")) return false;
            await Backup(id,snapshot); session=id; return true;
        })) return;
        }
#endif
        await settings.Save();
        session=id;
#if ANDROID
        leftForGame=false;
#endif
        await Status(offline?"Playing offline · Progress has not synced.":"Opening PPSSPP…");
        try { await DeviceFiles.Launch(settings.Emulator,DeviceFiles.GamePath(settings.GamesFolder,local[id].FileName)); }
        catch { session=null; throw; }
#if WINDOWS
        session=null;
        await Backup(id,await SaveBundle.Capture(saves,id));
        if(client && !offline)
        {
            try
            {
                using var peer=new WifiClient(Pairing.Parse(settings.PairCode!));
                if(await Sync(id,peer,saves)) await Status("Session finished · Your progress is synced to the main PC.");
            }
            catch(Exception e) when(e is IOException or HttpRequestException or TaskCanceledException)
            { await Status("Progress is still on this PC. "+e.Message); }
        }
        else await Status(client?"Session finished · Progress has not synced.":"Session finished · Your saves are available to your other devices.");
#else
        await Status("Playing in PPSSPP · Return here when you finish.");
#endif
    }
    static void CompleteGameDownload(IGameFolder folder,string root,string partial,string target)
    {
#if ANDROID
        ((SafFolder)folder).CompleteGameDownload(partial,target);
#else
        SafePath.Validate(partial); SafePath.Validate(target);
        File.Move(Path.Combine(root,partial),Path.Combine(root,target),false);
#endif
    }
    async Task<bool> Sync(string id,WifiClient peer,IGameFolder saves)
    {
        // Reading the device save and asking the PC about its copy are independent, so do both at once.
        var capture=Task.Run(()=>SaveBundle.Capture(saves,id));
        var describe=peer.Describe(id);
        await Task.WhenAll(capture,describe);
        var localSave=await capture;
        var remote=await describe;
        var baseline=settings.Baselines.GetValueOrDefault(id);
        var missing=baseline?.Paths.Except(localSave.Files.Keys).Any()==true;
        var action=SyncPlan.Decide(localSave.Info.Hash,remote.Info.Hash,baseline?.Hash,localSave.Info.Suspicious||missing||remote.Info.Suspicious);
        if(action is SyncAction.Conflict or SyncAction.Suspicious)
        {
            var choices=new List<string>();
            if(localSave.Info.Hash is not null && !localSave.Info.Suspicious && !missing) choices.Add("Use this device's save");
            if(remote.Info.Hash is not null && !remote.Info.Suspicious) choices.Add("Use PC save");
            if(choices.Count==0) throw new IOException("These saves look incomplete. Sync is paused. Check your PSP folder and restore a backup before trying again.");
            var choice=await DisplayActionSheetAsync(action==SyncAction.Conflict?"Both devices have different progress. Which save should be used? Both copies will be backed up.":"A save is missing or looks incomplete. Choose a safe copy to continue. Backups will be kept.","Cancel",null,choices.ToArray());
            if(choice=="Cancel" || choice is null) { await Status("Sync paused · Nothing was replaced."); return false; }
            action=choice=="Use PC save"?SyncAction.Download:SyncAction.Upload;
        }
        if(action==SyncAction.Upload)
        {
            await Status("Sending your save to PC…");
            await Backup(id,localSave);
            await peer.UploadSave(id,localSave,remote.Info.Hash);
        }
        else if(action==SyncAction.Download)
        {
            if(remote.Info.Hash is not null)
            {
                await Status("Getting your latest save…");
                var bundle=await peer.DownloadSave(id,remote.Info.Hash);
                await Backup(id,bundle);
                await SaveBundle.Apply(saves,id,bundle,Path.Combine(FileSystem.AppDataDirectory,"backups"));
                localSave=bundle;
            }
        }
        settings.Baselines[id]=new(localSave.Info.Hash,localSave.Files.Keys.ToArray());
        await settings.Save();
        return true;
    }
    static async Task Backup(string id,SaveBundle bundle)
    {
        var directory=Path.Combine(FileSystem.AppDataDirectory,"backups"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory,$"{id}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip"),bundle.Zip());
    }
    async Task Status(string message)
    {
        status=message;
        try { await web.EvaluateJavaScriptAsync("window.setStatus?.("+JsonSerializer.Serialize(message)+")"); }
        catch(InvalidOperationException) { }
    }
    public void Deactivated()
    {
#if ANDROID
        if(session is not null) leftForGame=true;
#endif
    }
    public async Task Returned()
    {
#if ANDROID
        if(!leftForGame || session is null || !await gate.WaitAsync(0)) return;
        try
        {
            leftForGame=false;
            if(!await DisplayAlertAsync("Finished playing?","Exit the game in PPSSPP before syncing your progress back to PC.","Game is closed · Sync","Still playing")) return;
            var id=session; session=null;
            if(settings.PairCode is null || settings.PspFolder is null) return;
            using var peer=new WifiClient(Pairing.Parse(settings.PairCode));
            if(await Sync(id,peer,DeviceFiles.OpenFolder(settings.PspFolder))) await Status("Your progress is synced to PC.");
        }
        catch(Exception error)
        { await Status("Progress is still on this device. "+error.Message); await DisplayAlertAsync("Progress not synced",status,"OK"); }
        finally
        {
            gate.Release();
            await web.EvaluateJavaScriptAsync("window.setState?.("+JsonSerializer.Serialize(State(),Json)+")");
        }
#else
        await Task.CompletedTask;
#endif
    }
    async Task StartHost()
    {
#if WINDOWS
        if(settings.PspFolder is null || settings.GamesFolder is null) return;
        host??=new WifiHost();
        await host.Start(DeviceFiles.OpenFolder(settings.PspFolder),DeviceFiles.OpenFolder(settings.GamesFolder),()=>local.Values.ToArray(),()=>session is not null || DeviceFiles.EmulatorRunning(),FileSystem.AppDataDirectory);
#else
        await Task.CompletedTask;
#endif
    }
    async Task RestartHost()
    {
#if WINDOWS
        if(host is not null) { await host.DisposeAsync(); host=null; }
#else
        await Task.CompletedTask;
#endif
    }
    async Task CopyPairing()
    {
#if WINDOWS
        await StartHost();
        if(host?.Key is null) throw new IOException("Select the games and PSP folders first.");
        var addresses=System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a=>a.ToString()).Where(ip=> { try { Pairing.Parse(new Pairing(ip,WifiHost.Port,host.CertificateHash!,host.Key).Encode()); return !ip.StartsWith("127."); } catch(FormatException) { return false; } }).Distinct().ToArray();
        if(addresses.Length==0) throw new IOException("Connect this PC to your local Wi-Fi or Ethernet first.");
        var address=addresses.Length==1?addresses[0]:await DisplayActionSheetAsync("Choose the PC's Wi-Fi / Ethernet address","Cancel",null,addresses);
        if(address is null || address=="Cancel") return;
        await Clipboard.Default.SetTextAsync(new Pairing(address,WifiHost.Port,host.CertificateHash!,host.Key).Encode());
        await DisplayAlertAsync("Pairing code copied","On Android, open Settings → Connect PC → Paste. Keep this PC app open. Only share this code with your own device.","OK");
#else
        await Task.CompletedTask;
#endif
    }
    async Task DetectSaveFolder()
    {
#if WINDOWS
        if(settings.Emulator is null || settings.PspFolder is not null) return;
        var directory=Path.GetDirectoryName(settings.Emulator)!;
        var installed=Path.Combine(directory,"installed.txt");
        var root=!File.Exists(installed)?Path.Combine(directory,"memstick"):string.IsNullOrWhiteSpace(File.ReadAllText(installed))?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"PPSSPP"):File.ReadAllText(installed).Trim();
        var psp=Directory.Exists(Path.Combine(root,"PSP"))?Path.Combine(root,"PSP"):root;
        if(Directory.Exists(Path.Combine(psp,"SAVEDATA")) && await DisplayAlertAsync("Use detected PSP folder?",psp,"Use folder","Choose later")) settings.PspFolder=psp;
        await StartHost();
#else
        await Task.CompletedTask;
#endif
    }
}
