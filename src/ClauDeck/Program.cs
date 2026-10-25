using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClauDeck;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (Cli.TryRun(args, out var exitCode)) return exitCode;

        // One window at a time. A second start, from a shortcut while the first sits in
        // the tray, asks the first to show itself and leaves. The demo keeps to itself,
        // so it can run beside the real thing.
        var demo = Demo.Options.From(args);
        var name = demo.On ? @"Local\ClauDeck.Demo" : @"Local\ClauDeck";
        using var mutex = new Mutex(true, name, out var isFirst);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
        var hidden = args.Contains("--tray") && !demo.On;
        if (!isFirst)
        {
            if (!hidden) { Processes.AllowForeground(); show.Set(); }
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(init =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(show, hidden, demo);
        });
        return 0;
    }
}
