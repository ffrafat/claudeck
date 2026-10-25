using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClauDeck;

sealed record TokenEntry(string AccountUuid, string? OrgUuid, string Scopes, string AccessToken,
                         DateTimeOffset? ExpiresAt, string? Plan);

/// <summary>
/// Reads the access tokens a Claude profile already holds. Only ever the access token:
/// the refresh token rotates when used, and using it would sign that Claude out.
/// Nothing is written back to the profile.
/// </summary>
static class Tokens
{
    public sealed record Profile(string? LastAccountUuid, List<TokenEntry> Entries);

    public static Profile Read(string dataDir)
    {
        var configPath = Path.Combine(dataDir, "config.json");
        if (!File.Exists(configPath)) return new(null, new());
        using var cfg = JsonDocument.Parse(ReadShared(configPath));
        var last = cfg.RootElement.TryGetProperty("lastKnownAccountUuid", out var l) ? l.GetString() : null;
        if (!cfg.RootElement.TryGetProperty("oauth:tokenCacheV2", out var cache) || cache.GetString() is not { Length: > 0 } sealed_)
            return new(last, new());

        var plain = Decrypt(Convert.FromBase64String(sealed_), Key(dataDir));
        using var doc = JsonDocument.Parse(plain);
        var entries = new List<TokenEntry>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            // acct:<account>|<client>:<org>:https://api.anthropic.com:<scopes>
            if (!p.Name.StartsWith("acct:") || p.Value.ValueKind != JsonValueKind.Object) continue;
            var bar = p.Name.IndexOf('|');
            if (bar < 0 || !p.Value.TryGetProperty("token", out var tok)) continue;
            var account = p.Name[5..bar];
            var rest = p.Name[(bar + 1)..].Split(':');
            var hostAt = p.Name.IndexOf("https://api.anthropic.com:", StringComparison.Ordinal);
            var scopes = hostAt >= 0 ? p.Name[(hostAt + 26)..] : "";
            DateTimeOffset? expires = p.Value.TryGetProperty("expiresAt", out var e) && e.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
            var plan = p.Value.TryGetProperty("subscriptionType", out var s) ? s.GetString() : null;
            entries.Add(new(account, rest.Length > 1 ? rest[1] : null, scopes, tok.GetString() ?? "", expires, plan));
        }
        return new(last, entries);
    }

    /// <summary>The narrowest unexpired token for the account the profile is signed into.</summary>
    public static TokenEntry? Pick(Profile profile)
    {
        var now = DateTimeOffset.UtcNow;
        return profile.Entries
            .Where(e => e.AccessToken.Length > 0 && (e.ExpiresAt == null || e.ExpiresAt > now))
            .Where(e => profile.LastAccountUuid == null || e.AccountUuid == profile.LastAccountUuid)
            .OrderBy(e => e.Scopes.Split(' ').Length)
            .FirstOrDefault();
    }

    static byte[] Key(string dataDir)
    {
        using var ls = JsonDocument.Parse(ReadShared(Path.Combine(dataDir, "Local State")));
        var wrapped = Convert.FromBase64String(ls.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!);
        if (wrapped.Length < 6 || Encoding.ASCII.GetString(wrapped, 0, 5) != "DPAPI")
            throw new InvalidDataException("the profile key is not protected the way this launcher expects");
        return Unprotect(wrapped[5..]);
    }

    // Chromium's v10 format: "v10", a 12-byte nonce, the ciphertext, a 16-byte tag.
    static string Decrypt(byte[] blob, byte[] key)
    {
        if (blob.Length < 31 || Encoding.ASCII.GetString(blob, 0, 3) != "v10")
            throw new InvalidDataException("the saved login is in a format this launcher does not know");
        var cipher = blob[15..^16];
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(blob[3..15], cipher, blob[^16..], plain);
        return Encoding.UTF8.GetString(plain);
    }

    // Claude holds these files open while it runs.
    public static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    static byte[] Unprotect(byte[] data)
    {
        var input = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        Marshal.Copy(data, 0, input.pbData, data.Length);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                throw new CryptographicException($"Windows could not unlock this profile's key ({Marshal.GetLastWin32Error()})");
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            LocalFree(output.pbData);
            return result;
        }
        finally { Marshal.FreeHGlobal(input.pbData); }
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB pDataOut);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    struct DATA_BLOB { public int cbData; public IntPtr pbData; }
}
