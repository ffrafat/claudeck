using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClauDeck;

/// <summary>One account as the window shows it. Raises a single change for everything.</summary>
// Public because XAML bindings name it; what it wraps stays internal.
public sealed class AccountViewModel : INotifyPropertyChanged
{
    internal AccountViewModel(Account account, AccountStatus status) { Account = account; Status = status; }

    internal Account Account { get; private set; }
    internal AccountStatus Status { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(Account account) { Account = account; Changed(); }
    public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public string Id => Account.Id;
    public string Name => Account.Name;
    public bool IsMain => Account.IsMain;
    public bool IsNotMain => !Account.IsMain;
    // Kept while the file stays the same, so a refresh does not reload the image.
    string? badgeFile;
    ImageSource? badge;
    public ImageSource Badge
    {
        get
        {
            var file = Icons.PngFileFor(Account, 64);
            if (file != badgeFile) { badgeFile = file; badge = new BitmapImage(new Uri(file)); }
            return badge!;
        }
    }

    // Details shows the account in ClauDeck; Start and Stop are about its Claude window.
    public bool IsOpen => Status.Pid != null;
    public bool Starting { get; set; }
    public bool Closing { get; set; }
    public bool ShowStart => !IsOpen && !Starting;
    public bool ShowStarting => Starting && !IsOpen;
    public bool ShowRunning => IsOpen;
    public bool CanStop => IsOpen && !Closing;
    public string RunningText => Closing ? "Stopping…" : "Running";
    public string StatusText => IsOpen ? "Running" : !Status.HasData ? "Not set up" : !Status.HasLogin ? "Signed out" : "Not running";
    /// <summary>Only what needs attention; running or not is shown by the Start and Stop controls.</summary>
    public string ProblemText => IsOpen ? "" : !Status.HasData ? "Not set up" : !Status.HasLogin ? "Signed out" : "";

    public string Subtitle
    {
        get
        {
            var who = Status.Email ?? (Status.HasLogin ? "Signed in" : Status.HasData ? "Not signed in" : "Not opened yet");
            return Status.Plan is { } p ? $"{who} · {char.ToUpper(p[0])}{p[1..]} plan" : who;
        }
    }

    public double SessionValue => Status.Usage?.Session ?? 0;
    public string SessionPercent => Format.Percent(Status.Usage?.Session);
    public string SessionResets => Format.Resets(Status.Usage?.SessionResets, Status.Usage);
    public bool SessionHigh => Status.Usage?.Session >= 90;
    public bool SessionRaised => Status.Usage?.Session is >= 70 and < 90;

    public double WeeklyValue => Status.Usage?.Weekly ?? 0;
    public string WeeklyPercent => Format.Percent(Status.Usage?.Weekly);
    public string WeeklyResets => Format.Resets(Status.Usage?.WeeklyResets, Status.Usage);
    public bool WeeklyHigh => Status.Usage?.Weekly >= 90;
    public bool WeeklyRaised => Status.Usage?.Weekly is >= 70 and < 90;

    public string LimitsSource => Status.Usage switch
    {
        null => Status.HasLogin ? "Reading limits…" : "Limits appear once this account is signed in.",
        { Live: true } u => $"Live from Anthropic, updated {Format.Ago(u.AsOf.LocalDateTime)}",
        var u => $"From Claude's own record, last written {Format.Ago(u.AsOf.LocalDateTime)}",
    } + (Status.UsageError != null ? $". Live figures unavailable: {Status.UsageError}" : "");

    public string ExtraUsage => Status.Usage?.ExtraUsageOn switch { true => "On", false => "Off", null => "Unknown" };
    public string WeekBreakdown => Status.Usage?.WeekBreakdown ?? "Nothing used yet this week";
    public string CodeChats => Status.HasData ? Status.CodeChats.ToString() : "—";
    public string LastUsed => IsOpen ? "Now" : Status.LastUsed is { } t && t > DateTime.MinValue ? Format.Ago(t) : "Never";
    public string Folder => Account.IsMain ? Account.DataDir : Account.Folder;
    public int ColorIndex => Icons.ColorIndex(Account);
    public string ColorName => Icons.Palette[ColorIndex].Name;
    public bool HasShortcut => !Account.IsMain && Shortcuts.HasAccountShortcut(Account);
}

/// <summary>
/// The accounts, their status and the polling behind them. Everything here runs on
/// the window's thread except the work handed to Task.Run, whose results come back.
/// </summary>
sealed class Hub
{
    public LauncherState State { get; private set; }
    public ObservableCollection<AccountViewModel> Accounts { get; } = new();
    public bool Watched { get; set; }
    public bool IsDemo { get; }

