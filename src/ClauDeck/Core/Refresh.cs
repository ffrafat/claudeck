namespace ClauDeck;

/// <summary>One pass over an account: what is on disk, then its limits.</summary>
static class Refresh
{
    public static async Task FetchOne(Account a, AccountStatus s, bool watched)
    {
        var dir = a.DataDir;
        ProfileReader.ReadInto(s, dir);
        s.LastFetch = DateTimeOffset.Now;
        var token = s.HasLogin ? SafePick(dir) : null;
        try
        {
            if (token == null) throw new InvalidOperationException(s.HasData ? "not signed in" : "not opened yet");
            s.Usage = await UsageClient.FetchLive(token);
            s.UsageError = null;
            s.Failures = 0;
            s.NextFetch = DateTimeOffset.Now + (watched ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(10));
            if (s.EmailFor != token.AccountUuid)
            {
                s.Email = await UsageClient.FetchEmail(token);
                s.EmailFor = token.AccountUuid;
            }
        }
        catch (Exception e)
        {
            s.Failures++;
            s.NextFetch = DateTimeOffset.Now + (e is UsageClient.RateLimited r ? r.Wait : s.NextBackoff());
            s.UsageError = token == null ? null : e.Message;
            if (s.Usage is not { Live: true } || DateTimeOffset.Now - s.Usage.AsOf > TimeSpan.FromMinutes(30))
                s.Usage = UsageClient.ReadLocal(dir) ?? s.Usage;
        }
    }

    static TokenEntry? SafePick(string dir)
    {
        try { return Tokens.Pick(Tokens.Read(dir)); }
        catch (Exception) { return null; }
    }
}

static class Format
{
    public static string Percent(double? used) => used is double u ? $"{u:0}%" : "—";

    public static string Resets(DateTimeOffset? resets, UsageInfo? u)
    {
        if (u == null) return "";
        if (!u.Live) return $"As of {Ago(u.AsOf.LocalDateTime)}";
        if (resets is not { } at) return "Not started";
        var left = at - DateTimeOffset.Now;
        if (left.TotalMinutes < 1) return "Resets now";
        if (left.TotalHours < 1) return $"Resets in {left.Minutes} min";
        if (left.TotalHours < 24) return $"Resets in {(int)left.TotalHours} h {left.Minutes} min";
        return $"Resets {at.LocalDateTime:ddd d MMM, HH:mm}";
    }

    public static string Limit(double? used, DateTimeOffset? resets, UsageInfo? u) =>
        used == null ? (u == null ? "…" : "—") : $"{Percent(used)} · {Resets(resets, u).ToLowerInvariant()}";

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (t.Date == DateTime.Today) return $"today {t:HH:mm}";
        if (t.Date == DateTime.Today.AddDays(-1)) return $"yesterday {t:HH:mm}";
        return t.ToString("d MMM, HH:mm");
    }
}
