using System.ComponentModel;
using System.Runtime.InteropServices;

static class WindowsRedirectionChecks
{
    public static bool Enabled { get; private set; }
    public static void Enable()
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("RedirectionGuard check requires Windows.");
        const int policy=16; // ProcessRedirectionTrustPolicy
        uint flags=1;
        if(!SetProcessMitigationPolicy(policy,ref flags,4) || !GetProcessMitigationPolicy(new IntPtr(-1),policy,out flags,4) || (flags&1)==0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not enable and verify RedirectionGuard in the isolated check process.");
        Enabled=true;
        Console.WriteLine("RedirectionGuard enforcement enabled and verified for this check process.");
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool SetProcessMitigationPolicy(int policy,ref uint flags,nuint size);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool GetProcessMitigationPolicy(IntPtr process,int policy,out uint flags,nuint size);
}
