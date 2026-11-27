using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shiny;

namespace Notcho.Services;

/// <summary>
/// Scores, schedules, box scores and standings for the leagues and teams you follow, from ESPN's public
/// site API (free, no key) - logos included.
/// </summary>
/// <remarks>
/// <para>
/// Everything is fetched on demand and cached, each kind for as long as it can go stale: a game that's on
/// is good for 15 seconds, a finished day for an hour, standings for half an hour. The notch polls
/// <see cref="RefreshAsync"/> every 20 seconds while <see cref="NeedsPolling"/> says something is on, and
/// on its minute tick otherwise; each refresh fetches only what has gone stale.
/// </para>
/// <para>
/// <see cref="Changed"/> fires once per refresh, and only when something shown actually changed - a
/// rebuilt tab loses its scroll position, so a quiet minute mustn't rebuild it. Logos never raise it:
/// each is set on its image in place when it arrives (see <see cref="LogoAsync"/>).
/// </para>
/// </remarks>
[Singleton]
public sealed class SportsService
{
    public const string MyTeams = "mine";

    /// <summary>Just under the notch's 20 second poll, so every poll while a game is on fetches it again.</summary>
    static readonly TimeSpan LiveStale = TimeSpan.FromSeconds(15);
    static readonly TimeSpan QuietStale = TimeSpan.FromMinutes(10);
    static readonly TimeSpan PastStale = TimeSpan.FromHours(1);
    static readonly TimeSpan FailedRetry = TimeSpan.FromMinutes(1);
    static readonly TimeSpan TablesStale = TimeSpan.FromMinutes(30);
    static readonly TimeSpan TeamsStale = TimeSpan.FromDays(1);
    /// <summary>How many past games and upcoming ones a team's schedule shows.</summary>
    const int ScheduleResults = 3;
    const int ScheduleUpcoming = 7;
    const string Site = "https://site.api.espn.com/apis/site/v2/sports/";

    readonly AppSettings settings;
    readonly TimeProvider time;
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Dictionary<string, Scoreboard> boards = new();
    readonly Dictionary<string, GameDetail> details = new();
    readonly Dictionary<string, LeagueStandings> tables = new();
    readonly Dictionary<string, TeamSchedule> schedules = new();
    readonly Dictionary<string, (IReadOnlyList<SportsTeam> Teams, DateTime FetchedUtc)> teams = new();
    readonly Dictionary<string, byte[]> logos = new();
    readonly Dictionary<string, Task<byte[]?>> logoLoads = new();
    readonly string logoFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notcho", "Logos");
    /// <summary>Where Back goes: the pages before this one, with the game or team each showed.</summary>
    readonly Stack<(SportsPage Page, string? Game, SportsTeam? Team)> history = new();
    bool refreshing;
    bool refreshQueued;
    bool changedWhileRefreshing;

    public SportsService(AppSettings settings, TimeProvider time)
    {
        this.settings = settings;
        this.time = time;
        this.Day = this.Today;
        // A league followed or a team picked, here or in Settings: the tab shows it and fetches for it.
        settings.Changed += (_, key) =>
        {
            if (key is not (nameof(AppSettings.SportsLeagues) or nameof(AppSettings.SportsTeams) or nameof(AppSettings.PeekItems)))
                return;
            this.Raise();
            _ = this.RefreshAsync();
        };
    }

    /// <summary>Something shown changed: new scores, a new page, a new day.</summary>
    public event EventHandler? Changed;

    /// <summary>The closed notch can show a picked game's score: turned on in Settings.</summary>
    public bool CanPin => this.settings.PeekItems.Contains(nameof(Views.PeekItem.Sports));

    DateTime UtcNow => this.time.GetUtcNow().UtcDateTime;

    DateTime Today => this.time.GetLocalNow().Date;

    // ---------------------------------------------------------------------------------------------
    // What's followed
    // ---------------------------------------------------------------------------------------------

    public IReadOnlyList<SportsLeague> Leagues => this.settings.SportsLeagues.Select(SportsLeague.Find).OfType<SportsLeague>().ToList();

    public IReadOnlyList<SportsTeam> Favourites => this.settings.SportsTeams;

    public bool IsFavourite(SportsTeam team) => this.Favourites.Any(t => t.Key == team.Key);

    public void ToggleFavourite(SportsTeam team)
    {
        var favourites = this.Favourites.ToList();
        if (!favourites.Any(t => t.Key == team.Key))
            favourites.Add(team);
        else
            favourites.RemoveAll(t => t.Key == team.Key);
        this.settings.SportsTeams = favourites;
    }

    public void SetLeagueFollowed(string league, bool followed)
    {
        var leagues = this.settings.SportsLeagues.Where(l => l != league).ToList();
        if (followed)
            leagues.Add(league);
        this.settings.SportsLeagues = SportsLeague.All.Select(l => l.Key).Where(leagues.Contains).ToList();
    }

    /// <summary>
    /// The chip that's on: your teams (when you have some) or a followed league. Anything else - a league
    /// unfollowed since, or your last team removed - falls back to the first there is.
    /// </summary>
    public string Filter
    {
        get
        {
            var chosen = this.settings.SportsFilter;
            var leagues = this.settings.SportsLeagues;
            if (chosen == MyTeams && this.Favourites.Count > 0)
                return MyTeams;
            if (chosen != null && leagues.Contains(chosen))
                return chosen;
            if (this.Favourites.Count > 0)
                return MyTeams;
            return leagues.FirstOrDefault() ?? MyTeams;
        }
    }

