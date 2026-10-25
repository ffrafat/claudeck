namespace ClauDeck;

sealed class AccountStatus
{
    public int? Pid { get; set; }
    public bool HasData { get; set; }
    public bool HasLogin { get; set; }
    public string? AccountUuid { get; set; }
    public string? Plan { get; set; }
    public string? Email { get; set; }
    public string? EmailFor { get; set; }
    public int CodeChats { get; set; }
    public DateTime? LastUsed { get; set; }

    public UsageInfo? Usage { get; set; }
    public string? UsageError { get; set; }
    public DateTimeOffset NextFetch { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastFetch { get; set; } = DateTimeOffset.MinValue;
    public int Failures { get; set; }

    // Failures back off, because a refused request retried every tick is how a client
    // earns a rate limit rather than avoids one.
    static readonly TimeSpan[] Backoff =
        { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30) };

    public TimeSpan NextBackoff() => Backoff[Math.Min(Failures - 1, Backoff.Length - 1)];
}

static class ProfileReader
{
    /// <summary>Everything about a profile that can be read from disk without opening it.</summary>
    public static void ReadInto(AccountStatus s, string dataDir)
    {
        s.HasData = File.Exists(Path.Combine(dataDir, "config.json"));
        if (!s.HasData) { s.HasLogin = false; s.CodeChats = 0; s.LastUsed = null; return; }

        s.LastUsed = new[] { "config.json", "Preferences", "Local State" }
            .Select(f => Path.Combine(dataDir, f)).Where(File.Exists)
            .Select(File.GetLastWriteTime).DefaultIfEmpty().Max();

        try
        {
            var profile = Tokens.Read(dataDir);
            var token = Tokens.Pick(profile);
            s.HasLogin = token != null;
            s.AccountUuid = profile.LastAccountUuid ?? token?.AccountUuid;
            s.Plan = token?.Plan;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or FormatException or
                                       InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            s.HasLogin = false;
        }

        s.CodeChats = 0;
        if (s.AccountUuid != null)
        {
            var store = Path.Combine(dataDir, "claude-code-sessions", s.AccountUuid);
            if (Directory.Exists(store))
                s.CodeChats = Directory.GetDirectories(store).Sum(org => Directory.GetFiles(org, "local_*.json").Length);
        }
    }
}
