using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClauDeck;

sealed class UsageInfo
{
    public double? Session { get; init; }
    public DateTimeOffset? SessionResets { get; init; }
    public double? Weekly { get; init; }
    public DateTimeOffset? WeeklyResets { get; init; }
    public bool? ExtraUsageOn { get; init; }
    public string? WeekBreakdown { get; init; }
    public DateTimeOffset AsOf { get; init; }
    public bool Live { get; init; }
}

/// <summary>
/// Plan limits, live from Anthropic when the profile holds a usable access token and
/// otherwise from the history file Claude itself writes while it runs.
/// </summary>
static class UsageClient
{
    // The token goes here and nowhere else.
    const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    const string ProfileUrl = "https://api.anthropic.com/api/oauth/profile";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed class RateLimited(TimeSpan wait) : Exception("Anthropic asked to wait before asking again") { public TimeSpan Wait { get; } = wait; }

    public static async Task<UsageInfo> FetchLive(TokenEntry token)
    {
        using var res = await Send(UsageUrl, token);
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new RateLimited(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5));
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("the saved login was refused; open this account once so Claude can renew it");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var (session, sessionResets) = Window(root, "five_hour");
        var (weekly, weeklyResets) = Window(root, "seven_day");
        bool? extra = root.TryGetProperty("extra_usage", out var x) && x.ValueKind == JsonValueKind.Object &&
                      x.TryGetProperty("is_enabled", out var on) && on.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? on.GetBoolean() : null;
        return new UsageInfo
        {
            Session = session, SessionResets = sessionResets, Weekly = weekly, WeeklyResets = weeklyResets,
            ExtraUsageOn = extra, WeekBreakdown = Breakdown(root), AsOf = DateTimeOffset.Now, Live = true,
        };
    }

    /// <summary>The account's email, when Anthropic will say. Optional: a failure here hides nothing else.</summary>
    public static async Task<string?> FetchEmail(TokenEntry token)
    {
        try
        {
            using var res = await Send(ProfileUrl, token);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return FindString(doc.RootElement, "email_address") ?? FindString(doc.RootElement, "email");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    static async Task<HttpResponseMessage> Send(string url, TokenEntry token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        req.Headers.UserAgent.ParseAdd($"{Brand.Name}/1.3");
        return await Http.SendAsync(req);
    }

    /// <summary>
    /// The last sample Claude wrote to plan-usage-history.json. Only written while that
    /// Claude is running, so for a closed account it is as old as the last time it was open.
    /// </summary>
    public static UsageInfo? ReadLocal(string dataDir)
    {
        var path = Path.Combine(dataDir, "plan-usage-history.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(Tokens.ReadShared(path));
            if (!doc.RootElement.TryGetProperty("samples", out var samples) || samples.GetArrayLength() == 0) return null;
            var last = samples[samples.GetArrayLength() - 1];
            var u = last.GetProperty("u");
            return new UsageInfo
            {
                Session = u.TryGetProperty("fh", out var fh) ? fh.GetDouble() : null,
                Weekly = u.TryGetProperty("sd", out var sd) ? sd.GetDouble() : null,
                AsOf = DateTimeOffset.FromUnixTimeMilliseconds(last.GetProperty("t").GetInt64()).ToLocalTime(),
                Live = false,
            };
        }
        catch (Exception e) when (e is JsonException or IOException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    static (double?, DateTimeOffset?) Window(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return (null, null);
        double? used = w.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : null;
        DateTimeOffset? resets = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String &&
                                 DateTimeOffset.TryParse(r.GetString(), out var at) ? at.ToLocalTime() : null;
        return (used, resets);
    }

    static string? Breakdown(JsonElement root)
    {
        if (!root.TryGetProperty("seven_day_breakdown", out var b) || b.ValueKind != JsonValueKind.Object ||
            !b.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) return null;
        var parts = rows.EnumerateArray()
            .Where(r => r.TryGetProperty("percent", out var p) && p.ValueKind == JsonValueKind.Number && p.GetDouble() > 0)
            .Select(r => $"{r.GetProperty("display_name").GetString()} {r.GetProperty("percent").GetDouble():0}%");
        var text = string.Join(", ", parts);
        return text.Length > 0 ? text : null;
    }

    static string? FindString(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == name && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                if (FindString(p.Value, name) is { } found) return found;
            }
        return null;
    }
}
