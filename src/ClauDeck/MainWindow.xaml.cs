using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;

namespace ClauDeck;

public sealed partial class MainWindow : Window
{
    readonly string appIconFile;
    readonly TrayIcon? tray;   // none in the demo, which must not look like a running ClauDeck
    readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool exiting;
    string? selectedTag;

    internal MainWindow()
    {
        InitializeComponent();

        appIconFile = Brand.IconFile;
        if (!File.Exists(appIconFile)) { Directory.CreateDirectory(Paths.IconDir); Icons.WriteAppIcon(appIconFile); }
        AppWindow.SetIcon(appIconFile);
        TitleIcon.Source = new BitmapImage(new Uri(Icons.AppPng(32)));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        Root.ActualThemeChanged += (_, _) => ColourCaptionButtons();

        Place();
        ApplyTheme(App.DemoOptions.Theme ?? App.Hub.State.Theme);
        ApplyBackdrop(App.Hub.State.Backdrop);

        AppWindow.Closing += (_, e) =>
        {
            if (exiting || tray == null) return;
            // Closing the window keeps the launcher in the tray, like the old one.
            e.Cancel = true;
            HideToTray();
        };

        App.Hub.ListChanged += BuildMenu;
        App.Hub.Notice += ShowNotice;
        App.Hub.WrongFolder += (a, v) => _ = WarnWrongFolder(a, v);
        App.Hub.CloseTimedOut += (vm, pid) => _ = OfferForceStop(vm, pid);
        foreach (var vm in App.Hub.Accounts) vm.PropertyChanged += (_, _) => OnAccountChanged();
        App.Hub.Accounts.CollectionChanged += (_, e) =>
        {
            foreach (AccountViewModel vm in e.NewItems ?? Array.Empty<AccountViewModel>()) vm.PropertyChanged += (_, _) => OnAccountChanged();
        };
        noticeTimer.Tick += (_, _) => { Notice.IsOpen = false; noticeTimer.Stop(); };

        if (!App.Hub.IsDemo) tray = new TrayIcon(appIconFile, ShowFront, TrayMenu);
        BuildMenu();
        Select(App.DemoOptions.Page ?? "overview");
    }

    // ---------------------------------------------------------------- window --

