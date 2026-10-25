using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClauDeck;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        Logo.Source = new BitmapImage(new Uri(Icons.AppPng(128)));
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3)}" + (App.Hub.IsDemo ? " · demo" : "");
    }
}
