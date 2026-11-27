using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shiny;
using Shiny.Extensions.Stores;

namespace Notcho.Services;

/// <summary>
/// Persisted preferences, on Shiny's default store: NSUserDefaults on macOS, a settings file elsewhere.
/// </summary>
[Singleton]
public sealed class AppSettings
{
    readonly IKeyValueStore store;

    public AppSettings(IKeyValueStore store) => this.store = store;

    /// <summary>A setting the user controls changed. The argument is the setting's name.</summary>
    public event EventHandler<string>? Changed;

    public string TeleprompterScript
    {
        get => this.Text(nameof(this.TeleprompterScript)) ?? DefaultScript;
        set => this.Set(nameof(this.TeleprompterScript), value);
    }

    /// <summary>Points scrolled per second.</summary>
    public double TeleprompterSpeed
    {
        get => this.Double(nameof(this.TeleprompterSpeed), 28);
        set => this.Set(nameof(this.TeleprompterSpeed), Math.Clamp(value, 5, 200));
    }

    public double TeleprompterFontSize
    {
        get => this.Double(nameof(this.TeleprompterFontSize), 20);
        set => this.Set(nameof(this.TeleprompterFontSize), Math.Clamp(value, 12, 48));
    }

    public const double MinTeleprompterWidth = 640;
    public const double MaxTeleprompterWidth = 1000;
    public const double MinTeleprompterHeight = 80;
    public const double MaxTeleprompterHeight = 400;

    /// <summary>
    /// The panel's width while the Teleprompter tab shows, in points. At the minimum it is every other
    /// tab's width; any wider and the panel grows sideways for the teleprompter only.
    /// </summary>
    public double TeleprompterWidth
    {
        get => this.Double(nameof(this.TeleprompterWidth), MinTeleprompterWidth);
        set => this.Set(nameof(this.TeleprompterWidth), Math.Clamp(Math.Round(value), MinTeleprompterWidth, MaxTeleprompterWidth));
    }

    /// <summary>How much of the script shows at once: the height of the scrolling text, in points.</summary>
    public double TeleprompterHeight
    {
        get => this.Double(nameof(this.TeleprompterHeight), 150);
        set => this.Set(nameof(this.TeleprompterHeight), Math.Clamp(Math.Round(value), MinTeleprompterHeight, MaxTeleprompterHeight));
    }

    /// <summary>
    /// Offer to follow the reader's voice with speech-to-text: a microphone button on the teleprompter that
    /// scrolls with the words as they're said and marks the one being read. Off unless asked for.
    /// </summary>
    public bool TeleprompterFollowVoice
    {
        get => this.Bool(nameof(this.TeleprompterFollowVoice), false);
        set => this.Set(nameof(this.TeleprompterFollowVoice), value);
    }

    /// <summary>What the closed notch (and the volume/brightness HUD) is painted with. Black unless changed.</summary>
    public NotchFill ClosedNotchFill
    {
        get => NotchFill.Parse(this.Text(nameof(this.ClosedNotchFill)));
        set => this.Set(nameof(this.ClosedNotchFill), value.Serialize());
    }

    /// <summary>What the open panel is painted with. Black unless changed.</summary>
    public NotchFill OpenNotchFill
    {
        get => NotchFill.Parse(this.Text(nameof(this.OpenNotchFill)));
        set => this.Set(nameof(this.OpenNotchFill), value.Serialize());
    }

    public bool UseHardwareNotch
    {
        get => this.Bool(nameof(this.UseHardwareNotch), true);
        set => this.Set(nameof(this.UseHardwareNotch), value);
    }

    public const double MaxNotchWingWidth = 160;
    public const double DefaultNotchWingWidth = 84;
    public const double MaxNotchExtraHeight = 24;

    /// <summary>
    /// How far the collapsed notch reaches out left of the camera, in points; 0 is the camera's own edge.
    /// A wish, not a promise: a wing never goes narrower than what it is showing needs.
    /// </summary>
    public double NotchLeftWidth
    {
        get => this.Double(nameof(this.NotchLeftWidth), DefaultNotchWingWidth);
        set => this.Set(nameof(this.NotchLeftWidth), Math.Clamp(Math.Round(value), 0, MaxNotchWingWidth));
    }

