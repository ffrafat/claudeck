using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ClauDeck;

sealed record ClaudeProcess(int Pid, int ParentPid, string Image, string CommandLine, bool IsMain, string? UserDataDir);

static partial class Processes
{
    static readonly Regex UserDataDirArg = new("--user-data-dir=(?:\"([^\"]+)\"|(\\S+))", RegexOptions.Compiled);

    /// <summary>
    /// Every desktop Claude process. The Claude Code command line is also called
    /// claude.exe and the desktop app starts one per Code session, so the image path
    /// has to name the desktop app.
    /// </summary>
    public static List<ClaudeProcess> Scan()
    {
        var parents = ParentMap();
        var result = new List<ClaudeProcess>();
        foreach (var p in Process.GetProcessesByName("claude"))
        {
            using (p)
            {
                var (image, cmd) = Inspect(p.Id);
                if (image == null || cmd == null || !IsDesktopImage(image)) continue;
                var m = UserDataDirArg.Match(cmd);
                var dir = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
                result.Add(new ClaudeProcess(p.Id, parents.GetValueOrDefault(p.Id), image, cmd,
                    !cmd.Contains("--type=", StringComparison.Ordinal), dir == null ? null : Normalize(dir)));
            }
        }
        return result;
    }

    static bool IsDesktopImage(string image) =>
        image.Contains(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) ||
        image.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant();

    /// <summary>
    /// Maps each account's data folder to the pid of the Claude showing it. The main
    /// process of the ordinary Claude carries no --user-data-dir, but its helpers name
    /// the folder it actually settled on, which is the more trustworthy of the two.
    /// </summary>
    public static Dictionary<string, int> RunningByDataDir(List<ClaudeProcess> all)
    {
        var map = new Dictionary<string, int>();
        var mains = all.Where(p => p.IsMain).ToDictionary(p => p.Pid);
        foreach (var helper in all.Where(p => !p.IsMain && p.UserDataDir != null))
            if (mains.ContainsKey(helper.ParentPid)) map.TryAdd(helper.UserDataDir!, helper.ParentPid);
        foreach (var main in mains.Values.Where(m => m.UserDataDir != null))
            map.TryAdd(main.UserDataDir!, main.Pid);
        return map;
    }

    /// <summary>Brings a Claude window forward. False when it has none on screen.</summary>
    public static bool Focus(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd) || GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            if (GetWindowTextLength(hwnd) == 0) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) return false;
        if (IsIconic(found)) ShowWindow(found, 9 /* SW_RESTORE */);
        SetForegroundWindow(found);
        return true;
    }

    /// <summary>
    /// Asks a Claude to quit the way Windows does when it closes an app for an update or
    /// a sign-out. Claude answers that by quitting properly ("Windows session ending -
    /// quitting the app" in its log), running its own cleanup, where killing the process
    /// would cut off whatever it was writing. Returns how many windows were asked.
    /// </summary>
    public static int RequestQuit(int pid)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || GetWindowTextLength(hwnd) == 0) return true;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            // Only Electron's own windows; its helper windows have no handler for this.
            if (cls.ToString().StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)) windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        foreach (var hwnd in windows)
        {
            const int WM_QUERYENDSESSION = 0x11, WM_ENDSESSION = 0x16, ENDSESSION_CLOSEAPP = 1, SMTO_ABORTIFHUNG = 2;
            SendMessageTimeout(hwnd, WM_QUERYENDSESSION, IntPtr.Zero, ENDSESSION_CLOSEAPP, SMTO_ABORTIFHUNG, 3000, out _);
            SendMessageTimeout(hwnd, WM_ENDSESSION, new IntPtr(1), ENDSESSION_CLOSEAPP, SMTO_ABORTIFHUNG, 3000, out _);
        }
        return windows.Count;
    }

    public static bool IsRunning(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    public static void KillTree(int pid, List<ClaudeProcess> all)
    {
        foreach (var child in all.Where(p => p.ParentPid == pid)) KillTree(child.Pid, all);
        try { using var p = Process.GetProcessById(pid); p.Kill(); } catch (ArgumentException) { } catch (InvalidOperationException) { }
    }

    public static void AllowForeground() => AllowSetForegroundWindow(-1);

    static (string? image, string? cmd) Inspect(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return (null, null);
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            var image = QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;

            NtQueryInformationProcess(h, 60 /* ProcessCommandLineInformation */, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) needed = 8192;
            var buf = Marshal.AllocHGlobal(needed);
            try
            {
                if (NtQueryInformationProcess(h, 60, buf, needed, out _) != 0) return (image, null);
                // UNICODE_STRING: Length, MaximumLength, then the buffer pointer aligned to a pointer.
                var length = (ushort)Marshal.ReadInt16(buf);
                var text = Marshal.ReadIntPtr(buf, IntPtr.Size);
                return (image, Marshal.PtrToStringUni(text, length / 2));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { CloseHandle(h); }
    }

    static Dictionary<int, int> ParentMap()
    {
        var map = new Dictionary<int, int>();
        var snap = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snap, ref entry)) return map;
            do { map[entry.th32ProcessID] = entry.th32ParentProcessID; } while (Process32Next(snap, ref entry));
        }
        finally { CloseHandle(snap); }
        return map;
    }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public int dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buf, int len, out int ret);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(int flags, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, int flags, int timeout, out IntPtr result);
    static IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, int lParam, int flags, int timeout, out IntPtr result) =>
        SendMessageTimeout(hwnd, msg, wParam, new IntPtr(lParam), flags, timeout, out result);
}
