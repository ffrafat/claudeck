namespace ClauDeck;

/// <summary>
/// Sample accounts for screenshots and trying the app out: ClauDeck.exe --demo. Nothing
/// here is real; nothing is started, saved or sent anywhere, and it runs beside a real copy.
/// </summary>
static class Demo
{
    public sealed record Options(bool On, string? Page, string? Theme)
    {
        public static Options From(string[] args)
        {
            string? After(string flag) { var i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            return new(args.Contains("--demo"), After("--page"), After("--theme"));
        }
    }

    public static LauncherState State() => new()
    {
        MainName = "Personal",
        MainColor = 0,
        Palette = 2,
        Accounts =
        {
            new Account { Id = "work", Name = "Work", Color = 1 },
            new Account { Id = "client-acme", Name = "Client · Acme", Color = 7 },
            new Account { Id = "research", Name = "Research", Color = 3 },
        },
    };

    public static Dictionary<string, AccountStatus> Statuses()
    {
        var now = DateTimeOffset.Now;
        AccountStatus S(int? pid, string email, string plan, int chats, double session, double weekly, double sessionIn, double weekIn, string? breakdown, bool extra) => new()
        {
            Pid = pid, HasData = true, HasLogin = true, Email = email, Plan = plan, CodeChats = chats,
            LastUsed = DateTime.Now.AddMinutes(-sessionIn * 7),
            Usage = new UsageInfo
            {
                Session = session, SessionResets = session > 0 ? now.AddHours(sessionIn) : null,
                Weekly = weekly, WeeklyResets = now.AddDays(weekIn),
                ExtraUsageOn = extra, WeekBreakdown = breakdown, AsOf = now.AddMinutes(-1), Live = true,
            },
            LastFetch = now, NextFetch = DateTimeOffset.MaxValue,
        };
        return new()
        {
            [Account.MainId] = S(1, "you@example.com", "pro", 27, 34, 21, 3.4, 4.2, "Claude Code 82%, Chats 18%", false),
            ["work"] = S(2, "you@company.example", "max", 64, 76, 48, 1.2, 2.6, "Claude Code 100%", true),
            ["client-acme"] = S(null, "dev@acme.example", "pro", 9, 0, 15, 0, 5.1, "Claude Code 100%", false),
            ["research"] = S(null, "lab@university.example", "pro", 13, 93, 66, 0.6, 1.4, "Chats 61%, Claude Code 39%", false),
        };
    }
}