    /// <summary>As <see cref="NotchLeftWidth"/>, right of the camera.</summary>
    public double NotchRightWidth
    {
        get => this.Double(nameof(this.NotchRightWidth), DefaultNotchWingWidth);
        set => this.Set(nameof(this.NotchRightWidth), Math.Clamp(Math.Round(value), 0, MaxNotchWingWidth));
    }

    /// <summary>
    /// Both wings the same width, so the camera stays centred: one slider in Settings, and the wider of
    /// the two wings' content sets both.
    /// </summary>
    public bool NotchWidthsLocked
    {
        get => this.Bool(nameof(this.NotchWidthsLocked), true);
        set => this.Set(nameof(this.NotchWidthsLocked), value);
    }

    /// <summary>Points the collapsed notch hangs below the camera. At 0 it is exactly the camera's height.</summary>
    public double NotchExtraHeight
    {
        get => this.Double(nameof(this.NotchExtraHeight), 0);
        set => this.Set(nameof(this.NotchExtraHeight), Math.Clamp(Math.Round(value), 0, MaxNotchExtraHeight));
    }

    public const double MaxStandInCameraWidth = 240;
    public const double MinStandInNotchWidth = 40;
    public const double MinStandInCameraHeight = 24;
    public const double MaxStandInCameraHeight = 48;
    public const double MaxStandInNotchWidth = 400;

    /// <summary>
    /// On a display without a camera notch, the gap the closed notch leaves between its wings where a camera
    /// would be, in points. About a MacBook's unless changed; at 0 the wings meet, and the notch is only as
    /// wide as what it shows.
    /// </summary>
    public double StandInCameraWidth
    {
        get => this.Double(nameof(this.StandInCameraWidth), 180);
        set => this.Set(nameof(this.StandInCameraWidth), Math.Clamp(Math.Round(value), 0, MaxStandInCameraWidth));
    }

    /// <summary>
    /// On a display without a camera notch, how tall the closed notch is before any extra height, in points:
    /// the stand-in camera's height. Never less than the artwork in the left wing needs.
    /// </summary>
    public double StandInCameraHeight
    {
        get => this.Double(nameof(this.StandInCameraHeight), 32);
        set => this.Set(nameof(this.StandInCameraHeight), Math.Clamp(Math.Round(value), MinStandInCameraHeight, MaxStandInCameraHeight));
    }

    /// <summary>
    /// On a display without a camera notch, the narrowest the closed notch gets, in points. There's no camera
    /// for an empty notch to vanish into, so with nothing to show this is what there is to hover.
    /// </summary>
    public double StandInMinWidth
    {
        get => this.Double(nameof(this.StandInMinWidth), 120);
        set => this.Set(nameof(this.StandInMinWidth), Math.Clamp(Math.Round(value), MinStandInNotchWidth, MaxStandInNotchWidth));
    }

    /// <summary>
    /// What the closed notch shows, by <c>PeekItem</c> name. What's playing and the weather unless changed;
    /// the system readings only when asked for, since they keep the app sampling every second.
    /// </summary>
    public IReadOnlyList<string> PeekItems
    {
        get => this.List(nameof(this.PeekItems)) ?? ["Music", "Weather"];
        set => this.Set(nameof(this.PeekItems), value.Distinct().ToArray());
    }

    /// <summary>The notch opens on a click instead of on hover. It still folds away when the pointer leaves.</summary>
    public bool ClickToOpen
    {
        get => this.Bool(nameof(this.ClickToOpen), false);
        set => this.Set(nameof(this.ClickToOpen), value);
    }

    /// <summary>
    /// The music buttons on the closed notch, by <c>PeekMusicButton</c> name. None unless asked for, and only
    /// shown with <see cref="ClickToOpen"/>: with hovering, the notch would open before they could be clicked.
    /// </summary>
    public IReadOnlyList<string> PeekMusicButtons
    {
        get => this.List(nameof(this.PeekMusicButtons)) ?? [];
        set => this.Set(nameof(this.PeekMusicButtons), value.Distinct().ToArray());
    }

