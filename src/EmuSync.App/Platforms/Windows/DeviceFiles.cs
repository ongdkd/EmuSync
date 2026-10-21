using EmuSync.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EmuSync.App;

public static class DeviceFiles
{
    public static IGameFolder OpenFolder(string path)=>new LocalFolder(path);
    public static async Task<string?> PickFolder()
    {
        var picker=new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var window=(MauiWinUIWindow)Application.Current!.Windows[0].Handler.PlatformView!;
        WinRT.Interop.InitializeWithWindow.Initialize(picker,window.WindowHandle);
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    public static async Task<string?> PickEmulator()
    {
        var result=await FilePicker.Default.PickAsync(new PickOptions {PickerTitle="Choose PPSSPP",FileTypes=new FilePickerFileType(new Dictionary<DevicePlatform,IEnumerable<string>> {{DevicePlatform.WinUI,[".exe"]}})});
        if(result is null) return null;
        if(!Path.GetFileName(result.FullPath).StartsWith("PPSSPP",StringComparison.OrdinalIgnoreCase)) throw new IOException("Select the PPSSPP executable.");
        return result.FullPath;
    }
    public static bool EmulatorRunning()=>Process.GetProcesses().Any(p=> {
        try { return p.ProcessName.StartsWith("PPSSPP",StringComparison.OrdinalIgnoreCase); }
        catch(InvalidOperationException) { return false; }
        finally { p.Dispose(); }
    });
    public static string GamePath(string root,string relative)=>Path.Combine(root,SafePath.Validate(relative).Replace('/',Path.DirectorySeparatorChar));
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    public static void ShowApp()
    {
        var window=(MauiWinUIWindow)Application.Current!.Windows[0].Handler.PlatformView!;
        ShowWindow(window.WindowHandle,9);
        window.Activate();
        SetForegroundWindow(window.WindowHandle);
    }
    public static async Task Launch(string executable,string file)
    {
        if(EmulatorRunning()) throw new IOException("PPSSPP is already running. Close it before syncing and playing.");
        var start=new ProcessStartInfo(executable) { UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add("--pause-menu-exit");
        start.ArgumentList.Add(file);
        using var process=Process.Start(start) ?? throw new IOException("PPSSPP could not be started.");
        await process.WaitForExitAsync();
    }
}