    public event Action? ListChanged;
    public event Action<Account, Verification>? WrongFolder;
    public event Action<AccountViewModel, int>? CloseTimedOut;
    public event Action<string>? Notice;

    readonly Dictionary<string, AccountStatus> statuses = new();
    readonly DispatcherQueue ui = DispatcherQueue.GetForCurrentThread();
    readonly DispatcherQueueTimer processTimer, usageTimer;
    bool scanning, fetching;

    public Hub(bool demo)
    {
        IsDemo = demo;
        State = demo ? Demo.State() : Store.Load();
        if (demo) foreach (var (id, s) in Demo.Statuses()) statuses[id] = s;
        processTimer = ui.CreateTimer();
        processTimer.Interval = TimeSpan.FromSeconds(3);
        processTimer.Tick += (_, _) => ScanProcesses();
        usageTimer = ui.CreateTimer();
        usageTimer.Interval = TimeSpan.FromSeconds(30);
        usageTimer.Tick += (_, _) => FetchDue();
        Rebuild();
        if (demo) return;   // sample figures stay as they are; nothing real is looked at
        processTimer.Start();
        usageTimer.Start();
        RefreshAll(force: false);
    }

    /// <summary>True, after saying so, when this is the demo and nothing should really happen.</summary>
    bool DemoOnly()
    {
        if (IsDemo) Notice?.Invoke("This is the demo: nothing is started, stopped or changed.");
        return IsDemo;
    }

    public AccountViewModel? Find(string id) => Accounts.FirstOrDefault(a => a.Id == id);

    AccountStatus StatusOf(string id) => statuses.TryGetValue(id, out var s) ? s : statuses[id] = new AccountStatus();

    void Rebuild()
    {
        var wanted = State.All().ToList();
        foreach (var gone in Accounts.Where(vm => wanted.All(a => a.Id != vm.Id)).ToList()) Accounts.Remove(gone);
        for (var i = 0; i < wanted.Count; i++)
        {
            var existing = Find(wanted[i].Id);
            if (existing == null) Accounts.Insert(i, new AccountViewModel(wanted[i], StatusOf(wanted[i].Id)));
            else
            {
                existing.Update(wanted[i]);
                var at = Accounts.IndexOf(existing);
                if (at != i) Accounts.Move(at, i);
            }
        }
        ListChanged?.Invoke();
    }

    // The demo's changes live only as long as the window.
    void Save() { if (!IsDemo) Store.Save(State); Rebuild(); }

    // -------------------------------------------------------------- polling --

    public void ScanProcesses()
    {
        if (scanning || IsDemo) return;
        scanning = true;
        var accounts = State.All().ToList();
        Task.Run(() =>
        {
            var running = Processes.RunningByDataDir(Processes.Scan());
            var pids = accounts.ToDictionary(a => a.Id, a => running.TryGetValue(Processes.Normalize(a.DataDir), out var pid) ? pid : (int?)null);
            // Every Claude puts the same icon in the notification area; each extra
            // account's is repainted in its avatar colour so the icons can be told apart.
            foreach (var a in accounts)
                if (!a.IsMain && pids[a.Id] is int pid) TrayTint.Apply(a, pid);
            return pids;
        }).ContinueWith(t => ui.TryEnqueue(() =>
        {
            scanning = false;
            if (!t.IsCompletedSuccessfully) return;
            foreach (var (id, pid) in t.Result)
            {
                var s = StatusOf(id);
                if (s.Pid == pid) continue;
                s.Pid = pid;
                Find(id)?.Changed();
            }
        }));
    }

    public void RefreshAll(bool force)
    {
        var now = DateTimeOffset.Now;
        foreach (var a in State.All())
        {
            var s = StatusOf(a.Id);
            // A press asks now, but a double press is still one request.
            if (force ? now - s.LastFetch > TimeSpan.FromSeconds(5) && s.Failures < 3 : now - s.LastFetch > TimeSpan.FromMinutes(1))
                s.NextFetch = DateTimeOffset.MinValue;
        }
        ScanProcesses();
        FetchDue();
    }

    void FetchDue()
    {
        if (fetching || IsDemo) return;
        var due = State.All().Select(a => (a, s: StatusOf(a.Id))).Where(x => x.s.NextFetch <= DateTimeOffset.Now).ToList();
        if (due.Count == 0) return;
        fetching = true;
        var watched = Watched;
        Task.Run(async () =>
        {
            foreach (var (a, s) in due)
            {
                await Refresh.FetchOne(a, s, watched);
                ui.TryEnqueue(() => Find(a.Id)?.Changed());
            }
        }).ContinueWith(_ => ui.TryEnqueue(() => fetching = false));
    }

    // -------------------------------------------------------------- actions --