    public void SetFilter(string filter)
    {
        this.settings.SportsFilter = filter;
        this.history.Clear();
        if (this.Page is SportsPage.Game or SportsPage.Team)
            this.Page = SportsPage.Scores;
        this.Raise();
        _ = this.RefreshAsync();
    }

    /// <summary>The leagues the chip covers: your teams' leagues, or the one league.</summary>
    IReadOnlyList<string> FilterLeagues => this.Filter == MyTeams
        ? this.Favourites.Select(t => t.League).Distinct().ToList()
        : [this.Filter];

    // ---------------------------------------------------------------------------------------------
    // Navigation
    // ---------------------------------------------------------------------------------------------

    public SportsPage Page { get; private set; } = SportsPage.Scores;

    /// <summary>The day the scores are for, local.</summary>
    public DateTime Day { get; private set; }

    /// <summary>"league|event id" of the game showing on the Game page.</summary>
    public string? OpenGameKey { get; private set; }

    public SportsTeam? OpenTeam { get; private set; }

    /// <summary>The conference or division showing in the standings, by name.</summary>
    public string? StandingsGroupName { get; private set; }

    public bool CanGoBack => this.history.Count > 0;

    /// <summary>Back to today's scores, for the tab being opened again.</summary>
    public void Reset()
    {
        this.history.Clear();
        this.Page = SportsPage.Scores;
        this.Day = this.Today;
        this.OpenGameKey = null;
        this.OpenTeam = null;
    }

    public void StepDay(int days)
    {
        this.Day = this.Day.AddDays(days);
        this.Raise();
        _ = this.RefreshAsync();
    }

    public void GoToToday()
    {
        this.Day = this.Today;
        this.Raise();
        _ = this.RefreshAsync();
    }

    public void ShowScores() => this.ShowTop(SportsPage.Scores);

    public void ShowStandings() => this.ShowTop(SportsPage.Standings);

    void ShowTop(SportsPage page)
    {
        this.history.Clear();
        this.Page = page;
        this.Raise();
        _ = this.RefreshAsync();
    }

    /// <summary>Every league, to turn on or off, from the tab itself.</summary>
    public void ShowLeagues()
    {
        this.history.Push((this.Page, this.OpenGameKey, this.OpenTeam));
        this.Page = SportsPage.Leagues;
        this.Raise();
    }

    public bool IsFollowed(string league) => this.settings.SportsLeagues.Contains(league);

    public void ShowStandingsGroup(string name)
    {
        this.StandingsGroupName = name;
        this.Raise();
    }

    public void OpenGame(SportsGame game)
    {
        this.history.Push((this.Page, this.OpenGameKey, this.OpenTeam));
        this.Page = SportsPage.Game;
        this.OpenGameKey = $"{game.League}|{game.Id}";
        this.Raise();
        _ = this.RefreshAsync();
    }

    public void ShowTeam(SportsTeam team)
    {
        this.history.Push((this.Page, this.OpenGameKey, this.OpenTeam));
        this.Page = SportsPage.Team;
        this.OpenTeam = team;
        this.Raise();
        _ = this.RefreshAsync();
    }

    public void Back()
    {
        if (this.history.Count == 0)
            return;
        (this.Page, this.OpenGameKey, this.OpenTeam) = this.history.Pop();
        this.Raise();
        _ = this.RefreshAsync();
    }

    // ---------------------------------------------------------------------------------------------
    // What's cached
    // ---------------------------------------------------------------------------------------------

    static string BoardKey(string league, DateTime day) => $"{league}|{day:yyyyMMdd}";

    public Scoreboard? Board(string league, DateTime day) => this.boards.GetValueOrDefault(BoardKey(league, day));

    /// <summary>The league's own logo.</summary>
    public string? LeagueLogo(string league) => SportsLeague.Find(league)?.Logo;

    /// <summary>
    /// The day's games for the chip, a section per league: with your teams, only their games; with a
    /// league, all of it, your teams' games first.
    /// </summary>
    /// <param name="loading">Some league hasn't answered yet.</param>
    /// <param name="failed">Some league couldn't be reached.</param>
    public IReadOnlyList<(SportsLeague League, IReadOnlyList<SportsGame> Games)> Scores(out bool loading, out bool failed)
    {
        var favourites = this.Favourites.Select(t => t.Key).ToHashSet();
        var mine = this.Filter == MyTeams;
        var sections = new List<(SportsLeague, IReadOnlyList<SportsGame>)>();
        loading = false;
        failed = false;

        foreach (var key in this.FilterLeagues)
        {
            var board = this.Board(key, this.Day);
            loading |= board == null;
            failed |= board?.Failed == true;
            var league = SportsLeague.Find(key);
            var games = (board?.Games ?? [])
                .Where(g => !mine || favourites.Any(g.Involves))
                .OrderByDescending(g => favourites.Any(g.Involves))
                .ThenBy(g => g.State == GameState.Live ? 0 : 1)
                .ThenBy(g => g.StartUtc)
                .ToList();
            if (league != null && games.Count > 0)
                sections.Add((league, games));
        }
        return sections;
    }

