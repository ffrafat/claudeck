using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ClauDeck;

static class Brand
{
    public const string Name = "ClauDeck";
    public const string Tagline = "Multiple Claude Desktops, in parallel";
    // Named for the brand so Explorer's icon cache never shows the old icon under it.
    public static string IconFile => Path.Combine(Paths.IconDir, "claudeck.ico");
}

static class Paths
{
    // Known-folder lookups, never the environment: a launcher started from inside a
    // Claude window inherits that window's LOCALAPPDATA, which may be an account's own.
    public static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Accounts made before the rename stay where they are: Chromium writes absolute
    // paths into a profile, so moving one is how a sign-in gets lost.
    public static readonly string Root =
        File.Exists(Path.Combine(UserProfile, "ClaudeAccounts", "accounts.json"))
            ? Path.Combine(UserProfile, "ClaudeAccounts")
            : Path.Combine(UserProfile, "ClauDeck");
    public static readonly string StateFile = Path.Combine(Root, "accounts.json");
    public static readonly string IconDir = Path.Combine(Root, ".icons");

    /// <summary>
    /// Where the ordinary Claude keeps its data. Claude 2.42304 moved it out of the Store
    /// package into %LOCALAPPDATA%\Claude-Data\claude_&lt;publisher&gt;; older builds kept it
    /// in the package's LocalCache, and the classic installer in %APPDATA%\Claude.
    /// </summary>
    public static string MainDataDir()
    {
        var relocated = Path.Combine(LocalAppData, "Claude-Data");
        if (Directory.Exists(relocated))
        {
            var best = Directory.GetDirectories(relocated, "claude_*")
                .Where(d => File.Exists(Path.Combine(d, "config.json")))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "config.json")))
                .FirstOrDefault();
            if (best != null) return best;
        }
        var packages = Path.Combine(LocalAppData, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var pkg in Directory.GetDirectories(packages, "Claude_*"))
            {
                var inPackage = Path.Combine(pkg, "LocalCache", "Roaming", "Claude");
                if (File.Exists(Path.Combine(inPackage, "config.json"))) return inPackage;
            }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
    }
}

sealed class Account
{
    public const string MainId = "main";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Color { get; set; }
    /// <summary>Wants a Desktop and Start shortcut; kept so a reinstall can put it back.</summary>
    public bool Shortcut { get; set; }

    [JsonIgnore] public bool IsMain => Id == MainId;
    [JsonIgnore] public string Folder => Path.Combine(Paths.Root, Id);
    [JsonIgnore] public string DataDir => IsMain ? Paths.MainDataDir() : Path.Combine(Folder, "Data");
    // Handing each account its own LOCALAPPDATA is what stops Claude relocating it onto
    // the main account's data: with no record of a move there, it stays where it is put.
    [JsonIgnore] public string LocalDir => Path.Combine(Folder, "Local");
}

sealed class LauncherState
{
    public string MainName { get; set; } = "Main account";
    public List<Account> Accounts { get; set; } = new();
    public string? ClaudeExe { get; set; }
    public bool TrayHintShown { get; set; }
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
    /// <summary>"mica", "micaalt" or "acrylic".</summary>
    public string Backdrop { get; set; } = "mica";
    /// <summary>The main account's avatar colour; Claude's own clay unless changed.</summary>
    public int MainColor { get; set; }
    /// <summary>Which palette the stored colours index into. Missing on files written before avatars were recoloured.</summary>
    public int Palette { get; set; }

    public IEnumerable<Account> All()
    {
        yield return new Account { Id = Account.MainId, Name = MainName, Color = MainColor };
        foreach (var a in Accounts) yield return a;
    }

    public Account? Find(string idOrName) =>
        All().FirstOrDefault(a => a.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
        ?? All().FirstOrDefault(a => a.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
}

static class Store
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    const int CurrentPalette = 2;

    public static LauncherState Load()
    {
        var state = new LauncherState { Palette = CurrentPalette };
        try
        {
            if (File.Exists(Paths.StateFile))
                state = JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(Paths.StateFile)) ?? state;
        }
        catch (JsonException) { }
        if (state.Palette < CurrentPalette)
        {
            // Colours saved against the old badge palette mean nothing in this one, and
            // the old first colour is now clay, the main account's. Deal them out afresh.
            state.MainColor = 0;
            foreach (var a in state.Accounts) a.Color = -1;
            foreach (var a in state.Accounts) a.Color = NextColor(state);
            state.Palette = CurrentPalette;
            if (File.Exists(Paths.StateFile)) Save(state);
        }
        return state;
    }

    public static void Save(LauncherState state)
    {
        Directory.CreateDirectory(Paths.Root);
        // Written aside and moved over, so a launcher killed mid-write cannot leave a
        // half file that reads as "no accounts".
        var temp = Paths.StateFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, Paths.StateFile, overwrite: true);
    }

    public static string? ValidateName(LauncherState state, string name, Account? except = null)
    {
        name = name.Trim();
        if (name.Length == 0) return "Give the account a name.";
        if (name.Length > 40) return "Keep the name under 40 characters.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "The name can't contain \\ / : * ? \" < > |";
        if (state.All().Any(a => a != except && a.Id != except?.Id && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return $"There is already an account called '{name}'.";
        return null;
    }

    /// <summary>
    /// The folder name is permanent, because Chromium writes absolute paths into its
    /// data, so it is derived once from the first name and never follows a rename.
    /// </summary>
    public static string NewId(LauncherState state, string name)
    {
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 24) slug = slug[..24].Trim('-');
        if (slug.Length == 0 || slug == Account.MainId) slug = "account";
        var id = slug;
        for (var n = 2; state.All().Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); n++) id = $"{slug}-{n}";
        return id;
    }

    /// <summary>
    /// A random colour among those used least so far, so accounts look different from
    /// each other until the palette runs out, and new ones still vary after that.
    /// </summary>
    public static int NextColor(LauncherState state)
    {
        var used = new int[Icons.Palette.Length];
        used[((state.MainColor % used.Length) + used.Length) % used.Length]++;
        foreach (var a in state.Accounts) if (a.Color >= 0) used[a.Color % used.Length]++;
        var fewest = used.Min();
        var choices = Enumerable.Range(0, used.Length).Where(i => used[i] == fewest).ToArray();
        return choices[Random.Shared.Next(choices.Length)];
    }
}
