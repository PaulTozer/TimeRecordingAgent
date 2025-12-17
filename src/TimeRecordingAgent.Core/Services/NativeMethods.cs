using System.Text;

namespace TimeRecordingAgent.Core.Services;

internal static class NativeMethods
{
    private const uint SPI_GETSCREENSAVERRUNNING = 0x0072;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindowsNative(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    internal static void EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam)
    {
        EnumChildWindowsNative(parent, callback, lParam);
    }

    internal static string GetWindowTextSafe(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 2);
        return GetWindowText(handle, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    internal static bool TryGetScreensaverRunning(out bool isRunning)
    {
        int flag = 0;
        var success = SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, ref flag, 0);
        isRunning = flag != 0;
        return success;
    }

}