    public const double MaxHoverOpenDelay = 1500;

    /// <summary>
    /// How long the pointer has to rest on the notch before hovering opens it, in milliseconds, so passing
    /// over it on the way to the menu bar doesn't. Leaving before then calls it off.
    /// </summary>
    public double HoverOpenDelay
    {
        get => this.Double(nameof(this.HoverOpenDelay), 150);
        set => this.Set(nameof(this.HoverOpenDelay), Math.Clamp(Math.Round(value), 0, MaxHoverOpenDelay));
    }

    public bool ShowVolumeHud
    {
        get => this.Bool(nameof(this.ShowVolumeHud), true);
        set => this.Set(nameof(this.ShowVolumeHud), value);
    }

    public bool ShowBrightnessHud
    {
        get => this.Bool(nameof(this.ShowBrightnessHud), true);
        set => this.Set(nameof(this.ShowBrightnessHud), value);
    }

    /// <summary>Briefly open Now Playing when the song changes, unless the panel is already open.</summary>
    public bool ShowTrackChanges
    {
        get => this.Bool(nameof(this.ShowTrackChanges), true);
        set => this.Set(nameof(this.ShowTrackChanges), value);
    }

    /// <summary>
    /// Tabs turned off in Settings, by name. Everything else shows (if the platform has it), so a tab
    /// added in an update shows up on its own.
    /// </summary>
    public IReadOnlyList<string> HiddenTabs
    {
        // Before tabs could be hidden, only the macshot tab could be turned off, under its own switch.
        get => this.List(nameof(this.HiddenTabs)) ?? (this.Bool(LegacyShowMacShotTab, true) ? [] : ["Capture"]);
        set => this.Set(nameof(this.HiddenTabs), value.Distinct().ToArray());
    }

    const string LegacyShowMacShotTab = "ShowMacShotTab";

    /// <summary>The task list, as JSON (see <see cref="TasksService"/>).</summary>
    public string? Tasks
    {
        get => this.Text(nameof(this.Tasks));
        set => this.Set(nameof(this.Tasks), value ?? "", notify: false);
    }

    /// <summary>The clipboard history, as JSON (see <see cref="ClipboardService"/>).</summary>
    public string? ClipboardHistory
    {
        get => this.Text(nameof(this.ClipboardHistory));
        set => this.Set(nameof(this.ClipboardHistory), value ?? "", notify: false);
    }

    /// <summary>The calculator's tape of finished sums, as JSON (see <see cref="CalculatorService"/>).</summary>
    public string? CalculatorTape
    {
        get => this.Text(nameof(this.CalculatorTape));
        set => this.Set(nameof(this.CalculatorTape), value ?? "", notify: false);
    }

    /// <summary>The calculator groups thousands (1,234,567) the way the region does.</summary>
    public bool CalculatorGroupDigits
    {
        get => this.Bool(nameof(this.CalculatorGroupDigits), true);
        set => this.Set(nameof(this.CalculatorGroupDigits), value);
    }

    /// <summary>The calculator's tape of recent sums shows beside the keypad.</summary>
    public bool CalculatorShowTape
    {
        get => this.Bool(nameof(this.CalculatorShowTape), true);
        set => this.Set(nameof(this.CalculatorShowTape), value);
    }

    /// <summary>Every answer goes on the clipboard as = is pressed, ready to paste.</summary>
    public bool CalculatorCopyOnEquals
    {
        get => this.Bool(nameof(this.CalculatorCopyOnEquals), false);
        set => this.Set(nameof(this.CalculatorCopyOnEquals), value);
    }

    /// <summary>While keeping awake, the screen stays on too, rather than only the computer.</summary>
    public bool AwakeKeepDisplayOn
    {
        get => this.Bool(nameof(this.AwakeKeepDisplayOn), true);
        set => this.Set(nameof(this.AwakeKeepDisplayOn), value);
    }

