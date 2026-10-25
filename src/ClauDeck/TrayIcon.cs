using System.Runtime.InteropServices;

namespace ClauDeck;

/// <summary>
/// The notification-area icon. WinUI has none of its own, so this is the plain Win32
/// one, with Windows' own menu so it looks like every other tray menu on the system.
/// </summary>
sealed class TrayIcon : IDisposable
{
    public sealed record MenuItem(string Text, Action OnClick, bool Bold = false, bool Checked = false);

    const int WM_TRAY = 0x8000 + 1;
    const int WM_CONTEXTMENU = 0x007B, NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

    readonly WndProc proc;          // held so the delegate outlives the native window
    readonly IntPtr hwnd;
    readonly IntPtr icon;
    readonly Func<IReadOnlyList<MenuItem?>> buildMenu;
    readonly Action onClick;
    string tip = Brand.Name;

    public TrayIcon(string iconFile, Action onClick, Func<IReadOnlyList<MenuItem?>> buildMenu)
    {
        this.onClick = onClick;
        this.buildMenu = buildMenu;
        proc = WindowProc;
        var cls = new WNDCLASSEX { cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = proc, hInstance = GetModuleHandle(null), lpszClassName = "ClauDeckTray" };
        RegisterClassEx(ref cls);
        hwnd = CreateWindowEx(0, cls.lpszClassName, "ClauDeck tray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        icon = LoadImage(IntPtr.Zero, iconFile, 1 /* IMAGE_ICON */, GetSystemMetrics(49), GetSystemMetrics(50), 0x10 /* LR_LOADFROMFILE */);
        try { SetPreferredAppMode(1 /* AllowDark */); FlushMenuThemes(); } catch (EntryPointNotFoundException) { }
        Add();
    }

    public string Tooltip
    {
        set
        {
            tip = value.Length > 127 ? value[..127] : value;
            var data = Data(0x04 /* NIF_TIP */);
            Shell_NotifyIcon(1 /* NIM_MODIFY */, ref data);
        }
    }

    public void Balloon(string title, string text)
    {
        var data = Data(0x10 /* NIF_INFO */);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = 1; // NIIF_INFO
        Shell_NotifyIcon(1, ref data);
    }

    void Add()
    {
        var data = Data(0x01 | 0x02 | 0x04 /* NIF_MESSAGE | NIF_ICON | NIF_TIP */);
        Shell_NotifyIcon(0 /* NIM_ADD */, ref data);
        data.uTimeoutOrVersion = 4; // NOTIFYICON_VERSION_4
        Shell_NotifyIcon(4 /* NIM_SETVERSION */, ref data);
    }

    NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = hwnd, uID = 1, uFlags = flags,
        uCallbackMessage = WM_TRAY, hIcon = icon, szTip = tip, szInfo = "", szInfoTitle = "",
    };

    IntPtr WindowProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY)
        {
            // Version 4 icons report a click as NIN_SELECT and a right-click as
            // WM_CONTEXTMENU, after the raw button messages; answering both opens twice.
            var evt = (int)(lParam.ToInt64() & 0xFFFF);
            if (evt is NIN_SELECT or NIN_KEYSELECT) onClick();
            else if (evt == WM_CONTEXTMENU) ShowMenu();
            return IntPtr.Zero;
        }
        // Explorer restarted and took every tray icon with it.
        if (msg == WM_TASKBARCREATED) Add();
        return DefWindowProc(h, msg, wParam, lParam);
    }

    void ShowMenu()
    {
        var items = buildMenu();
        var menu = CreatePopupMenu();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not { } item) { AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, null); continue; }
            AppendMenu(menu, item.Checked ? 0x8 /* MF_CHECKED */ : 0, i + 1, item.Text);
            if (item.Bold) SetMenuDefaultItem(menu, i + 1, 0);
        }
        GetCursorPos(out var pt);
        // Without this the menu stays open when the user clicks elsewhere.
        SetForegroundWindow(hwnd);
        var chosen = TrackPopupMenuEx(menu, 0x100 /* TPM_RETURNCMD */ | 0x2 /* TPM_RIGHTBUTTON */, pt.X, pt.Y, hwnd, IntPtr.Zero);
        PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        if (chosen > 0 && items[chosen - 1] is { } picked) picked.OnClick();
    }

    public void Dispose()
    {
        var data = Data(0);
        Shell_NotifyIcon(2 /* NIM_DELETE */, ref data);
        if (icon != IntPtr.Zero) DestroyIcon(icon);
        DestroyWindow(hwnd);
    }

    delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public int cbSize, style;
        public WndProc lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

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

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr inst, string name, int type, int cx, int cy, int load);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, int flags, int id, string? text);
    [DllImport("user32.dll")] static extern bool SetMenuDefaultItem(IntPtr menu, int item, int byPos);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, int flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    // Lets the tray menu follow dark mode. Undocumented but stable since Windows 10 1903.
    [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();
}
