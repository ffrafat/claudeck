using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClauDeck;

public sealed partial class SettingsPage : Page
{
    bool loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        var state = App.Hub.State;
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == state.Theme) ?? ThemeBox.Items[0];
        BackdropBox.SelectedItem = BackdropBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == state.Backdrop) ?? BackdropBox.Items[0];
        StartupToggle.IsOn = File.Exists(Shortcuts.StartupFile);
        RootText.Text = App.Hub.IsDemo ? "Not used in the demo" : $"Each account's own Claude data and sign-in: {Paths.Root}";
        loading = false;
    }

    void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        App.Hub.State.Theme = theme;
        App.Hub.SaveSettings();
        App.Window.ApplyTheme(theme);
    }

    void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || BackdropBox.SelectedItem is not ComboBoxItem { Tag: string kind }) return;
        App.Hub.State.Backdrop = kind;
        App.Hub.SaveSettings();
        App.Window.ApplyBackdrop(kind);
    }

    void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (loading || App.Hub.IsDemo) return;
        Shortcuts.SetStartWithWindows(StartupToggle.IsOn, Brand.IconFile);
    }

    void OnOpenRoot(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.Root}\"") { UseShellExecute = false });

    void OnExit(object sender, RoutedEventArgs e) => App.Window.Exit();
}