    /// <summary>Start keeping awake, until it's stopped, whenever the app opens.</summary>
    public bool AwakeOnLaunch
    {
        get => this.Bool(nameof(this.AwakeOnLaunch), false);
        set => this.Set(nameof(this.AwakeOnLaunch), value);
    }

    /// <summary>A cup on the closed notch while keeping awake.</summary>
    public bool AwakeShowInPeek
    {
        get => this.Bool(nameof(this.AwakeShowInPeek), true);
        set => this.Set(nameof(this.AwakeShowInPeek), value);
    }

    /// <summary>The microphone voice notes record from, by its device id; null is the system default.</summary>
    public string? VoiceNoteInput
    {
        get => this.Text(nameof(this.VoiceNoteInput)) is { Length: > 0 } id ? id : null;
        set => this.Set(nameof(this.VoiceNoteInput), value ?? "", notify: false);
    }

    /// <summary>The camera the mirror shows, by its id; null is the default camera.</summary>
    public string? MirrorCamera
    {
        get => this.Text(nameof(this.MirrorCamera)) is { Length: > 0 } id ? id : null;
        set => this.Set(nameof(this.MirrorCamera), value ?? "", notify: false);
    }

    /// <summary>The mirror's look, a <c>CameraFilter</c> name.</summary>
    public string MirrorFilter
    {
        get => this.Text(nameof(this.MirrorFilter)) ?? "None";
        set => this.Set(nameof(this.MirrorFilter), value, notify: false);
    }

    /// <summary>Paths in the file tray, in the order they were dropped.</summary>
    public IReadOnlyList<string> TrayFiles
    {
        get => this.List(nameof(this.TrayFiles)) ?? [];
        set => this.Set(nameof(this.TrayFiles), value.ToArray(), notify: false);
    }

    /// <summary>"auto" (the region's units), "c" or "f".</summary>
    public string TemperatureUnit
    {
        get => this.Text(nameof(this.TemperatureUnit)) ?? "auto";
        set => this.Set(nameof(this.TemperatureUnit), value);
    }

    public bool WeatherUseCurrentLocation
    {
        get => this.Bool(nameof(this.WeatherUseCurrentLocation), true);
        set => this.Set(nameof(this.WeatherUseCurrentLocation), value);
    }

    /// <summary>Places added in Settings. Stored as "name|latitude|longitude" strings.</summary>
    public IReadOnlyList<WeatherPlace> WeatherPlaces
    {
        get => (this.List(nameof(this.WeatherPlaces)) ?? [])
            .Select(s => s.Split('|'))
            .Where(p => p.Length == 3)
            .Select(p => new WeatherPlace(p[0], System.Double.Parse(p[1], CultureInfo.InvariantCulture), System.Double.Parse(p[2], CultureInfo.InvariantCulture)))
            .ToList();
        set => this.Set(
            nameof(this.WeatherPlaces),
            value.Select(p => String.Join('|', p.Name.Replace("|", " "), p.Latitude.ToString(CultureInfo.InvariantCulture), p.Longitude.ToString(CultureInfo.InvariantCulture))).ToArray()
        );
    }

    /// <summary>The key of the place the Weather tab shows.</summary>
    public string? WeatherSelected
    {
        get => this.Text(nameof(this.WeatherSelected));
        set => this.Set(nameof(this.WeatherSelected), value ?? "", notify: false);
    }

    /// <summary>The leagues the Sports tab follows, by ESPN path ("hockey/nhl"), in the strip's order.</summary>
    public IReadOnlyList<string> SportsLeagues
    {
        get => this.List(nameof(this.SportsLeagues)) ?? SportsLeague.Defaults;
        set => this.Set(nameof(this.SportsLeagues), value.Distinct().ToArray());
    }

