using System.Runtime.InteropServices;

namespace ClauDeck;

static class Shortcuts
{
    static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    static string StartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
    static string Startup => Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    static string Exe => Environment.ProcessPath!;

    // What this app was called before it was ClauDeck, and the exe each name ran.
    static readonly (string Name, string Exe)[] OldNames = { ("Claude Accounts", "ClaudeAccounts.exe"), ("ClawDeck", "ClawDeck.exe") };

    static string AccountFile(string dir, Account a) => Path.Combine(dir, $"Claude - {a.Name}.lnk");
    public static string StartupFile => Path.Combine(Startup, $"{Brand.Name}.lnk");

    public static bool HasAccountShortcut(Account a) => File.Exists(AccountFile(Desktop, a)) || File.Exists(AccountFile(StartMenu, a));

    public static void CreateForAccount(Account a)
    {
        foreach (var dir in new[] { Desktop, StartMenu })
            Write(AccountFile(dir, a), $"--open {a.Id}", Icons.IconFileFor(a), $"Open Claude with the '{a.Name}' account");
    }

    public static void RemoveForAccount(Account a)
    {
        foreach (var dir in new[] { Desktop, StartMenu })
            if (File.Exists(AccountFile(dir, a))) File.Delete(AccountFile(dir, a));
    }

    public static void CreateForApp(string iconFile)
    {
        foreach (var dir in new[] { Desktop, StartMenu })
            Write(Path.Combine(dir, $"{Brand.Name}.lnk"), "", iconFile, Brand.Tagline);
    }

    public static void SetStartWithWindows(bool on, string iconFile)
    {
        if (on) Write(StartupFile, "--tray", iconFile, $"{Brand.Name} in the notification area");
        else if (File.Exists(StartupFile)) File.Delete(StartupFile);
    }

    /// <summary>
    /// Takes away the shortcuts the app had under its old name, which point at an exe
    /// that is no longer there. Returns whether one of them started it with Windows,
    /// so that choice carries over. Only shortcuts that point at this app are touched.
    /// </summary>
    public static bool RemoveOldAppShortcuts()
    {
        var startedWithWindows = false;
        foreach (var (name, exe) in OldNames)
            foreach (var dir in new[] { Desktop, StartMenu, Startup })
            {
                var file = Path.Combine(dir, $"{name}.lnk");
                if (!File.Exists(file) || !(Target(file)?.EndsWith($@"\{exe}", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                if (dir == Startup) startedWithWindows = true;
                File.Delete(file);
            }
        return startedWithWindows;
    }

    static string? Target(string path) => Com(shell =>
    {
        dynamic link = shell.CreateShortcut(path);
        try { return (string)link.TargetPath; }
        finally { Marshal.FinalReleaseComObject(link); }
    });

    static void Write(string path, string args, string icon, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Com<object?>(shell =>
        {
            dynamic link = shell.CreateShortcut(path);
            try
            {
                link.TargetPath = Exe;
                link.Arguments = args;
                link.WorkingDirectory = Path.GetDirectoryName(Exe);
                link.IconLocation = icon + ",0";
                link.Description = description;
                link.Save();
            }
            finally { Marshal.FinalReleaseComObject(link); }
            return null;
        });
    }

    static T Com<T>(Func<dynamic, T> use)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available");
        dynamic shell = Activator.CreateInstance(type)!;
        try { return use(shell); }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
