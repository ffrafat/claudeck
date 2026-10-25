using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClauDeck;

public partial class App : Application
{
    readonly EventWaitHandle showSignal;
    readonly bool startHidden;
    readonly Demo.Options demo;
    MainWindow? window;

    internal static Hub Hub { get; private set; } = null!;
    internal static MainWindow Window => ((App)Current).window!;
    internal static Demo.Options DemoOptions => ((App)Current).demo;

    internal App(EventWaitHandle showSignal, bool startHidden, Demo.Options demo)
    {
        this.showSignal = showSignal;
        this.startHidden = startHidden;
        this.demo = demo;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Hub = new Hub(demo.On);
        window = new MainWindow();
        if (!startHidden) window.ShowFront();

        // Another copy started from a shortcut asks this one to come forward.
        var ui = DispatcherQueue.GetForCurrentThread();
        new Thread(() =>
        {
            while (showSignal.WaitOne()) ui.TryEnqueue(() => window.ShowFront());
        }) { IsBackground = true, Name = "show signal" }.Start();
    }
}
