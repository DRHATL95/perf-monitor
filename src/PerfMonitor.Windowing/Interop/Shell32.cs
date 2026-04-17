using System.Runtime.InteropServices;

namespace PerfMonitor.Windowing.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public int lParam;
}

internal static class Shell32
{
    public const uint ABM_NEW      = 0x00000000;
    public const uint ABM_REMOVE   = 0x00000001;
    public const uint ABM_QUERYPOS = 0x00000002;
    public const uint ABM_SETPOS   = 0x00000003;

    public const uint ABE_LEFT   = 0;
    public const uint ABE_TOP    = 1;
    public const uint ABE_RIGHT  = 2;
    public const uint ABE_BOTTOM = 3;

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