    /// <summary>Each favourite's next game, for a day none of them play.</summary>
    public IReadOnlyList<SportsGame> UpNext()
    {
        var now = this.UtcNow;
        return this.Favourites
            .Select(t => this.schedules.GetValueOrDefault(t.Key)?.Games.FirstOrDefault(g => g.State != GameState.Final && g.StartUtc >= now.AddHours(-4)))
            .OfType<SportsGame>()
            .DistinctBy(g => g.Id)
            .OrderBy(g => g.StartUtc)
            .ToList();
    }

    public GameDetail? OpenGameDetail => this.OpenGameKey is { } key ? this.details.GetValueOrDefault(key) : null;

    /// <summary>The game as a scoreboard or a schedule last had it, to show while its box score loads.</summary>
    public SportsGame? OpenGameSummary
    {
        get
        {
            if (this.OpenGameKey is not { } key)
                return null;
            var id = key.Split('|')[1];
            return this.boards.Values.SelectMany(b => b.Games)
                .Concat(this.schedules.Values.SelectMany(s => s.Games))
                .FirstOrDefault(g => g.Id == id);
        }
    }

    public TeamSchedule? OpenTeamSchedule => this.OpenTeam is { } team ? this.schedules.GetValueOrDefault(team.Key) : null;

    /// <summary>The games a team's page lists: its last few results, then what's next.</summary>
    public IReadOnlyList<SportsGame> ScheduleWindow(TeamSchedule schedule)
    {
        var played = schedule.Games.Where(g => g.State == GameState.Final).TakeLast(ScheduleResults);
        var coming = schedule.Games.Where(g => g.State != GameState.Final).Take(ScheduleUpcoming);
        return played.Concat(coming).ToList();
    }

    /// <summary>The league the standings show: the chip's, or with your teams on, your first team's.</summary>
    public string? StandingsLeague => this.Filter == MyTeams
        ? this.Favourites.FirstOrDefault()?.League ?? this.settings.SportsLeagues.FirstOrDefault()
        : this.Filter;

    public LeagueStandings? Standings => this.StandingsLeague is { } league ? this.tables.GetValueOrDefault(league) : null;

    /// <summary>
    /// The group the standings show: the one picked, else the first with one of your teams in it, else the first.
    /// </summary>
    public StandingsGroup? ShownGroup(LeagueStandings standings)
    {
        var favourites = this.Favourites.Select(t => t.Key).ToHashSet();
        return standings.Groups.FirstOrDefault(g => g.Name == this.StandingsGroupName)
               ?? standings.Groups.FirstOrDefault(g => g.Rows.Any(r => favourites.Contains(r.Team.Key)))
               ?? standings.Groups.FirstOrDefault();
    }

    // ---------------------------------------------------------------------------------------------
    // The game on the closed notch
    // ---------------------------------------------------------------------------------------------

    /// <summary>"league|event id" of the game picked for the closed notch; null when none is.</summary>
    public string? PinnedKey => this.settings.SportsPinnedGame;

    static string GameKey(SportsGame game) => $"{game.League}|{game.Id}";

    public bool IsPinned(SportsGame game) => this.PinnedKey == GameKey(game);

    /// <summary>Only a game still to be decided: one that's on, or one that hasn't started (it shows once it does).</summary>
    public static bool CanBePinned(SportsGame game) => game.State is GameState.Live or GameState.Scheduled;

    /// <summary>Picks a game for the closed notch, or puts the picked one back.</summary>
    public void TogglePin(SportsGame game)
    {
        this.settings.SportsPinnedGame = this.IsPinned(game) ? null : GameKey(game);
        this.Raise();
        _ = this.RefreshAsync();
    }

    /// <summary>
    /// The picked game while it's on, for the closed notch. Before it starts there's nothing to show; once
    /// it's over the pick is let go (see <see cref="FetchDetailAsync"/>).
    /// </summary>
    public SportsGame? PinnedGame
    {
        get
        {
            if (!this.CanPin || this.PinnedKey is not { } key || !this.details.TryGetValue(key, out var detail))
                return null;
            return detail.Game.State == GameState.Live ? detail.Game : null;
        }
    }

