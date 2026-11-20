using System.Runtime.InteropServices;
using System.Text;
namespace YierBubu.Windows;

public static class FullscreenDetector
{
    public static bool IsCoveringPet(nint pet)
    {
        nint foreground=NativeMethods.GetForegroundWindow();
        if(foreground==0||foreground==pet||!NativeMethods.IsWindowVisible(foreground)||NativeMethods.IsIconic(foreground))return false;
        NativeMethods.GetWindowThreadProcessId(foreground,out uint process);
        if(process==(uint)Environment.ProcessId)return false;
        var name=new StringBuilder(256);NativeMethods.GetClassName(foreground,name,name.Capacity);
        if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return false;
        if((NativeMethods.GetWindowLongPtr(foreground,-16).ToInt64()&0x00C00000)!=0)return false;
        nint monitor=NativeMethods.MonitorFromWindow(pet,2);
        if(monitor!=NativeMethods.MonitorFromWindow(foreground,2))return false;
        var info=new NativeMethods.MonitorInfo{Size=Marshal.SizeOf<NativeMethods.MonitorInfo>()};
        if(!NativeMethods.GetMonitorInfo(monitor,ref info))return false;
        if(NativeMethods.DwmGetWindowAttribute(foreground,9,out var rect,Marshal.SizeOf<NativeMethods.Rect>())!=0&&
            !NativeMethods.GetWindowRect(foreground,out rect))return false;
        return FullscreenPolicy.CoversMonitor(new(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top),
            new(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top));
    }
}
