using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace ClauDeck;

public sealed partial class AccountPage : Page
{
    public AccountViewModel ViewModel { get; private set; } = null!;
    readonly List<Button> swatches = new();

    public AccountPage()
    {
        InitializeComponent();
        for (var i = 0; i < Icons.Palette.Length; i++)
        {
            var index = i;
            var swatch = new Button
            {
                Width = 40, Height = 40, Padding = new Thickness(2), CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Content = new Image { Source = new BitmapImage(new Uri(Icons.SwatchPng(i, 64))), Width = 32, Height = 32 },
            };
            ToolTipService.SetToolTip(swatch, Icons.Palette[i].Name);
            AutomationProperties.SetName(swatch, $"{Icons.Palette[i].Name} avatar");
            swatch.Click += (_, _) => App.Hub.SetColor(ViewModel.Account, index);
            swatches.Add(swatch);
            Swatches.Children.Add(swatch);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = App.Hub.Find((string)e.Parameter) ?? App.Hub.Accounts[0];
        ViewModel.PropertyChanged += OnViewModelChanged;
        Bindings.Update();
        MarkSwatch();
        App.Hub.RefreshAll(force: false);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.PropertyChanged -= OnViewModelChanged;

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => MarkSwatch();

    // The chosen colour gets the accent ring every Windows 11 colour picker uses.
    void MarkSwatch()
    {
        for (var i = 0; i < swatches.Count; i++)
        {
            var chosen = i == ViewModel.ColorIndex;
            swatches[i].BorderThickness = new Thickness(chosen ? 2 : 0);
            swatches[i].BorderBrush = chosen ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : null;
        }
    }

    void OnStart(object sender, RoutedEventArgs e) => App.Hub.Start(ViewModel.Account);
    void OnFront(object sender, RoutedEventArgs e) => App.Hub.BringToFront(ViewModel.Account);
    void OnStop(object sender, RoutedEventArgs e) => _ = App.Window.Stop(ViewModel);
    void OnRename(object sender, RoutedEventArgs e) => _ = App.Window.Rename(ViewModel);
    void OnRemove(object sender, RoutedEventArgs e) => _ = App.Window.Remove(ViewModel);
    void OnOpenFolder(object sender, RoutedEventArgs e) => App.Window.OpenFolder(ViewModel);

    void OnShortcutToggled(object sender, RoutedEventArgs e)
    {
        // The binding sets the switch too; only a change by hand is a request.
        if (ViewModel.IsMain || ShortcutToggle.IsOn == ViewModel.HasShortcut) return;
        App.Hub.SetShortcut(ViewModel.Account, ShortcutToggle.IsOn);
    }
}