    /// <summary>
    /// Something on is worth asking about every 20 seconds: the picked game (or its start coming up), or,
    /// while the tab is open, a game on the page showing.
    /// </summary>
    public bool NeedsPolling(bool tabOpen)
    {
        var now = this.UtcNow;
        bool Active(SportsGame g) => g.State == GameState.Live || (g.State == GameState.Scheduled && g.StartUtc <= now.AddMinutes(2) && g.StartUtc >= now.AddHours(-6));

        if (this.CanPin && this.PinnedKey is { } key)
        {
            var pinned = this.details.GetValueOrDefault(key)?.Game;
            // Not fetched yet (a pick from before a restart): poll until it is.
            if (pinned == null || Active(pinned))
                return true;
        }

        if (!tabOpen)
            return false;
        return this.Page switch
        {
            SportsPage.Scores => this.FilterLeagues.SelectMany(l => this.Board(l, this.Day)?.Games ?? []).Any(Active),
            SportsPage.Game => (this.OpenGameDetail?.Game ?? this.OpenGameSummary) is { } g && Active(g),
            SportsPage.Team => this.OpenTeamSchedule?.Games.Any(Active) == true,
            _ => false
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Refreshing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fetches whatever the page showing needs (and the game picked for the closed notch) that has gone
    /// stale. One refresh at a time; asking during one runs another after it.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (this.refreshing)
        {
            this.refreshQueued = true;
            return;
        }

        this.refreshing = true;
        this.changedWhileRefreshing = false;
        try
        {
            do
            {
                this.refreshQueued = false;
                var results = await Task.WhenAll(this.Fetches()).ConfigureAwait(true);
                this.changedWhileRefreshing |= results.Any(changed => changed);
            }
            while (this.refreshQueued);
        }
        finally
        {
            this.refreshing = false;
        }

        if (this.changedWhileRefreshing)
            this.Raise();
    }

    IEnumerable<Task<bool>> Fetches()
    {
        var wanted = new HashSet<string>();
        var fetches = new List<Task<bool>>();
        void Board(string league, DateTime day)
        {
            if (wanted.Add(BoardKey(league, day)))
                fetches.Add(this.FetchBoardAsync(league, day));
        }

        if (this.CanPin && this.PinnedKey is { } pinned)
            fetches.Add(this.FetchDetailAsync(pinned));

        switch (this.Page)
        {
            case SportsPage.Scores:
                foreach (var league in this.FilterLeagues)
                    Board(league, this.Day);
                // Your teams' next games, for a day none of them play.
                if (this.Filter == MyTeams)
                    fetches.AddRange(this.Favourites.Select(this.FetchScheduleAsync));
                break;

            case SportsPage.Standings when this.StandingsLeague is { } league:
                fetches.Add(this.FetchStandingsAsync(league));
                break;

            // Already asked for when it's the pinned game.
            case SportsPage.Game when this.OpenGameKey is { } key && key != this.PinnedKey:
                fetches.Add(this.FetchDetailAsync(key));
                break;

            case SportsPage.Team when this.OpenTeam is { } team:
                fetches.Add(this.FetchScheduleAsync(team));
                break;
        }
        return fetches;
    }

    void Raise() => this.Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// A key the closed notch is rebuilt on: the picked game and its score. Not the clock, which would
    /// rebuild it on every poll.
    /// </summary>
    public string PeekKey => this.PinnedGame is { } g ? $"{g.Id}|{g.Away.Score}|{g.Home.Score}" : "";

    TimeSpan BoardStaleAfter(Scoreboard board)
    {
        if (board.Failed)
            return FailedRetry;
        var now = this.UtcNow;
        var onNow = board.Games.Any(g => g.State == GameState.Live || (g.State == GameState.Scheduled && g.StartUtc <= now.AddMinutes(2) && g.StartUtc >= now.AddHours(-6)));
        if (onNow)
            return LiveStale;
        return board.Day < this.Today ? PastStale : QuietStale;
    }

    async Task<bool> FetchBoardAsync(string league, DateTime day)
    {
        var key = BoardKey(league, day);
        var old = this.boards.GetValueOrDefault(key);
        if (old != null && this.UtcNow - old.FetchedUtc < this.BoardStaleAfter(old))
            return false;

        var board = await this.GetAsync($"{Site}{league}/scoreboard?dates={day:yyyyMMdd}&limit=200", root =>
        {
            var games = root.TryGetProperty("events", out var events)
                ? events.EnumerateArray().Select(e => ParseGame(e, league)).OfType<SportsGame>().ToList()
                : [];
            return new Scoreboard(league, day, games, this.UtcNow);
        }).ConfigureAwait(true);

        if (board == null)
        {
            // Keep what was there to show; only mark it to be tried again soon.
            this.boards[key] = (old ?? new Scoreboard(league, day, [], this.UtcNow)) with { FetchedUtc = this.UtcNow, Failed = true };
            return old == null;
        }

        this.boards[key] = board;
        return old == null || old.Failed || !old.Games.Select(g => g.Signature).SequenceEqual(board.Games.Select(g => g.Signature));
    }

    async Task<bool> FetchDetailAsync(string key)
    {
        var old = this.details.GetValueOrDefault(key);
        if (old != null)
        {
            var startsSoon = old.Game.State == GameState.Scheduled && old.Game.StartUtc <= this.UtcNow.AddMinutes(2);
            var staleAfter = old.Game.State switch
            {
                GameState.Live => LiveStale,
                GameState.Final or GameState.Off => TimeSpan.FromDays(1),
                _ => startsSoon ? LiveStale : QuietStale
            };
            if (this.UtcNow - old.FetchedUtc < staleAfter)
                return false;
        }

        var league = key.Split('|')[0];
        var id = key.Split('|')[1];
        var detail = await this.GetAsync($"{Site}{league}/summary?event={id}", root => this.ParseDetail(root, league)).ConfigureAwait(true);
        if (detail == null)
            return false;

        // The summary has no recap line or network; the scoreboard's are kept.
        var summary = key == this.OpenGameKey ? this.OpenGameSummary : null;
        detail = detail with { Game = detail.Game with { Recap = detail.Game.Recap ?? summary?.Recap, Broadcast = detail.Game.Broadcast ?? summary?.Broadcast } };

        this.details[key] = detail;

        // The picked game is over: it comes off the closed notch, and the pick with it.
        if (key == this.PinnedKey && detail.Game.State is GameState.Final or GameState.Off)
        {
            this.settings.SportsPinnedGame = null;
            return true;
        }

        return old == null || old.Game.Signature != detail.Game.Signature || old.Stats.Count != detail.Stats.Count || !old.Stats.Zip(detail.Stats).All(p => p.First.Away == p.Second.Away && p.First.Home == p.Second.Home);
    }

    async Task<bool> FetchStandingsAsync(string league)
    {
        var old = this.tables.GetValueOrDefault(league);
        if (old != null && this.UtcNow - old.FetchedUtc < TablesStale)
            return false;

        var table = await this.GetAsync($"https://site.api.espn.com/apis/v2/sports/{league}/standings", root => this.ParseStandings(root, league)).ConfigureAwait(true);
        if (table == null)
            return false;
        this.tables[league] = table;
        return true;
    }

    async Task<bool> FetchScheduleAsync(SportsTeam team)
    {
        var old = this.schedules.GetValueOrDefault(team.Key);
        var live = old?.Games.Any(g => g.State == GameState.Live) == true;
        if (old != null && this.UtcNow - old.FetchedUtc < (live ? LiveStale : TablesStale))
            return false;

        var url = $"{Site}{team.League}/teams/{team.Id}/schedule";
        var schedule = await this.GetAsync(url, root => this.ParseSchedule(root, team)).ConfigureAwait(true);
        if (schedule == null)
            return false;

        // Soccer's schedule is results only; the fixtures to come are asked for separately.
        if (SportsLeague.Find(team.League)?.IsSoccer == true)
        {
            var fixtures = await this.GetAsync(url + "?fixture=true", root => this.ParseSchedule(root, team)).ConfigureAwait(true);
            if (fixtures != null)
                schedule = schedule with { Games = schedule.Games.Concat(fixtures.Games).DistinctBy(g => g.Id).OrderBy(g => g.StartUtc).ToList() };
        }

        this.schedules[team.Key] = schedule;
        return old == null || !old.Games.Select(g => g.Signature).SequenceEqual(schedule.Games.Select(g => g.Signature));
    }

    /// <summary>Every team in a league, for picking favourites. Cached for a day.</summary>
    public async Task<IReadOnlyList<SportsTeam>> TeamsAsync(string league)
    {
        if (this.teams.TryGetValue(league, out var cached) && this.UtcNow - cached.FetchedUtc < TeamsStale)
            return cached.Teams;

        var list = await this.GetAsync($"{Site}{league}/teams?limit=1000", root =>
        {
            var found = new List<SportsTeam>();
            if (root.TryGetProperty("sports", out var sports))
            {
                foreach (var entry in sports.EnumerateArray()
                             .SelectMany(s => Array(s, "leagues"))
                             .SelectMany(l => Array(l, "teams"))
                             .Where(t => t.TryGetProperty("team", out _)))
                {
                    found.Add(ParseTeam(entry.GetProperty("team"), league));
                }
            }
            return (IReadOnlyList<SportsTeam>)found.OrderBy(t => t.Name).ToList();
        }).ConfigureAwait(true);

        if (list == null)
            return cached.Teams ?? [];
        this.teams[league] = (list, this.UtcNow);
        return list;
    }

    /// <summary>Fetches the followed leagues' team lists, so a search right after has them to hand.</summary>
    public Task PreloadTeamsAsync() => Task.WhenAll(this.settings.SportsLeagues.Select(this.TeamsAsync));

    /// <summary>
    /// Teams in the followed leagues matching <paramref name="query"/>, best first: the abbreviation
    /// exactly, then a name that starts with it, then one with a word that does, then any name with it in.
    /// </summary>
    public async Task<IReadOnlyList<SportsTeam>> SearchTeamsAsync(string query)
    {
        var text = query.Trim();
        if (text.Length == 0)
            return [];

        var all = await Task.WhenAll(this.settings.SportsLeagues.Select(this.TeamsAsync)).ConfigureAwait(true);
        return all.SelectMany(t => t)
            .Select(t => (Team: t, Rank: MatchRank(t, text)))
            .Where(m => m.Rank >= 0)
            .OrderBy(m => m.Rank)
            .ThenBy(m => m.Team.Name)
            .Take(10)
            .Select(m => m.Team)
            .ToList();
    }

    /// <summary>How well a team matches what's typed: lower is better, -1 not at all.</summary>
    static int MatchRank(SportsTeam team, string text)
    {
        if (team.Abbreviation.Equals(text, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (team.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (team.Name.Contains(" " + text, StringComparison.OrdinalIgnoreCase))
            return 2;
        return team.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ? 3 : -1;
    }

    /// <summary>GETs and parses JSON; null on any failure (offline, a timeout, a shape ESPN changed).</summary>
    async Task<T?> GetAsync<T>(string url, Func<JsonElement, T> parse) where T : class?
    {
        try
        {
            using var doc = JsonDocument.Parse(await this.http.GetStringAsync(url).ConfigureAwait(true));
            return parse(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Logos
    // ---------------------------------------------------------------------------------------------

    /// <summary>A logo already loaded, to show straight away.</summary>
    public byte[]? CachedLogo(string url) => this.logos.GetValueOrDefault(url);

    /// <summary>
    /// A logo as PNG bytes: from memory, then the disk cache, then ESPN - shrunk to 96 px by its image
    /// service, which is a 2x 48 pt logo at a few KB rather than the 500 px original.
    /// </summary>
    public Task<byte[]?> LogoAsync(string url)
    {
        if (this.logos.TryGetValue(url, out var bytes))
            return Task.FromResult<byte[]?>(bytes);
        if (this.logoLoads.TryGetValue(url, out var loading))
            return loading;

        var load = this.LoadLogoAsync(url);
        this.logoLoads[url] = load;
        return load;
    }

    async Task<byte[]?> LoadLogoAsync(string url)
    {
        var file = Path.Combine(this.logoFolder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + ".png");
        try
        {
            byte[] bytes;
            if (File.Exists(file))
            {
                bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(true);
            }
            else
            {
                bytes = await this.http.GetByteArrayAsync(Resized(url)).ConfigureAwait(true);
                Directory.CreateDirectory(this.logoFolder);
                await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(true);
            }
            this.logos[url] = bytes;
            return bytes;
        }
        catch
        {
            return null;
        }
        finally
        {
            this.logoLoads.Remove(url);
        }
    }

    static string Resized(string url)
    {
        const string cdn = "https://a.espncdn.com/";
        return url.StartsWith(cdn, StringComparison.OrdinalIgnoreCase) && !url.Contains("/combiner/", StringComparison.Ordinal)
            ? $"{cdn}combiner/i?img=/{Uri.EscapeDataString(url[cdn.Length..]).Replace("%2F", "/")}&w=96&h=96"
            : url;
    }

    // ---------------------------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------------------------

    static IEnumerable<JsonElement> Array(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static JsonElement Obj(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    /// <summary>A dark-background logo from a "logos" list, else the first.</summary>
    static string? DarkLogo(JsonElement e)
    {
        var all = Array(e, "logos").ToList();
        var dark = all.FirstOrDefault(l => Array(l, "rel").Any(r => r.GetString() == "dark"));
        var chosen = dark.ValueKind == JsonValueKind.Object ? dark : all.FirstOrDefault();
        return Str(chosen, "href");
    }

    static SportsTeam ParseTeam(JsonElement t, string league)
    {
        var name = Str(t, "displayName") ?? Str(t, "name") ?? "?";
        var abbreviation = Str(t, "abbreviation") ?? name[..Math.Min(3, name.Length)].ToUpperInvariant();
        var logo = Str(t, "logoDark") ?? DarkLogo(t) ?? Str(t, "logo");
        return new SportsTeam(
            league,
            Str(t, "id") ?? "",
            abbreviation,
            name,
            Str(t, "shortDisplayName") ?? Str(t, "nickname") ?? name,
            logo,
            Str(t, "color"),
            Str(t, "alternateColor")
        );
    }

    static string? Score(JsonElement competitor)
    {
        if (!competitor.TryGetProperty("score", out var score))
            return null;
        return score.ValueKind switch
        {
            JsonValueKind.String => score.GetString(),
            JsonValueKind.Object => Str(score, "displayValue"),
            _ => null
        };
    }

    /// <summary>The season record: "17-6-4" from a scoreboard, "3-4-2, 8 PTS" trimmed to "3-4-2" from a schedule.</summary>
    static string? Record(JsonElement competitor)
    {
        var record = Array(competitor, "records").Select(r => Str(r, "summary")).FirstOrDefault()
                     ?? Array(competitor, "record").Select(r => Str(r, "displayValue")).FirstOrDefault();
        return record?.Split(',')[0];
    }

    static SportsGame? ParseGame(JsonElement ev, string league)
    {
        var comp = Array(ev, "competitions").FirstOrDefault();
        if (comp.ValueKind != JsonValueKind.Object)
            return null;

        var sides = Array(comp, "competitors").ToList();
        var homeElement = sides.FirstOrDefault(c => Str(c, "homeAway") == "home");
        var awayElement = sides.FirstOrDefault(c => Str(c, "homeAway") == "away");
        if (homeElement.ValueKind != JsonValueKind.Object || awayElement.ValueKind != JsonValueKind.Object)
            return null;

        GameSide Side(JsonElement c) => new(
            ParseTeam(Obj(c, "team"), league),
            Score(c),
            c.TryGetProperty("winner", out var w) && w.ValueKind == JsonValueKind.True,
            Record(c),
            Array(c, "linescores").Select(l => Str(l, "displayValue") ?? "").ToList()
        );

        var status = Obj(comp, "status").ValueKind == JsonValueKind.Object ? Obj(comp, "status") : Obj(ev, "status");
        var type = Obj(status, "type");
        var name = Str(type, "name") ?? "";
        var state = name switch
        {
            _ when name.Contains("POSTPONED") || name.Contains("CANCELED") || name.Contains("SUSPENDED") || name.Contains("DELAYED") || name.Contains("FORFEIT") => GameState.Off,
            _ => Str(type, "state") switch
            {
                "in" => GameState.Live,
                "post" => GameState.Final,
                _ => GameState.Scheduled
            }
        };

        var dateText = Str(comp, "date") ?? Str(ev, "date");
        var start = DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
        var timeValid = !comp.TryGetProperty("timeValid", out var tv) || tv.ValueKind != JsonValueKind.False;
        var shortDetail = Str(type, "shortDetail") ?? Str(type, "detail") ?? Str(type, "description") ?? "";

        var broadcast = Array(comp, "broadcasts").SelectMany(b => Array(b, "names")).Select(n => n.GetString()).FirstOrDefault()
                        ?? Str(comp, "broadcast")
                        ?? Array(comp, "geoBroadcasts").Select(b => Str(Obj(b, "media"), "shortName")).FirstOrDefault();
        var headline = Array(comp, "headlines").FirstOrDefault();

        return new SportsGame(
            league,
            Str(ev, "id") ?? Str(comp, "id") ?? "",
            start,
            state,
            state == GameState.Scheduled ? (timeValid ? "" : "TBD") : shortDetail,
            Side(awayElement),
            Side(homeElement),
            broadcast,
            Str(Obj(comp, "venue"), "fullName"),
            Str(headline, "shortLinkText") ?? Str(headline, "description")
        );
    }

    /// <summary>The box score figures worth comparing, per sport, by ESPN's names - with shorter labels.</summary>
    static readonly IReadOnlyDictionary<string, (string Name, string Label)[]> Curated = new Dictionary<string, (string, string)[]>
    {
        ["hockey"] = [("shotsTotal", "Shots"), ("powerPlayPct", "Power Play %"), ("faceoffPercent", "Faceoff %"), ("hits", "Hits"), ("blockedShots", "Blocked Shots"), ("takeaways", "Takeaways"), ("giveaways", "Giveaways"), ("penaltyMinutes", "Penalty Minutes")],
        ["basketball"] = [("fieldGoalPct", "Field Goal %"), ("threePointFieldGoalPct", "3-Point %"), ("freeThrowPct", "Free Throw %"), ("totalRebounds", "Rebounds"), ("assists", "Assists"), ("steals", "Steals"), ("blocks", "Blocks"), ("turnovers", "Turnovers"), ("pointsInPaint", "Points in Paint")],
        ["football"] = [("totalYards", "Total Yards"), ("netPassingYards", "Passing Yards"), ("rushingYards", "Rushing Yards"), ("firstDowns", "First Downs"), ("thirdDownEff", "3rd Down"), ("turnovers", "Turnovers"), ("totalPenaltiesYards", "Penalties"), ("possessionTime", "Possession")],
        ["soccer"] = [("possessionPct", "Possession %"), ("totalShots", "Shots"), ("shotsOnTarget", "Shots on Target"), ("wonCorners", "Corners"), ("saves", "Saves"), ("foulsCommitted", "Fouls"), ("offsides", "Offsides"), ("yellowCards", "Yellow Cards")],
        ["baseball"] = [("hits", "Hits"), ("homeRuns", "Home Runs"), ("RBIs", "RBIs"), ("walks", "Walks"), ("strikeouts", "Strikeouts"), ("runnersLeftOnBase", "Left on Base"), ("avg", "Batting Avg")]
    };

    GameDetail? ParseDetail(JsonElement root, string league)
    {
        var header = Obj(root, "header");
        if (ParseGame(header, league) is not { } game)
            return null;

        // Each side's figures by name, groups (baseball's batting, pitching) flattened, first name wins.
        var figures = new Dictionary<string, Dictionary<string, (string Label, string Value)>>();
        foreach (var team in Array(Obj(root, "boxscore"), "teams"))
        {
            var id = Str(Obj(team, "team"), "id") ?? "";
            var byName = new Dictionary<string, (string, string)>();
            var flat = Array(team, "statistics").SelectMany(s => s.TryGetProperty("stats", out _) ? Array(s, "stats") : [s]);
            foreach (var s in flat.Where(s => Str(s, "name") is { } n && !byName.ContainsKey(n)))
                byName[Str(s, "name")!] = (Str(s, "label") ?? Str(s, "abbreviation") ?? Str(s, "name")!, Str(s, "displayValue") ?? "");
            figures[id] = byName;
        }

        var stats = new List<StatComparison>();
        if (figures.TryGetValue(game.Away.Team.Id, out var away) && figures.TryGetValue(game.Home.Team.Id, out var home))
        {
            var sport = SportsLeague.Find(league)?.Sport ?? "";
            var wanted = Curated.GetValueOrDefault(sport) ?? [];
            stats.AddRange(wanted
                .Where(w => away.ContainsKey(w.Name) && home.ContainsKey(w.Name))
                .Select(w => Compare(w.Label, away[w.Name].Value, home[w.Name].Value)));
            // A sport without a list, or ESPN renamed things: the first few figures both sides have.
            if (stats.Count < 3)
            {
                stats.Clear();
                stats.AddRange(away.Keys.Where(home.ContainsKey).Take(8).Select(k => Compare(away[k].Label, away[k].Value, home[k].Value)));
            }
        }

        var leaders = new Dictionary<string, (string? Away, string? AwayValue, string? Home, string? HomeValue)>();
        var order = new List<string>();
        foreach (var team in Array(root, "leaders"))
        {
            var isHome = Str(Obj(team, "team"), "id") == game.Home.Team.Id;
            foreach (var category in Array(team, "leaders"))
            {
                var label = Str(category, "displayName") ?? Str(category, "name") ?? "";
                var top = Array(category, "leaders").FirstOrDefault();
                var athlete = Obj(top, "athlete");
                var who = Str(athlete, "shortName") ?? Str(athlete, "displayName");
                var value = ShortLeaderValue(Str(top, "displayValue"));
                if (!leaders.ContainsKey(label))
                    order.Add(label);
                var entry = leaders.GetValueOrDefault(label);
                leaders[label] = isHome ? (entry.Away, entry.AwayValue, who, value) : (who, value, entry.Home, entry.HomeValue);
            }
        }

        return new GameDetail(
            game,
            stats,
            order.Take(3).Select(c => new GameLeader(c, leaders[c].Away, leaders[c].AwayValue, leaders[c].Home, leaders[c].HomeValue)).ToList(),
            this.UtcNow
        );
    }

    /// <summary>
    /// A leader's figure short enough to sit beside the name: football's "27/45, 304 YDS, 1 TD, 2 INT" is
    /// just "304 YDS"; anything else up to its first comma.
    /// </summary>
    static string? ShortLeaderValue(string? value)
    {
        var parts = value?.Split(", ");
        return parts?.FirstOrDefault(p => p.EndsWith(" YDS", StringComparison.Ordinal)) ?? parts?.FirstOrDefault();
    }

    static StatComparison Compare(string label, string away, string home) => new(label, away, home, Magnitude(away), Magnitude(home));

    /// <summary>A figure as a number for its bar: "28:29" in seconds, "4-12" or "28/40" as a ratio, "49" or "33.3%" as is.</summary>
    static double Magnitude(string text)
    {
        var t = text.Trim().TrimEnd('%');
        var ci = CultureInfo.InvariantCulture;
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            return double.TryParse(parts[0], ci, out var m) && double.TryParse(parts[1], ci, out var s) ? m * 60 + s : 0;
        }

        var pair = t.Split('-', '/');
        if (pair.Length == 2 && double.TryParse(pair[0], ci, out var made) && double.TryParse(pair[1], ci, out var tried))
            return tried > 0 ? made / tried : 0;

        return double.TryParse(t, NumberStyles.Any, ci, out var v) ? Math.Abs(v) : 0;
    }

    /// <summary>The standings columns per sport, by ESPN's stat names.</summary>
    static (string Label, string Name)[] Columns(string sport) => sport switch
    {
        "hockey" => [("GP", "gamesPlayed"), ("W", "wins"), ("L", "losses"), ("OTL", "otLosses"), ("PTS", "points"), ("DIFF", "pointDifferential")],
        "basketball" => [("W", "wins"), ("L", "losses"), ("PCT", "winPercent"), ("GB", "gamesBehind"), ("L10", "Last Ten Games"), ("STRK", "streak")],
        "football" => [("W", "wins"), ("L", "losses"), ("T", "ties"), ("PCT", "winPercent"), ("DIFF", "pointDifferential"), ("STRK", "streak")],
        "baseball" => [("W", "wins"), ("L", "losses"), ("PCT", "winPercent"), ("GB", "gamesBehind"), ("DIFF", "pointDifferential"), ("STRK", "streak")],
        "soccer" => [("GP", "gamesPlayed"), ("W", "wins"), ("D", "ties"), ("L", "losses"), ("GD", "pointDifferential"), ("PTS", "points")],
        _ => [("W", "wins"), ("L", "losses")]
    };

    LeagueStandings ParseStandings(JsonElement root, string league)
    {
        var columns = Columns(SportsLeague.Find(league)?.Sport ?? "");
        var groups = new List<StandingsGroup>();

        var children = Array(root, "children").ToList();
        var sources = children.Count > 0 ? children : [root];
        foreach (var child in sources)
        {
            var entries = Array(Obj(child, "standings"), "entries").Select(e =>
            {
                var stats = Array(e, "stats")
                    .Where(s => Str(s, "name") != null)
                    .GroupBy(s => Str(s, "name")!)
                    .ToDictionary(g => g.Key, g => g.First());
                double Number(string name) => stats.TryGetValue(name, out var s) && s.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                var values = columns.Select(c => (stats.TryGetValue(c.Name, out var s) ? Str(s, "displayValue") ?? "" : "").Split(',')[0]).ToList();
                var seed = Number("playoffSeed") is > 0 and var p ? p : Number("rank");
                return (Row: new StandingsRow(ParseTeam(Obj(e, "team"), league), values), Seed: seed);
            }).ToList();

            // ESPN's order isn't always the table's; its seed (or a soccer table's rank) is.
            var ordered = entries.All(e => e.Seed > 0) ? entries.OrderBy(e => e.Seed).ToList() : entries;
            var name = Str(child, "name") ?? "";
            var abbreviation = Str(child, "abbreviation") ?? "";
            // ESPN's abbreviation where it's a real one ("AFC", "East"), not a slug ("big10") or a season ("2026-2027").
            var chip = abbreviation.Any(char.IsUpper) && abbreviation.Length <= 8 ? abbreviation : name.Replace(" Conference", "");
            if (ordered.Count > 0)
                groups.Add(new StandingsGroup(name, chip, ordered.Select(e => e.Row).ToList()));
        }

        return new LeagueStandings(league, columns.Select(c => c.Label).ToList(), groups, this.UtcNow);
    }

    TeamSchedule ParseSchedule(JsonElement root, SportsTeam team)
    {
        var games = Array(root, "events").Select(e => ParseGame(e, team.League)).OfType<SportsGame>().OrderBy(g => g.StartUtc).ToList();
        var info = Obj(root, "team");
        var record = Str(info, "recordSummary");
        return new TeamSchedule(team, record, games, this.UtcNow);
    }
}
