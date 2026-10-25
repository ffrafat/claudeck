using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClauDeck;

/// <summary>
/// The modes that run without a window: what shortcuts, the installer and scripts call.
/// </summary>
static class Cli
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        try
        {
            switch (args)
            {
                case ["--write-icon", var iconPath]: exitCode = WriteIcon(iconPath); return true;
                case ["--open", var id]: exitCode = OpenFromShortcut(id); return true;
                case ["--import", var name, var folder]: exitCode = Import(name, folder); return true;
                case ["--install"]: exitCode = Install(appShortcuts: true); return true;
                // What the installer runs: it makes the app's own shortcuts itself, as the person chose.
                case ["--migrate"]: exitCode = Install(appShortcuts: false); return true;
                case ["--report", var reportPath]: exitCode = Report(reportPath); return true;
                case ["--uninstall", ..]: exitCode = Uninstall(purge: args.Contains("--purge")); return true;
                case ["--write-branding", var brandingDir]: Branding.Write(brandingDir); return true;
                default: return false;
            }
        }
        catch (Exception e)
        {
            Box(e.Message, 0x10 /* MB_ICONERROR */);
            exitCode = 1;
            return true;
        }
    }

    static int WriteIcon(string path)
    {
        ClaudeApp.ResolveExe(Store.Load());
        Icons.WriteAppIcon(path);
        return 0;
    }

    /// <summary>What a per-account shortcut runs: open that account, check it, and leave.</summary>
    static int OpenFromShortcut(string id)
    {
        var state = Store.Load();
        var account = state.Find(id) ?? throw new InvalidOperationException(
            $"There is no Claude account called '{id}' any more. Open {Brand.Name} to see the ones there are.");
        var opened = ClaudeApp.Open(state, account);
        var verified = ClaudeApp.Verify(account, opened, TimeSpan.FromSeconds(25)).GetAwaiter().GetResult();
        if (verified.Ok) return 0;
        var answer = Box(WrongFolderText(account, verified), 0x30u /* MB_ICONWARNING */ | (verified.Pid != null ? 4u /* MB_YESNO */ : 0u));
        if (answer == 6 /* IDYES */ && verified.Pid is int pid) Processes.KillTree(pid, Processes.Scan());
        return 2;
    }

    public static string WrongFolderText(Account account, Verification v) =>
        $"Claude did not open '{account.Name}' in its own folder. It used {v.ActualDir} instead, so the window " +
        "may be showing a different account.\r\n\r\nDon't sign in or out in that window: it could sign another " +
        "account out." + (v.Pid != null ? "\r\n\r\nClose that window now?" : "");

    /// <summary>
    /// Brings an existing Claude profile folder in as an account. It is copied, never
    /// moved, so the original stays exactly as it was. Chromium's saved logins are
    /// encrypted with a key Windows keeps for this user, not for the folder, so a copy
    /// stays signed in.
    /// </summary>
    static int Import(string name, string source)
    {
        var state = Store.Load();
        if (!File.Exists(Path.Combine(source, "config.json"))) throw new InvalidOperationException($"{source} is not a Claude profile folder.");
        if (Store.ValidateName(state, name) is { } problem) throw new InvalidOperationException(problem);
        if (Processes.RunningByDataDir(Processes.Scan()).ContainsKey(Processes.Normalize(source)))
            throw new InvalidOperationException("That profile is open in Claude. Close it first.");

        var account = new Account { Id = Store.NewId(state, name), Name = name, Color = Store.NextColor(state), Shortcut = true };
        if (Directory.Exists(account.DataDir) && Directory.EnumerateFileSystemEntries(account.DataDir).Any())
            throw new InvalidOperationException($"{account.DataDir} already holds a profile.");
        Directory.CreateDirectory(account.LocalDir);
        using var copy = Process.Start(new ProcessStartInfo("robocopy.exe",
            $"\"{source}\" \"{account.DataDir}\" /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XJ /NFL /NDL /NJH /NJS /NP")
        { UseShellExecute = false, CreateNoWindow = true })!;
        copy.WaitForExit();
        // robocopy: 0-7 are kinds of success, 8 and above mean something was not copied.
        if (copy.ExitCode >= 8) throw new IOException($"Copying the profile failed (robocopy exit code {copy.ExitCode}).");
        state.Accounts.Add(account);
        Store.Save(state);
        Shortcuts.CreateForAccount(account);
        return 0;
    }

    /// <summary>Desktop and Start menu shortcuts for the launcher itself.</summary>
    static int Install(bool appShortcuts)
    {
        var state = Store.Load();
        try { ClaudeApp.ResolveExe(state); } catch (FileNotFoundException) { }   // Claude may be installed later
        Directory.CreateDirectory(Paths.IconDir);
        var icon = Brand.IconFile;
        Icons.WriteAppIcon(icon);
        var startedWithWindows = Shortcuts.RemoveOldAppShortcuts();
        if (appShortcuts) Shortcuts.CreateForApp(icon);
        // Account shortcuts carry this exe's path, so they are written again in case it
        // moved; and put back for accounts that had one before an uninstall took it away.
        foreach (var a in state.Accounts)
        {
            if (Shortcuts.HasAccountShortcut(a)) a.Shortcut = true;
            if (a.Shortcut) Shortcuts.CreateForAccount(a);
        }
        if (File.Exists(Paths.StateFile)) Store.Save(state);
        if (startedWithWindows || File.Exists(Shortcuts.StartupFile)) Shortcuts.SetStartWithWindows(true, icon);
        return 0;
    }

    /// <summary>
    /// Run by the uninstaller before it removes the program: takes away every shortcut
    /// ClauDeck made, which the installer did not make and so cannot know about. With
    /// --purge, the person chose to delete their accounts' data too.
    /// </summary>
    static int Uninstall(bool purge)
    {
        var state = Store.Load();
        foreach (var a in state.Accounts) Shortcuts.RemoveForAccount(a);
        Shortcuts.SetStartWithWindows(false, Brand.IconFile);
        if (purge)
        {
            // Never anything outside the accounts folder, and never while one of its Claudes runs.
            var running = Processes.RunningByDataDir(Processes.Scan());
            foreach (var a in state.Accounts.Where(a => running.ContainsKey(Processes.Normalize(a.DataDir))))
                if (running.TryGetValue(Processes.Normalize(a.DataDir), out var pid)) Processes.KillTree(pid, Processes.Scan());
            if (Directory.Exists(Paths.Root)) Directory.Delete(Paths.Root, recursive: true);
        }
        return 0;
    }

    /// <summary>The window's figures as text, for checking them from a script.</summary>
    static int Report(string path)
    {
        var state = Store.Load();
        var running = Processes.RunningByDataDir(Processes.Scan());
        var lines = new List<string>();
        foreach (var a in state.All())
        {
            var s = new AccountStatus();
            Refresh.FetchOne(a, s, watched: false).GetAwaiter().GetResult();
            var open = running.ContainsKey(Processes.Normalize(a.DataDir));
            lines.Add($"{a.Name} [{a.Id}]: {(open ? "open" : "closed")}, {(s.HasLogin ? "signed in" : "not signed in")}" +
                      $", email {(s.Email != null ? "known" : "unknown")}, plan {s.Plan ?? "-"}, {s.CodeChats} Code chats");
            lines.Add($"    session: {Format.Limit(s.Usage?.Session, s.Usage?.SessionResets, s.Usage)}");
            lines.Add($"    weekly:  {Format.Limit(s.Usage?.Weekly, s.Usage?.WeeklyResets, s.Usage)}");
            lines.Add($"    source:  {(s.Usage == null ? "none" : s.Usage.Live ? "live" : "local file")}{(s.UsageError != null ? $" (live failed: {s.UsageError})" : "")}");
            if (s.Usage?.WeekBreakdown != null) lines.Add($"    this week: {s.Usage.WeekBreakdown}");
        }
        File.WriteAllLines(path, lines);
        return 0;
    }

    static int Box(string text, uint flags) => MessageBoxW(IntPtr.Zero, text, Brand.Name, flags | 0x10000 /* MB_SETFOREGROUND */);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}
