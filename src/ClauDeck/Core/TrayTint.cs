using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace ClauDeck;

/// <summary>
/// Repaints an account's own Claude tray icon in its avatar colour, with its name as the
/// tooltip, so several Claudes in the notification area can be told apart. Nothing is
/// added: it is the icon Claude already shows, changed in place.
///
/// Electron keeps its tray icons on a hidden "Electron_NotifyIconHostWindow" and numbers
/// them, and Claude creates its tray without a fixed GUID, so the icon is addressed by
/// that window and number like any other. Claude swaps its own image back now and then
/// (a theme change, a status), so this runs on every scan rather than once.
/// </summary>
static class TrayTint
{
    const string HostClass = "Electron_NotifyIconHostWindow";
    const int NIM_MODIFY = 1, NIF_ICON = 0x02, NIF_TIP = 0x04;

    // Which icon number answered on each host window, so it is looked for only once.
    static readonly ConcurrentDictionary<IntPtr, int> iconIds = new();
    static readonly ConcurrentDictionary<string, IntPtr> loadedIcons = new();

    public static void Apply(Account account, int pid)
    {
        var host = FindHost(pid);
        if (host == IntPtr.Zero) return;
        var icon = SmallIcon(Icons.IconFileFor(account));
        if (icon == IntPtr.Zero) return;
        var tip = $"Claude - {account.Name}";

        if (iconIds.TryGetValue(host, out var known) && Modify(host, known, icon, tip)) return;
        // Electron numbers tray icons from a small base; the first that accepts is Claude's.
        for (var id = 0; id <= 20; id++)
        {
            if (!Modify(host, id, icon, tip)) continue;
            iconIds[host] = id;
            return;
        }
    }

    static bool Modify(IntPtr host, int id, IntPtr icon, string tip)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = host, uID = id, uFlags = NIF_ICON | NIF_TIP,
            hIcon = icon, szTip = tip, szInfo = "", szInfoTitle = "",
        };
        return Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    static IntPtr FindHost(int pid)
    {
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid) return true;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() != HostClass) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    // The shell copies the icon it is given, but the handle is kept per file so that a
    // scan every few seconds does not load the same icon over and over.
    static IntPtr SmallIcon(string file) => loadedIcons.GetOrAdd(file, f =>
        LoadImage(IntPtr.Zero, f, 1 /* IMAGE_ICON */, GetSystemMetrics(49), GetSystemMetrics(50), 0x10 /* LR_LOADFROMFILE */));

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr inst, string name, int type, int cx, int cy, int load);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
}
