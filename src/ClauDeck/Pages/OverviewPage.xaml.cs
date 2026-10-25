using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ClauDeck;

public sealed partial class OverviewPage : Page
{
    public ObservableCollection<AccountViewModel> Accounts => App.Hub.Accounts;

    public OverviewPage() => InitializeComponent();

    // Each card's buttons carry their account's id in Tag. The card's DataContext is
    // not something ItemsRepeater can be relied on to set for x:Bind templates, and a
    // lookup through it failed silently, which is how Start once did nothing on this page.
    static AccountViewModel? Of(object sender)
    {
        var id = (sender as FrameworkElement)?.Tag as string;
        var vm = id == null ? null : App.Hub.Find(id);
        if (vm == null) App.Window.ShowNotice("That account could not be found. Press F5 or reopen the window and try again.");
        return vm;
    }

    void OnDetails(object sender, RoutedEventArgs e) { if (Of(sender) is { } vm) App.Window.Select(vm.Id); }
    void OnStart(object sender, RoutedEventArgs e) { if (Of(sender) is { } vm) App.Hub.Start(vm.Account); }
    void OnFront(object sender, RoutedEventArgs e) { if (Of(sender) is { } vm) App.Hub.BringToFront(vm.Account); }
    void OnStop(object sender, RoutedEventArgs e) { if (Of(sender) is { } vm) _ = App.Window.Stop(vm); }
    void OnRename(object sender, RoutedEventArgs e) { if (Of(sender) is { } vm) _ = App.Window.Rename(vm); }
    void OnRefresh(object sender, RoutedEventArgs e) => App.Hub.RefreshAll(force: true);

    // The same menu as right-clicking the account in the sidebar.
    void OnMore(object sender, RoutedEventArgs e)
    {
        if (Of(sender) is { } vm && sender is FrameworkElement button)
            App.Window.AccountMenu(vm.Id).ShowAt(button, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }
}
