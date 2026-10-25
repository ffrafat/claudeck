using System.Collections;
using System.Diagnostics;

namespace ClauDeck;

enum OpenOutcome { Focused, Started, Handed }

sealed record OpenResult(OpenOutcome Outcome, int? Pid);

/// <summary>The data folder a newly opened Claude settled on, and whether that is the one asked for.</summary>
sealed record Verification(bool Ok, string? ActualDir, int? Pid);

static class ClaudeApp
{
    /// <summary>
    /// Claude.exe inside the Store package. Its folder carries the version, so it moves
    /// with every update; a cached path is used only while it still exists.
    /// </summary>
    public static string ResolveExe(LauncherState state)
    {
        if (state.ClaudeExe is { } cached && File.Exists(cached)) return cached;
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -Command \"(Get-AppxPackage -Name Claude | Select-Object -First 1).InstallLocation\"")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var location = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(15000);
        var exe = location.Length > 0 ? Path.Combine(location, "app", "Claude.exe") : "";
        if (!File.Exists(exe))
        {
            var classic = Path.Combine(Paths.LocalAppData, "AnthropicClaude", "claude.exe");
            exe = File.Exists(classic) ? classic : throw new FileNotFoundException("Claude Desktop does not seem to be installed for this Windows user.");
        }
        state.ClaudeExe = exe;
        Store.Save(state);
        return exe;
    }

    /// <summary>Claude_&lt;publisher&gt;!Claude, read off ...\WindowsApps\Claude_&lt;version&gt;_x64__&lt;publisher&gt;\app\Claude.exe.</summary>
    static string? AppUserModelId(string exe)
    {
        var package = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(exe)));
        var at = package?.LastIndexOf("__", StringComparison.Ordinal) ?? -1;
        return at > 0 ? $"Claude_{package![(at + 2)..]}!Claude" : null;
    }

    public static OpenResult Open(LauncherState state, Account account)
    {
        var running = Processes.RunningByDataDir(Processes.Scan());
        if (running.TryGetValue(Processes.Normalize(account.DataDir), out var pid) && Processes.Focus(pid))
            return new(OpenOutcome.Focused, pid);
        var already = running.ContainsKey(Processes.Normalize(account.DataDir));

        var exe = ResolveExe(state);
        // Our click is what lets Claude take the foreground; pass that on to it.
        Processes.AllowForeground();
        if (account.IsMain)
        {
            var aumid = AppUserModelId(exe);
            // Through the shell, so the ordinary Claude keeps its package identity and
            // its claude:// links, exactly as when it is opened from the Start menu.
            Process.Start(new ProcessStartInfo("explorer.exe", aumid != null ? $"shell:AppsFolder\\{aumid}" : $"\"{exe}\"") { UseShellExecute = false });
            return new(already ? OpenOutcome.Handed : OpenOutcome.Started, null);
        }

        Directory.CreateDirectory(account.DataDir);
        Directory.CreateDirectory(account.LocalDir);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Paths.UserProfile };
        psi.ArgumentList.Add($"--user-data-dir={account.DataDir}");
        psi.Environment.Clear();
        foreach (var (k, v) in CleanEnvironment()) psi.Environment[k] = v;
        psi.Environment["LOCALAPPDATA"] = account.LocalDir;
        using var started = Process.Start(psi)!;
        // A Claude already running on this folder but hidden in the tray answers the
        // second launch by showing its window, and the new process exits.
        return new(already ? OpenOutcome.Handed : OpenOutcome.Started, started.Id);
    }

    /// <summary>
    /// Waits for the new Claude's helper processes and reads the folder they were handed.
    /// That is the folder Chromium actually opened, after any relocation Claude did.
    /// </summary>
    public static async Task<Verification> Verify(Account account, OpenResult opened, TimeSpan timeout)
    {
        if (opened.Pid is not int pid || account.IsMain) return new(true, null, opened.Pid);
        var wanted = Processes.Normalize(account.DataDir);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            var all = Processes.Scan();
            if (!all.Any(p => p.Pid == pid))
            {
                // Expected when this account was already open in the tray. Otherwise the
                // only Claude it can have handed over to is one on a different folder,
                // which is how a profile switcher once opened an account on the main account's data.
                if (opened.Outcome == OpenOutcome.Handed) return new(true, null, null);
                var mine = Processes.RunningByDataDir(all).ContainsKey(wanted);
                return mine ? new(true, wanted, null) : new(false, "another Claude window that was already open", null);
            }
            var dirs = all.Where(p => p.ParentPid == pid && p.UserDataDir != null).Select(p => p.UserDataDir!).Distinct().ToList();
            if (dirs.Count == 0) continue;
            return dirs.All(d => d == wanted) ? new(true, wanted, pid) : new(false, dirs.First(d => d != wanted), pid);
        }
        return new(true, null, pid);
    }

    // The environment Explorer would give a program started from the Desktop: the
    // registry's variables plus the per-logon ones. None of a parent Claude's own
    // variables, such as the session token it hands to Claude Code, get through.
    static readonly string[] PerLogon =
    {
        "ALLUSERSPROFILE", "APPDATA", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432",
        "COMPUTERNAME", "HOMEDRIVE", "HOMEPATH", "LOGONSERVER", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
        "ProgramW6432", "PUBLIC", "SESSIONNAME", "SystemDrive", "SystemRoot", "USERDOMAIN", "USERDOMAIN_ROAMINGPROFILE",
        "USERNAME", "USERPROFILE", "windir",
    };

    static Dictionary<string, string> CleanEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
            foreach (DictionaryEntry e in Environment.GetEnvironmentVariables(target))
                env[(string)e.Key] = e.Value as string ?? "";
        env["Path"] = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) + ";" +
                      Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        foreach (var k in PerLogon)
            if (Environment.GetEnvironmentVariable(k) is { Length: > 0 } v) env[k] = v;
        env["APPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        env["USERPROFILE"] = Paths.UserProfile;
        return env;
    }
}