    /// <summary>Favourite teams. Stored as "league|id|abbreviation|name|short name|logo|colour|alternate colour" strings.</summary>
    public IReadOnlyList<SportsTeam> SportsTeams
    {
        get => (this.List(nameof(this.SportsTeams)) ?? [])
            .Select(s => s.Split('|'))
            .Where(p => p.Length == 8)
            .Select(p => new SportsTeam(p[0], p[1], p[2], p[3], p[4], NullIfEmpty(p[5]), NullIfEmpty(p[6]), NullIfEmpty(p[7])))
            .ToList();
        set => this.Set(
            nameof(this.SportsTeams),
            value.Select(t => String.Join('|', new[] { t.League, t.Id, t.Abbreviation, t.Name, t.ShortName, t.Logo ?? "", t.Color ?? "", t.AlternateColor ?? "" }.Select(s => s.Replace("|", " ")))).ToArray()
        );
    }

    /// <summary>The Sports tab's chip: "mine" for your teams, or a league's key.</summary>
    public string? SportsFilter
    {
        get => this.Text(nameof(this.SportsFilter));
        set => this.Set(nameof(this.SportsFilter), value ?? "", notify: false);
    }

    /// <summary>"league|event id" of the game whose score the closed notch shows while it's on; null for none.</summary>
    public string? SportsPinnedGame
    {
        get => this.Text(nameof(this.SportsPinnedGame)) is { Length: > 0 } key ? key : null;
        set => this.Set(nameof(this.SportsPinnedGame), value ?? "", notify: false);
    }

    static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>The news networks the News tab reads, by key ("cbc"), in Settings' order.</summary>
    public IReadOnlyList<string> NewsNetworks
    {
        get => this.List(nameof(this.NewsNetworks)) ?? NewsNetwork.Defaults;
        set => this.Set(nameof(this.NewsNetworks), value.Distinct().ToArray());
    }

    /// <summary>The categories the News tab has a chip for, by key ("world").</summary>
    public IReadOnlyList<string> NewsCategories
    {
        get => this.List(nameof(this.NewsCategories)) ?? NewsCategory.Defaults;
        set => this.Set(nameof(this.NewsCategories), value.Distinct().ToArray());
    }

    /// <summary>The News tab's chip: a category's key.</summary>
    public string? NewsFilter
    {
        get => this.Text(nameof(this.NewsFilter));
        set => this.Set(nameof(this.NewsFilter), value ?? "", notify: false);
    }

    /// <summary>The RSS tab's feeds. Stored as "url|title" strings, in the order they were added.</summary>
    public IReadOnlyList<RssFeed> RssFeeds
    {
        get => (this.List(nameof(this.RssFeeds)) ?? [])
            .Select(s => s.Split('|', 2))
            .Where(p => p.Length == 2)
            .Select(p => new RssFeed(p[0], p[1]))
            .ToList();
        set => this.Set(nameof(this.RssFeeds), value.Select(f => f.Url.Replace("|", "%7C") + "|" + f.Title).ToArray());
    }

    /// <summary>The RSS tab's chip: a feed's address, or "all".</summary>
    public string? RssFilter
    {
        get => this.Text(nameof(this.RssFilter));
        set => this.Set(nameof(this.RssFilter), value ?? "", notify: false);
    }

    /// <summary>Open at login has been set up on a first launch. It only happens once.</summary>
    public bool LoginItemSetUp
    {
        get => this.Bool(nameof(this.LoginItemSetUp), false);
        set => this.Set(nameof(this.LoginItemSetUp), value, notify: false);
    }

    /// <summary>The release "Don't show again" was ticked for, by version; a newer release asks again.</summary>
    public string? SkippedUpdate
    {
        get => this.Text(nameof(this.SkippedUpdate)) is { Length: > 0 } version ? version : null;
        set => this.Set(nameof(this.SkippedUpdate), value ?? "", notify: false);
    }

    public string LastTab
    {
        get => this.Text(nameof(this.LastTab)) ?? "Media";
        set => this.Set(nameof(this.LastTab), value, notify: false);
    }