    /// <summary>
    /// Starts the account's Claude, or brings it forward if it is already running: the
    /// same thing from either button, since a second copy on one folder would lose chats.
    /// </summary>
    public void Start(Account account)
    {
        if (DemoOnly()) return;
        var vm = Find(account.Id);
        var running = vm?.IsOpen == true;
        Notice?.Invoke(running ? $"Bringing {account.Name} to the front…" : $"Starting {account.Name}…");
        if (vm != null && !running) { vm.Starting = true; vm.Changed(); }
        var state = State;
        Task.Run(async () =>
        {
            var opened = ClaudeApp.Open(state, account);
            return (opened, verified: await ClaudeApp.Verify(account, opened, TimeSpan.FromSeconds(25)));
        }).ContinueWith(t => ui.TryEnqueue(() =>
        {
            if (vm != null) { vm.Starting = false; vm.Changed(); }
            if (t.IsFaulted) { Notice?.Invoke($"Could not start {account.Name}: {t.Exception!.InnerException!.Message}"); return; }
            var (opened, verified) = t.Result;
            Notice?.Invoke(opened.Outcome == OpenOutcome.Started ? $"Started {account.Name}." : $"{account.Name} is in front.");
            if (!verified.Ok) WrongFolder?.Invoke(account, verified);
            ScanProcesses();
        }));
    }

    public void BringToFront(Account account) => Start(account);

    public void CloseWindow(int pid) => Processes.KillTree(pid, Processes.Scan());

    /// <summary>Quits an account's Claude completely, tray icon included.</summary>
    public void Stop(AccountViewModel vm)
    {
        if (DemoOnly() || vm.Status.Pid is not int pid) return;
        vm.Closing = true;
        vm.Changed();
        Notice?.Invoke($"Stopping {vm.Name}…");
        Task.Run(async () =>
        {
            Processes.RequestQuit(pid);
            // Claude finishes its own cleanup first, which takes a few seconds when a
            // Code session is running in it.
            for (var i = 0; i < 40 && Processes.IsRunning(pid); i++) await Task.Delay(500);
            return !Processes.IsRunning(pid);
        }).ContinueWith(t => ui.TryEnqueue(() =>
        {
            vm.Closing = false;
            if (t.IsCompletedSuccessfully && t.Result) Notice?.Invoke($"Stopped {vm.Name}.");
            else CloseTimedOut?.Invoke(vm, pid);
            ScanProcesses();
            vm.Changed();
        }));
    }

    public void ForceStop(AccountViewModel vm, int pid)
    {
        Processes.KillTree(pid, Processes.Scan());
        Notice?.Invoke($"Stopped {vm.Name}.");
        ScanProcesses();
    }

    public Account Add(string name, bool shortcut)
    {
        var account = new Account { Id = Store.NewId(State, name), Name = name.Trim(), Color = Store.NextColor(State), Shortcut = shortcut };
        State.Accounts.Add(account);
        Directory.CreateDirectory(account.DataDir);
        Directory.CreateDirectory(account.LocalDir);
        Save();
        if (shortcut) Shortcuts.CreateForAccount(account);
        return account;
    }

    public void Rename(Account account, string name)
    {
        if (DemoOnly()) return;
        name = name.Trim();
        if (account.IsMain) State.MainName = name;
        else
        {
            var stored = State.Accounts.First(x => x.Id == account.Id);
            var hadShortcut = Shortcuts.HasAccountShortcut(stored);
            if (hadShortcut) Shortcuts.RemoveForAccount(stored);
            stored.Name = name;
            if (hadShortcut) Shortcuts.CreateForAccount(stored);
        }
        Save();
    }

    public void SetColor(Account account, int color)
    {
        if (DemoOnly()) return;
        if (account.IsMain) State.MainColor = color;
        else
        {
            var stored = State.Accounts.First(x => x.Id == account.Id);
            stored.Color = color;
            // The shortcut's icon file is named for its colour, so it needs pointing at the new one.
            if (Shortcuts.HasAccountShortcut(stored)) Shortcuts.CreateForAccount(stored);
        }
        Save();
        ScanProcesses();   // repaints a running window's taskbar button straight away
    }

    public void SetShortcut(Account account, bool on)
    {
        if (DemoOnly()) return;
        var stored = State.Accounts.First(x => x.Id == account.Id);
        if (on) Shortcuts.CreateForAccount(stored); else Shortcuts.RemoveForAccount(stored);
        stored.Shortcut = on;
        Save();
    }

    public void Remove(Account account)
    {
        if (DemoOnly()) return;
        var stored = State.Accounts.First(x => x.Id == account.Id);
        Shortcuts.RemoveForAccount(stored);
        State.Accounts.Remove(stored);
        statuses.Remove(account.Id);
        Save();
    }

    public void SaveSettings() { if (!IsDemo) Store.Save(State); }
}
