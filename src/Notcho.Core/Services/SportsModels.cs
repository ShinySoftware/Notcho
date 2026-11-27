namespace Notcho.Services;

/// <summary>A league the Sports tab can follow, by its ESPN path ("hockey/nhl").</summary>
/// <param name="Key">ESPN's sport and league, as they appear in its URLs.</param>
/// <param name="Short">What the league chip says.</param>
public sealed record SportsLeague(string Key, string Name, string Short)
{
    public string Sport => this.Key.Split('/')[0];

    public bool IsSoccer => this.Sport == "soccer";

    /// <summary>Every league on offer, in Settings' order. Team sports only: ESPN's racing, golf and tennis have no home and away.</summary>
    public static readonly IReadOnlyList<SportsLeague> All =
    [
        new("football/nfl", "NFL", "NFL"),
        new("basketball/nba", "NBA", "NBA"),
        new("baseball/mlb", "MLB", "MLB"),
        new("hockey/nhl", "NHL", "NHL"),
        new("basketball/wnba", "WNBA", "WNBA"),
        new("soccer/usa.1", "MLS", "MLS"),
        new("soccer/usa.nwsl", "NWSL", "NWSL"),
        new("soccer/eng.1", "Premier League", "EPL"),
        new("soccer/esp.1", "LaLiga", "LaLiga"),
        new("soccer/ger.1", "Bundesliga", "BUN"),
        new("soccer/ita.1", "Serie A", "Serie A"),
        new("soccer/fra.1", "Ligue 1", "Ligue 1"),
        new("soccer/uefa.champions", "UEFA Champions League", "UCL"),
        new("football/college-football", "College Football", "NCAAF"),
        new("basketball/mens-college-basketball", "Men's College Basketball", "NCAAM"),
        new("basketball/womens-college-basketball", "Women's College Basketball", "NCAAW")
    ];

    /// <summary>Followed until changed in Settings.</summary>
    public static readonly IReadOnlyList<string> Defaults = ["football/nfl", "basketball/nba", "baseball/mlb", "hockey/nhl", "soccer/eng.1"];

    public static SportsLeague? Find(string key) => All.FirstOrDefault(l => l.Key == key);
}

/// <summary>A team, with what's needed to draw it without asking ESPN again.</summary>
/// <param name="Logo">A logo drawn for dark backgrounds where ESPN has one.</param>
/// <param name="Color">ESPN's primary colour, as hex without the #.</param>
public sealed record SportsTeam(string League, string Id, string Abbreviation, string Name, string ShortName, string? Logo, string? Color, string? AlternateColor = null)
{
    public string Key => $"{this.League}:{this.Id}";
}

public enum GameState
{
    Scheduled,
    Live,
    Final,
    /// <summary>Postponed, cancelled, suspended or delayed.</summary>
    Off
}

/// <summary>One side of a game.</summary>
/// <param name="Periods">The score by period, for the line score.</param>
public sealed record GameSide(SportsTeam Team, string? Score, bool Winner, string? Record, IReadOnlyList<string> Periods)
{
    public double ScoreValue => double.TryParse(this.Score, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// <param name="Status">ESPN's short status: "Final/OT", "2nd - 12:34", "Halftime", "45'+2'".</param>
/// <param name="Recap">A line about how it went, once it's over.</param>
public sealed record SportsGame(
    string League,
    string Id,
    DateTime StartUtc,
    GameState State,
    string Status,
    GameSide Away,
    GameSide Home,
    string? Broadcast,
    string? Venue,
    string? Recap
)
{
    public bool Involves(string teamKey) => this.Away.Team.Key == teamKey || this.Home.Team.Key == teamKey;

    /// <summary>Changes whenever anything a card shows does.</summary>
    public string Signature => $"{this.Id}|{this.State}|{this.Status}|{this.Away.Score}|{this.Home.Score}";
}

/// <summary>A box score figure for both sides, with the numbers its bar is drawn from.</summary>
public sealed record StatComparison(string Label, string Away, string Home, double AwayValue, double HomeValue);

/// <summary>A category's leader on each side: "Points", "C. Dach", "1".</summary>
public sealed record GameLeader(string Category, string? Away, string? AwayValue, string? Home, string? HomeValue);

public sealed record GameDetail(SportsGame Game, IReadOnlyList<StatComparison> Stats, IReadOnlyList<GameLeader> Leaders, DateTime FetchedUtc);

public sealed record StandingsRow(SportsTeam Team, IReadOnlyList<string> Values);

/// <summary>A conference, division or a whole league table.</summary>
/// <param name="Short">What its chip says: "East", "AFC", "Big Ten".</param>
public sealed record StandingsGroup(string Name, string Short, IReadOnlyList<StandingsRow> Rows);

public sealed record LeagueStandings(string League, IReadOnlyList<string> Columns, IReadOnlyList<StandingsGroup> Groups, DateTime FetchedUtc);

public sealed record TeamSchedule(SportsTeam Team, string? Record, IReadOnlyList<SportsGame> Games, DateTime FetchedUtc);

/// <summary>A league's games on a day.</summary>
/// <param name="Failed">ESPN couldn't be reached: retried sooner than a good answer would be.</param>
public sealed record Scoreboard(string League, DateTime Day, IReadOnlyList<SportsGame> Games, string? Logo, DateTime FetchedUtc, bool Failed = false);

/// <summary>Where the Sports tab is.</summary>
public enum SportsPage
{
    Scores,
    Standings,
    Game,
    Team,
    /// <summary>Every league, to follow or unfollow.</summary>
    Leagues
}