    // ---------------------------------------------------------------------------------------------
    // Backup and restore
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Left out of a backup: what you've made rather than chosen (tasks, the clipboard, the tape, the
    /// tray's files - which name files on this computer only), and the app's own bookkeeping.
    /// </summary>
    static readonly HashSet<string> NotBackedUp =
    [
        nameof(Tasks), nameof(ClipboardHistory), nameof(CalculatorTape), nameof(TrayFiles),
        nameof(LoginItemSetUp), nameof(SkippedUpdate), nameof(LastTab), nameof(WeatherSelected),
        nameof(SportsFilter), nameof(SportsPinnedGame), nameof(NewsFilter), nameof(RssFilter),
        nameof(MirrorCamera), nameof(VoiceNoteInput)
    ];

    /// <summary>What a key is stored as, learnt as each setting is read (see <see cref="StoredKinds"/>).</summary>
    enum Kind { Text, Number, Flag, List }

    readonly Dictionary<string, Kind> kinds = new();

    /// <summary>
    /// Every key a setting reads, with what it's stored as. The store can't list its keys, so each public
    /// setting is read once and the helpers below note the keys they're asked for - a setting added
    /// later is backed up without anyone remembering to list it.
    /// </summary>
    Dictionary<string, Kind> StoredKinds()
    {
        foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
            property.GetValue(this);
        return this.kinds.Where(k => !NotBackedUp.Contains(k.Key)).ToDictionary();
    }

    /// <summary>The backup format this version writes, and the newest it can read.</summary>
    const int BackupFormat = 1;

    /// <summary>Bigger than any real backup by far; anything this size isn't one.</summary>
    public const long MaxBackupBytes = 4 * 1024 * 1024;