    void Place()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var size = new SizeInt32((int)(1120 * scale), (int)(760 * scale));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        size = new SizeInt32(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)(720 * scale);
            p.PreferredMinimumHeight = (int)(520 * scale);
        }
    }

    internal void ShowFront()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        App.Hub.Watched = true;
        App.Hub.RefreshAll(force: false);
    }

    void HideToTray()
    {
        AppWindow.Hide();
        App.Hub.Watched = false;
        if (!App.Hub.State.TrayHintShown)
        {
            tray?.Balloon("Still here", $"{Brand.Name} is in the notification area. Right-click it to open an account or exit.");
            App.Hub.State.TrayHintShown = true;
            App.Hub.SaveSettings();
        }
    }

    internal void Exit()
    {
        exiting = true;
        tray?.Dispose();
        Application.Current.Exit();
    }

    internal void ApplyTheme(string theme)
    {
        Root.RequestedTheme = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        ColourCaptionButtons();
    }

    internal void ApplyBackdrop(string kind)
    {
        SystemBackdrop = kind switch
        {
            "acrylic" => new DesktopAcrylicBackdrop(),
            "micaalt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            _ => new MicaBackdrop { Kind = MicaKind.Base },
        };
    }

    // With the content drawn under the title bar, the minimise, maximise and close
    // buttons keep their own colours unless told the theme changed.
    void ColourCaptionButtons()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(255, 150, 150, 150) : Color.FromArgb(255, 120, 120, 120);
        bar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
        bar.ButtonHoverForegroundColor = bar.ButtonForegroundColor;
        bar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);
        bar.ButtonPressedForegroundColor = bar.ButtonForegroundColor;
    }

    // ------------------------------------------------------------ navigation --

    void BuildMenu()
    {
        Nav.MenuItems.Clear();
        Nav.MenuItems.Add(new NavigationViewItem { Content = "Overview", Tag = "overview", Icon = new SymbolIcon(Symbol.Home) });
        Nav.MenuItems.Add(new NavigationViewItemHeader { Content = "Accounts" });
        foreach (var vm in App.Hub.Accounts)
        {
            var item = new NavigationViewItem { Content = vm.Name, Tag = vm.Id, Icon = new ImageIcon { Source = vm.Badge } };
            ToolTipService.SetToolTip(item, vm.Subtitle);
            item.ContextFlyout = AccountMenu(vm.Id);
            Nav.MenuItems.Add(item);
        }
        OnAccountChanged();
        // The list was rebuilt underneath the selection; put it back, or fall back to the overview.
        if (selectedTag != null && selectedTag != "settings") Select(App.Hub.Find(selectedTag) != null || selectedTag == "overview" ? selectedTag : "overview");
    }

    void OnAccountChanged()
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is not string id || App.Hub.Find(id) is not { } vm) continue;
            if ((string)item.Content != vm.Name) item.Content = vm.Name;
            item.InfoBadge = vm.IsOpen ? new InfoBadge { Style = (Style)Application.Current.Resources["SuccessDotInfoBadgeStyle"] } : null;
            ToolTipService.SetToolTip(item, $"{vm.Subtitle}\n{vm.StatusText} · session {vm.SessionPercent} · week {vm.WeeklyPercent}");
        }
        if (tray != null) tray.Tooltip = $"{Brand.Name}\n" + string.Join("\n", App.Hub.Accounts.Select(a => $"{a.Name}: {a.SessionPercent} · week {a.WeeklyPercent}"));
    }

    /// <summary>The right-click menu on an account in the sidebar, built as it opens so it matches the moment.</summary>
    internal MenuFlyout AccountMenu(string id)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (App.Hub.Find(id) is not { } vm) return;
            MenuFlyoutItem Item(string text, string glyph, Action click)
            {
                var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
                item.Click += (_, _) => click();
                return item;
            }
            if (vm.IsOpen)
            {
                menu.Items.Add(Item("Bring to front", "", () => App.Hub.BringToFront(vm.Account)));
                if (vm.CanStop) menu.Items.Add(Item("Stop", "", () => _ = Stop(vm)));
            }
            else menu.Items.Add(Item("Start", "", () => App.Hub.Start(vm.Account)));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("Rename", "", () => _ = Rename(vm)));
            menu.Items.Add(ColourMenu(vm));
            if (!vm.IsMain)
            {
                var shortcut = new ToggleMenuFlyoutItem { Text = "Desktop and Start shortcut", IsChecked = vm.HasShortcut, Icon = new FontIcon { Glyph = "" } };
                shortcut.Click += (_, _) => App.Hub.SetShortcut(vm.Account, shortcut.IsChecked);
                menu.Items.Add(shortcut);
            }
            menu.Items.Add(Item("Open folder", "", () => OpenFolder(vm)));
            if (!vm.IsMain)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(Item("Remove from ClauDeck", "", () => _ = Remove(vm)));
            }
        };
        return menu;
    }

    /// <summary>"Colour", with each avatar colour shown as the avatar it would make.</summary>
    static MenuFlyoutSubItem ColourMenu(AccountViewModel vm)
    {
        var sub = new MenuFlyoutSubItem { Text = "Colour", Icon = new FontIcon { Glyph = "" } };
        for (var i = 0; i < Icons.Palette.Length; i++)
        {
            var index = i;
            var item = new ToggleMenuFlyoutItem
            {
                Text = Icons.Palette[i].Name,
                IsChecked = i == vm.ColorIndex,
                Icon = new ImageIcon { Source = new BitmapImage(new Uri(Icons.SwatchPng(i, 32))) },
            };
            item.Click += (_, _) => App.Hub.SetColor(vm.Account, index);
            sub.Items.Add(item);
        }
        return sub;
    }

    internal void OpenFolder(AccountViewModel vm)
    {
        if (Directory.Exists(vm.Folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{vm.Folder}\"") { UseShellExecute = false });
    }

    internal void Select(string tag)
    {
        if (tag == "settings") { Nav.SelectedItem = Nav.SettingsItem; return; }
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag);
        if (item != null) Nav.SelectedItem = item;
    }

    void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag == null || tag == selectedTag) return;
        selectedTag = tag;
        switch (tag)
        {
            case "overview": ContentFrame.Navigate(typeof(OverviewPage)); break;
            case "settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
            case "about": ContentFrame.Navigate(typeof(AboutPage)); break;
            default: ContentFrame.Navigate(typeof(AccountPage), tag); break;
        }
    }

    void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag as string == "add") _ = AddAccount();
    }

    // --------------------------------------------------------------- dialogs --

    ContentDialog Dialog(string title, object content, string primary, string? close = "Cancel") => new()
    {
        XamlRoot = Content.XamlRoot, Title = title, Content = content, PrimaryButtonText = primary,
        CloseButtonText = close, DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.ActualTheme,
    };

    internal async Task AddAccount()
    {
        ShowFront();
        if (App.Hub.IsDemo) { ShowNotice("This is the demo: nothing is started, stopped or changed."); return; }
        var name = new TextBox { Header = "Name", PlaceholderText = "For example, Work", MinWidth = 360 };
        var shortcut = new CheckBox { Content = "Put a shortcut for it on the Desktop and in Start", IsChecked = true };
        var problem = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "A new Claude window will open at its sign-in screen. Sign in there with the other account.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(name);
        body.Children.Add(shortcut);
        body.Children.Add(problem);
        var dialog = Dialog("Add an account", body, "Add and open");
        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (Store.ValidateName(App.Hub.State, name.Text) is { } p) { problem.Text = p; e.Cancel = true; }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var account = App.Hub.Add(name.Text, shortcut.IsChecked == true);
        Select(account.Id);
        App.Hub.Start(account);
        ShowNotice($"Sign in to {account.Name} in the new Claude window. If your browser opens to finish signing in, " +
                   "leave your other Claude windows alone until it is done.", InfoBarSeverity.Informational, sticky: true);
    }

    internal async Task Rename(AccountViewModel vm)
    {
        var name = new TextBox { Header = "Name", Text = vm.Name, MinWidth = 360 };
        var problem = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "Only the name changes. Its sign-in and chats stay as they are.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(name);
        body.Children.Add(problem);
        name.SelectAll();
        var dialog = Dialog($"Rename {vm.Name}", body, "Rename");
        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (name.Text.Trim() == vm.Name) return;
            if (Store.ValidateName(App.Hub.State, name.Text, vm.Account) is { } p) { problem.Text = p; e.Cancel = true; }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && name.Text.Trim() != vm.Name) App.Hub.Rename(vm.Account, name.Text);
    }

    internal async Task Remove(AccountViewModel vm)
    {
        if (vm.IsOpen)
        {
            await Dialog("Close it first", $"Close the {vm.Name} window, then remove it.", "OK", null).ShowAsync();
            return;
        }
        var text = $"{vm.Name} leaves this list and loses its shortcuts. Its folder stays at {vm.Folder}, sign-in included, " +
                   "so nothing is lost. Delete that folder yourself if you want it gone.";
        var dialog = Dialog($"Remove {vm.Name}?", new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, "Remove");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        App.Hub.Remove(vm.Account);
        Select("overview");
    }

    internal async Task Stop(AccountViewModel vm)
    {
        if (!vm.CanStop) return;
        var text = $"This quits {vm.Name}'s Claude completely, including its icon in the notification area. " +
                   "Any Claude Code or Cowork task running in it stops. Its sign-in and chats are kept." +
                   (vm.IsMain ? "\r\n\r\nThis is your ordinary Claude app, the one in Start." : "");
        var dialog = Dialog($"Stop {vm.Name}?", new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, "Stop");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) App.Hub.Stop(vm);
    }

    async Task OfferForceStop(AccountViewModel vm, int pid)
    {
        ShowFront();
        var text = $"{vm.Name}'s Claude has not quit yet. It may still be finishing a task.\r\n\r\n" +
                   "Force close ends it straight away, like End task in Task Manager. Anything it has not saved yet is lost.";
        var dialog = Dialog($"{vm.Name} is still running", new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, "Force close", "Leave it running");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) App.Hub.ForceStop(vm, pid);
    }

    async Task WarnWrongFolder(Account account, Verification v)
    {
        ShowFront();
        var dialog = Dialog("Opened in the wrong folder", new TextBlock { Text = Cli.WrongFolderText(account, v), TextWrapping = TextWrapping.Wrap },
                            v.Pid != null ? "Close that window" : "OK", v.Pid != null ? "Leave it open" : null);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && v.Pid is int pid) App.Hub.CloseWindow(pid);
    }

    internal void ShowNotice(string text) => ShowNotice(text, InfoBarSeverity.Informational, sticky: false);

    void ShowNotice(string text, InfoBarSeverity severity, bool sticky)
    {
        Notice.Message = text;
        Notice.Severity = severity;
        Notice.IsOpen = true;
        noticeTimer.Stop();
        if (!sticky) noticeTimer.Start();
    }

    // ------------------------------------------------------------------ tray --

    IReadOnlyList<TrayIcon.MenuItem?> TrayMenu()
    {
        App.Hub.RefreshAll(force: false);
        var items = new List<TrayIcon.MenuItem?>();
        foreach (var vm in App.Hub.Accounts)
        {
            var usage = vm.Status.Usage?.Session is double s ? $"\t{s:0}% · week {vm.Status.Usage.Weekly:0}%" : "";
            items.Add(new($"{(vm.IsOpen ? "Show" : "Start")} {vm.Name}{usage}", () => App.Hub.Start(vm.Account), Checked: vm.IsOpen));
        }
        items.Add(null);
        items.Add(new("Add account…", () => _ = AddAccount()));
        items.Add(new($"Show {Brand.Name}", ShowFront, Bold: true));
        items.Add(null);
        items.Add(new("Exit", Exit));
        return items;
    }

    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
}