    /// <summary>
    /// The settings you've changed, as JSON: each stored key and its value as stored, with a SHA-256 of
    /// them so a restore can tell the file hasn't been damaged or edited since.
    /// </summary>
    public string Export(DateTimeOffset now)
    {
        var values = new JsonObject();
        foreach (var (key, kind) in this.StoredKinds().Where(k => this.store.Contains(k.Key)).OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            values[key] = kind switch
            {
                Kind.Text => JsonValue.Create(this.Text(key)),
                Kind.Number => JsonValue.Create(this.store.Get(key, 0d)),
                Kind.Flag => JsonValue.Create(this.store.Get(key, false)),
                _ => new JsonArray((this.List(key) ?? []).Select(v => (JsonNode?)JsonValue.Create(v)).ToArray())
            };
        }
        var backup = new JsonObject
        {
            ["app"] = "Notcho",
            ["format"] = BackupFormat,
            ["created"] = now.ToString("O", CultureInfo.InvariantCulture),
            ["checksum"] = Checksum(values),
            ["settings"] = values
        };
        return backup.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Checks a backup through and through without changing anything: that it's a Notcho backup, in a
    /// format this version reads, that its checksum matches its settings, and that every setting this
    /// version knows holds a value of the kind it keeps there.
    /// </summary>
    /// <exception cref="FormatException">It isn't a backup this version can restore, and why.</exception>
    public BackupContents Verify(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("This isn't a Notcho settings backup.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("This isn't a Notcho settings backup: it isn't valid JSON.", ex);
        }

        if (Text(root["app"]) != "Notcho" || root["settings"] is not JsonObject values)
            throw new FormatException("This isn't a Notcho settings backup.");
        if (root["format"] is not JsonValue formatValue || !formatValue.TryGetValue<int>(out var format) || format < 1)
            throw new FormatException("This backup is damaged: it doesn't say which format it's in.");
        if (format > BackupFormat)
            throw new FormatException("This backup was made by a newer version of Notcho. Update Notcho, then restore it.");
        if (Text(root["checksum"]) is not { } checksum || !checksum.Equals(Checksum(values), StringComparison.OrdinalIgnoreCase))
            throw new FormatException("This backup is damaged or has been edited: its checksum doesn't match its settings.");

        var known = this.StoredKinds();
        var restorable = new Dictionary<string, JsonNode>();
        var unknown = 0;
        foreach (var (key, node) in values)
        {
            if (!known.TryGetValue(key, out var kind))
            {
                // A setting from a newer version, or one this version has retired: passed over.
                unknown++;
            }
            else if (node == null || !IsKind(node, kind))
            {
                throw new FormatException($"This backup is damaged: the setting \"{key}\" doesn't hold the kind of value it should.");
            }
            else
            {
                restorable[key] = node;
            }
        }

        var created = DateTimeOffset.TryParse(Text(root["created"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : (DateTimeOffset?)null;
        return new BackupContents(restorable, unknown, created);
    }

    /// <summary>
    /// Puts back a verified backup's settings, raising <see cref="Changed"/> for each, as changing it in
    /// Settings would. Only ever given what <see cref="Verify"/> passed, so it can't stop half way.
    /// </summary>
    /// <returns>How many settings were restored.</returns>
    public int Restore(BackupContents backup)
    {
        var known = this.StoredKinds();
        foreach (var (key, node) in backup.Settings)
        {
            switch (known[key])
            {
                case Kind.Text:
                    this.store.Set(key, node.GetValue<string>());
                    break;
                case Kind.Number:
                    this.store.Set(key, node.GetValue<double>());
                    break;
                case Kind.Flag:
                    this.store.Set(key, node.GetValue<bool>());
                    break;
                default:
                    this.store.Set(key, node.AsArray().Select(v => v!.GetValue<string>()).ToArray());
                    break;
            }
        }
        foreach (var key in backup.Settings.Keys)
            this.Changed?.Invoke(this, key);
        return backup.Settings.Count;
    }

    static bool IsKind(JsonNode node, Kind kind) => kind switch
    {
        Kind.Text => node.GetValueKind() == JsonValueKind.String,
        Kind.Number => node.GetValueKind() == JsonValueKind.Number,
        Kind.Flag => node.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        _ => node is JsonArray array && array.All(v => v?.GetValueKind() == JsonValueKind.String)
    };

    /// <summary>A string value, or null when it's missing or not a string.</summary>
    static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>SHA-256 of the settings as compact JSON, in hex.</summary>
    static string Checksum(JsonObject values)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(values.ToJsonString())));

    string? Text(string key)
    {
        this.kinds[key] = Kind.Text;
        return this.store.Get<string?>(key, null);
    }

    double Double(string key, double fallback)
    {
        this.kinds[key] = Kind.Number;
        return this.store.Get(key, fallback);
    }

    bool Bool(string key, bool fallback)
    {
        this.kinds[key] = Kind.Flag;
        return this.store.Get(key, fallback);
    }

    /// <summary>A list, or null when unset - or unreadable, which is no worse than unset for a tray or a place list.</summary>
    string[]? List(string key)
    {
        this.kinds[key] = Kind.List;
        try
        {
            return this.store.Get<string[]?>(key, null);
        }
        catch
        {
            return null;
        }
    }

    /// <param name="notify">
    /// False for bookkeeping the app writes itself (the last tab, the tray list), which nothing needs to
    /// react to — raising Changed for those would rebuild the notch for no reason.
    /// </param>
    void Set<T>(string key, T value, bool notify = true)
    {
        this.store.Set(key, value);
        if (notify)
            this.Changed?.Invoke(this, key);
    }

    const string DefaultScript =
        "Welcome to Notcho.\n\n" +
        "This is the teleprompter. Your script scrolls right under the camera, so your eyes stay on " +
        "the lens while you read.\n\n" +
        "Open Settings (the ⚙ in the notch) to paste in your own script, then press play. " +
        "Use the speed and size buttons to tune it while you talk.\n\n" +
        "Hover away and the notch stays open while the script is running. Press pause to stop.";
}

/// <summary>A backup that passed <see cref="AppSettings.Verify"/>, ready to restore.</summary>
/// <param name="Settings">The settings this version knows, each holding the right kind of value.</param>
/// <param name="Unknown">Settings it holds that this version doesn't know, which are passed over.</param>
/// <param name="Created">When it was made, when it says.</param>
public sealed record BackupContents(IReadOnlyDictionary<string, JsonNode> Settings, int Unknown, DateTimeOffset? Created);
