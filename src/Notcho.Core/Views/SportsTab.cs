using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Notcho.Services;

namespace Notcho.Views;

/// <summary>
/// Apple Sports, in a notch. League chips across the top (your teams first), then the day's games as
/// cards: the logos either side, the scores, and between them the clock or the start time. A card opens
/// the game - its line score, the team stats side by side on split bars, and each side's leaders. Standings
/// show a league's table, your teams picked out; a team opens its schedule, where the star makes it a favourite.
/// </summary>
sealed class SportsTab : VerticalStackLayout
{
    const double CardHeight = 58;
    const double CardSpacing = 8;
    /// <summary>Two cards a row, across the panel.</summary>
    const double CardWidth = (Ui.ContentWidth - CardSpacing) / 2;
    /// <summary>How tall the games or a table get before they scroll.</summary>
    const double ListHeight = 4 * CardHeight + 3 * CardSpacing + 28;
    const double RowHeight = 26;

    readonly SportsService sports;

    public SportsTab(SportsService sports, Action openSettings)
    {
        this.sports = sports;
        this.Spacing = 10;

        if (sports.Leagues.Count == 0 && sports.Favourites.Count == 0)
        {
            this.Children.Add(Empty(openSettings));
            return;
        }

        this.Children.Add(this.Header());
        this.Children.Add(sports.Page switch
        {
            SportsPage.Standings => this.StandingsPage(),
            SportsPage.Game => this.GamePage(),
            SportsPage.Team => this.TeamPage(),
            SportsPage.Leagues => this.LeaguesPage(),
            _ => this.ScoresPage()
        });
    }

    static View Empty(Action openSettings)
        => Ui.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new HorizontalStackLayout { Spacing = 8, Children = { Ui.Symbol("sportscourt.fill", 16, Ui.Green), Ui.Label("Sports", 16, bold: true) } },
                Ui.Label("Follow your leagues and pick your teams for scores, schedules and standings here.", 12, color: Ui.Secondary),
                new HorizontalStackLayout { Children = { Ui.Chip("Choose in Settings", openSettings, selected: true, symbol: "gearshape.fill") } }
            }
        }, new Thickness(14, 12));

    // ---------------------------------------------------------------------------------------------
    // Header
    // ---------------------------------------------------------------------------------------------

    /// <summary>The chips and the Scores / Standings switch; on a game or a team, Back and a title instead.</summary>
    View Header()
    {
        var grid = new Grid { ColumnSpacing = 8, ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };

        if (this.sports.Page is SportsPage.Game or SportsPage.Team or SportsPage.Leagues)
        {
            var title = this.sports.Page switch
            {
                SportsPage.Team => this.sports.OpenTeam?.Name ?? "",
                SportsPage.Leagues => "Leagues",
                _ => GameTitle(this.sports.OpenGameDetail?.Game ?? this.sports.OpenGameSummary)
            };
            var back = new HorizontalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Ui.SymbolButton("chevron.left", this.sports.Back, 12, Ui.Text, "Back", padding: new Thickness(4)),
                    Ui.Label(title, 14, bold: true).CenteredVertically()
                }
            };
            grid.Children.Add(back.At(column: 0));

            if (this.sports.Page == SportsPage.Team && this.sports.OpenTeam is { } team)
            {
                var favourite = this.sports.IsFavourite(team);
                grid.Children.Add(Ui.Chip(favourite ? "Favourite" : "Add to My Teams", () => this.sports.ToggleFavourite(team), selected: favourite, tint: Ui.Yellow, symbol: favourite ? "star.fill" : "star").At(column: 1));
            }

            // With the closed notch's score turned on in Settings, a game still to be decided can go there.
            var game = this.sports.OpenGameDetail?.Game ?? this.sports.OpenGameSummary;
            if (this.sports.Page == SportsPage.Game && this.sports.CanPin && game != null && SportsService.CanBePinned(game))
            {
                var pinned = this.sports.IsPinned(game);
                var label = pinned ? "On the notch" : game.State == GameState.Live ? "Show on notch" : "Show on notch when it starts";
                grid.Children.Add(Ui.Chip(label, () => this.sports.TogglePin(game), selected: pinned, tint: Ui.Orange, symbol: pinned ? "pin.fill" : "pin").At(column: 1));
            }
            return grid;
        }

        var chips = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        var filter = this.sports.Filter;
        if (this.sports.Favourites.Count > 0)
            chips.Children.Add(Ui.Chip("My Teams", () => this.sports.SetFilter(SportsService.MyTeams), filter == SportsService.MyTeams, symbol: "star.fill"));
        foreach (var league in this.sports.Leagues)
        {
            var key = league.Key;
            chips.Children.Add(this.LeagueChip(league, filter == key, () => this.sports.SetFilter(key)));
        }
        // After the leagues: turn them on and off, right here.
        chips.Children.Add(Ui.SymbolButton("square.and.pencil", this.sports.ShowLeagues, 13, Ui.Secondary, "Choose leagues", Ui.Raised, new Thickness(7, 5)));
        // Many leagues scroll sideways rather than crowd the switch.
        grid.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = chips
        }.At(column: 0));

        var standings = this.sports.Page == SportsPage.Standings;
        grid.Children.Add(new HorizontalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Ui.SymbolButton("sportscourt.fill", this.sports.ShowScores, 14, standings ? Ui.Secondary : Ui.Text, "Scores", standings ? null : Ui.Raised, new Thickness(7, 5)),
                Ui.SymbolButton("list.number", this.sports.ShowStandings, 14, standings ? Ui.Text : Ui.Secondary, "Standings", standings ? Ui.Raised : null, new Thickness(7, 5))
            }
        }.At(column: 1));
        return grid;
    }

    static string GameTitle(SportsGame? game) => game == null ? "" : $"{game.Away.Team.ShortName} at {game.Home.Team.ShortName}";

    /// <summary>A chip with the league's logo, once a scoreboard has said what it is, before its short name.</summary>
    /// <param name="text">What it says; the league's short name unless given.</param>
    View LeagueChip(SportsLeague league, bool selected, Action tapped, string? text = null)
    {
        var ink = selected ? Colors.Black : Ui.Text;
        var row = new HorizontalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
        if (this.sports.LeagueLogo(league.Key) is { } logo)
            row.Children.Add(this.Logo(logo, 15));
        row.Children.Add(new Label { Text = text ?? league.Short, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = ink, VerticalTextAlignment = TextAlignment.Center });

        var chip = new Border
        {
            AutomationId = league.Short,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            BackgroundColor = selected ? Ui.Text : Ui.Raised,
            HeightRequest = 26,
            Padding = new Thickness(10, 0),
            Content = row
        };
        return chip.Pressable(tapped);
    }

    // ---------------------------------------------------------------------------------------------
    // Scores
    // ---------------------------------------------------------------------------------------------

    View ScoresPage()
    {
        var page = new VerticalStackLayout { Spacing = 8 };
        page.Children.Add(this.DayBar());

        var sections = this.sports.Scores(out var loading, out var failed);
        if (sections.Count == 0)
        {
            page.Children.Add(this.NoGames(loading, failed));
            return page;
        }

        var list = new VerticalStackLayout { Spacing = 6 };
        var rows = 0;
        foreach (var (league, games) in sections)
        {
            // A league's name over its games when there's more than one league, or it's your teams across them.
            if (sections.Count > 1 || this.sports.Filter == SportsService.MyTeams)
                list.Children.Add(this.LeagueCaption(league));
            list.Children.Add(this.Cards(games));
            rows += (games.Count + 1) / 2;
        }

        page.Children.Add(rows * (CardHeight + CardSpacing) > ListHeight
            ? new ScrollView { Content = list, HeightRequest = ListHeight }
            : list);
        return page;
    }

    /// <summary>‹ Today › - the name of the day, which taps back to today.</summary>
    View DayBar()
    {
        var day = this.sports.Day;
        var today = DateTime.Today;
        var name = day == today ? "Today" : day == today.AddDays(-1) ? "Yesterday" : day == today.AddDays(1) ? "Tomorrow" : day.ToString("dddd");

        var label = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Ui.Label(name, 13, bold: true).CenteredVertically(),
                Ui.Label(day.ToString("MMM d"), 12, bold: true, color: Ui.Tertiary).CenteredVertically()
            }
        };
        if (day != today)
            label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(this.sports.GoToToday) });

        return new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.SymbolButton("chevron.left", () => this.sports.StepDay(-1), 11, Ui.Secondary, "Previous day", padding: new Thickness(8, 4)).At(column: 0),
                label.At(column: 1),
                Ui.SymbolButton("chevron.right", () => this.sports.StepDay(1), 11, Ui.Secondary, "Next day", padding: new Thickness(8, 4)).At(column: 2)
            }
        };
    }

    View NoGames(bool loading, bool failed)
    {
        if (loading)
            return Ui.Card(Ui.Label("Loading scores…", 12, color: Ui.Secondary), new Thickness(14, 12));

        var content = new VerticalStackLayout { Spacing = 8 };
        var message = failed ? "Couldn't reach ESPN for the scores." : this.sports.Filter == SportsService.MyTeams ? "None of your teams play on this day." : "No games on this day.";
        content.Children.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { Ui.Symbol(failed ? "info.circle.fill" : "calendar", 14, Ui.Tertiary), Ui.Label(message, 12, color: Ui.Secondary).CenteredVertically() }
        });

        // Your teams' next games, so a quiet day still says when to look.
        var next = this.sports.Filter == SportsService.MyTeams ? this.sports.UpNext() : [];
        if (next.Count == 0)
            return Ui.Card(content, new Thickness(14, 12));

        var page = new VerticalStackLayout { Spacing = 6, Children = { Ui.Card(content, new Thickness(14, 12)), Ui.Caption("Up next") } };
        page.Children.Add(this.Cards(next.Take(4).ToList(), showDate: true));
        return page;
    }

    View LeagueCaption(SportsLeague league)
    {
        var row = new HorizontalStackLayout { Spacing = 6, Margin = new Thickness(4, 4, 0, 0) };
        if (this.sports.LeagueLogo(league.Key) is { } logo)
            row.Children.Add(this.Logo(logo, 13));
        row.Children.Add(Ui.Caption(league.Name).CenteredVertically());
        return row;
    }

    /// <summary>Games two to a row.</summary>
    View Cards(IReadOnlyList<SportsGame> games, bool showDate = false)
    {
        var grid = new Grid
        {
            ColumnSpacing = CardSpacing,
            RowSpacing = CardSpacing,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)]
        };
        for (var i = 0; i < games.Count; i++)
        {
            if (i % 2 == 0)
                grid.RowDefinitions.Add(new RowDefinition(CardHeight));
            grid.Children.Add(this.Card(games[i], showDate).At(row: i / 2, column: i % 2));
        }
        return grid;
    }

    /// <summary>
    /// The away team on the left and the home team on the right, each a logo over its abbreviation, the
    /// scores inside them and the state of play in the middle: a red clock while it's on, "Final" after
    /// (the loser's score dimmed), or the start time and where it's on before - the records in place of scores.
    /// </summary>
    View Card(SportsGame game, bool showDate)
    {
        var grid = new Grid
        {
            WidthRequest = CardWidth - 20,
            ColumnDefinitions =
            [
                new ColumnDefinition(44),
                new ColumnDefinition(38),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(38),
                new ColumnDefinition(44)
            ]
        };

        grid.Children.Add(this.TeamMark(game.Away.Team, 24).At(column: 0));
        grid.Children.Add(this.TeamMark(game.Home.Team, 24).At(column: 4));
        grid.Children.Add(Score(game, game.Away).At(column: 1));
        grid.Children.Add(Score(game, game.Home).At(column: 3));
        grid.Children.Add(Status(game, showDate).At(column: 2));

        var card = Ui.Card(grid, new Thickness(10, 6));
        card.HeightRequest = CardHeight;
        return card.Pressable(() => this.sports.OpenGame(game));
    }

    View TeamMark(SportsTeam team, double size)
        => new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { this.Logo(team.Logo, size), Ui.Label(team.Abbreviation, 10, bold: true, color: Ui.Secondary).Centered() }
        };

    static Label Score(SportsGame game, GameSide side)
    {
        if (game.State is GameState.Scheduled or GameState.Off || side.Score == null)
            return Ui.Label(side.Record ?? "", 10, bold: true, color: Ui.Tertiary).Centered();

        var other = ReferenceEquals(side, game.Away) ? game.Home : game.Away;
        var lost = game.State == GameState.Final && side.ScoreValue < other.ScoreValue;
        return Ui.Label(side.Score, 22, bold: true, color: lost ? Ui.Tertiary : Ui.Text).Centered();
    }

    /// <summary>The middle of a card: the state of play, and a smaller line under it.</summary>
    static View Status(SportsGame game, bool showDate)
    {
        var stack = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        switch (game.State)
        {
            case GameState.Live:
                stack.Children.Add(new HorizontalStackLayout
                {
                    Spacing = 4,
                    HorizontalOptions = LayoutOptions.Center,
                    Children = { LiveDot(), Ui.Label(game.Status, 11, bold: true, color: Ui.Red).CenteredVertically() }
                });
                if (game.Broadcast != null)
                    stack.Children.Add(Ui.Label(game.Broadcast, 9, bold: true, color: Ui.Tertiary).Centered());
                break;

            case GameState.Final:
                stack.Children.Add(Ui.Label(game.Status, 11, bold: true, color: Ui.Secondary).Centered());
                if (showDate)
                    stack.Children.Add(Ui.Label(LocalDate(game), 9, bold: true, color: Ui.Tertiary).Centered());
                break;

            case GameState.Off:
                stack.Children.Add(Ui.Label(game.Status, 11, bold: true, color: Ui.Orange).Centered());
                break;

            default:
                if (showDate)
                    stack.Children.Add(Ui.Label(LocalDate(game), 10, bold: true, color: Ui.Secondary).Centered());
                stack.Children.Add(Ui.Label(game.Status.Length > 0 ? game.Status : LocalTime(game), 12, bold: true).Centered());
                if (game.Broadcast != null)
                    stack.Children.Add(Ui.Label(game.Broadcast, 9, bold: true, color: Ui.Tertiary).Centered());
                break;
        }
        return stack;
    }

    static View LiveDot() => new Border
    {
        StrokeThickness = 0,
        StrokeShape = new Ellipse(),
        BackgroundColor = Ui.Red,
        WidthRequest = 6,
        HeightRequest = 6,
        VerticalOptions = LayoutOptions.Center
    };

    static string LocalTime(SportsGame game) => game.StartUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    static string LocalDate(SportsGame game)
    {
        var date = game.StartUtc.ToLocalTime().Date;
        return date == DateTime.Today ? "Today" : date == DateTime.Today.AddDays(1) ? "Tomorrow" : date == DateTime.Today.AddDays(-1) ? "Yesterday" : date.ToString("ddd, MMM d");
    }

    // ---------------------------------------------------------------------------------------------
    // A game
    // ---------------------------------------------------------------------------------------------

    View GamePage()
    {
        var detail = this.sports.OpenGameDetail;
        var page = new VerticalStackLayout { Spacing = 8 };
        if ((detail?.Game ?? this.sports.OpenGameSummary) is not { } game)
        {
            page.Children.Add(Ui.Card(Ui.Label("Loading the game…", 12, color: Ui.Secondary), new Thickness(14, 12)));
            return page;
        }

        page.Children.Add(this.Scoreline(game));

        var more = new VerticalStackLayout { Spacing = 8 };
        if (game.Away.Periods.Count > 0 && game.State != GameState.Scheduled)
            more.Children.Add(Ui.Card(LineScore(game), new Thickness(12, 8)));

        if (detail == null)
            more.Children.Add(Ui.Card(Ui.Label("Loading the box score…", 12, color: Ui.Secondary), new Thickness(14, 10)));
        else if (detail.Stats.Count == 0 && game.State == GameState.Scheduled)
            more.Children.Add(Ui.Card(Ui.Label("Team stats show here once it starts.", 12, color: Ui.Secondary), new Thickness(14, 10)));

        if (detail?.Stats.Count > 0)
            more.Children.Add(Ui.Card(this.TeamStats(game, detail.Stats), new Thickness(12, 10)));
        if (detail?.Leaders.Count > 0)
            more.Children.Add(Ui.Card(Leaders(game, detail.Leaders), new Thickness(12, 10)));

        // The box score scrolls under the header; until there is one, there's nothing to scroll.
        page.Children.Add(detail?.Stats.Count > 0 ? new ScrollView { Content = more, HeightRequest = 250 } : more);
        return page;
    }

    /// <summary>The game's header: big logos (each opens its team), the score, and the state of play between.</summary>
    View Scoreline(SportsGame game)
    {
        View Side(GameSide side)
        {
            var team = side.Team;
            var stack = new VerticalStackLayout
            {
                Spacing = 3,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    this.Logo(team.Logo, 44),
                    Ui.Label(team.ShortName, 12, bold: true).Centered(),
                    Ui.Label(side.Record ?? "", 10, bold: true, color: Ui.Tertiary).Centered()
                }
            };
            stack.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => this.sports.ShowTeam(team)) });
            return stack;
        }

        Label Big(GameSide side, GameSide other)
        {
            var played = game.State is GameState.Live or GameState.Final && side.Score != null;
            var lost = game.State == GameState.Final && side.ScoreValue < other.ScoreValue;
            return Ui.Label(played ? side.Score! : "", 36, bold: true, color: lost ? Ui.Tertiary : Ui.Text).Centered();
        }

        var middle = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        middle.Children.Add(Status(game, showDate: true));

        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(64),
                new ColumnDefinition(120),
                new ColumnDefinition(64),
                new ColumnDefinition(GridLength.Star)
            ],
            Children =
            {
                Side(game.Away).At(column: 0),
                Big(game.Away, game.Home).At(column: 1),
                middle.At(column: 2),
                Big(game.Home, game.Away).At(column: 3),
                Side(game.Home).At(column: 4)
            }
        };

        var content = new VerticalStackLayout { Spacing = 6, Children = { grid } };
        // The network is already under the clock or the start time; after the game, it goes here.
        var network = game.State is GameState.Live or GameState.Scheduled ? null : game.Broadcast;
        var where = String.Join("  ·  ", new[] { game.Venue, network }.Where(s => !String.IsNullOrWhiteSpace(s)));
        if (where.Length > 0)
            content.Children.Add(Ui.Label(where, 10, bold: true, color: Ui.Tertiary).Centered());
        if (game.State == GameState.Final && game.Recap is { } recap)
        {
            var line = Ui.Label(recap.TrimStart('—', ' '), 11, color: Ui.Secondary);
            line.MaxLines = 2;
            line.LineBreakMode = LineBreakMode.WordWrap;
            line.HorizontalTextAlignment = TextAlignment.Center;
            content.Children.Add(line);
        }

        // Washed in the home side's colour, as Apple Sports tints a game.
        return Ui.Card(content, new Thickness(14, 10), wash: TeamColor(game.Home.Team).WithAlpha(0.45f));
    }

    /// <summary>The score by period, a row a side, with the total last.</summary>
    static View LineScore(SportsGame game)
    {
        var sport = SportsLeague.Find(game.League)?.Sport ?? "";
        var periods = Math.Max(game.Away.Periods.Count, game.Home.Periods.Count);
        var grid = new Grid { RowSpacing = 4, ColumnSpacing = 0 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(56));
        for (var i = 0; i < periods; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(40));
        grid.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];

        for (var i = 0; i < periods; i++)
            grid.Children.Add(Ui.Label(PeriodLabel(sport, i), 10, bold: true, color: Ui.Tertiary).Centered().At(row: 0, column: i + 1));
        grid.Children.Add(Ui.Label("T", 10, bold: true, color: Ui.Tertiary).Centered().At(row: 0, column: periods + 1));

        void Side(GameSide side, int row)
        {
            grid.Children.Add(Ui.Label(side.Team.Abbreviation, 12, bold: true).At(row: row, column: 0));
            for (var i = 0; i < side.Periods.Count; i++)
                grid.Children.Add(Ui.Label(side.Periods[i], 12, color: Ui.Secondary).Centered().At(row: row, column: i + 1));
            grid.Children.Add(Ui.Label(side.Score ?? "", 13, bold: true).Centered().At(row: row, column: periods + 1));
        }
        Side(game.Away, 1);
        Side(game.Home, 2);
        return grid;
    }

    static string PeriodLabel(string sport, int i) => sport switch
    {
        "hockey" => i < 3 ? $"{i + 1}" : i == 3 ? "OT" : "SO",
        "basketball" or "football" => i < 4 ? $"{i + 1}" : i == 4 ? "OT" : $"{i - 3}OT",
        "soccer" => i switch { 0 => "1H", 1 => "2H", 2 => "ET", _ => "PK" },
        _ => $"{i + 1}"
    };

    /// <summary>
    /// Each figure with the away side's on the left and the home side's on the right, over a bar split
    /// between them in their colours - the larger share bright, as Apple Sports compares a box score.
    /// </summary>
    View TeamStats(SportsGame game, IReadOnlyList<StatComparison> stats)
    {
        var awayColor = TeamColor(game.Away.Team);
        var homeColor = TeamColor(game.Home.Team);
        var list = new VerticalStackLayout { Spacing = 8 };
        list.Children.Add(new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                this.Logo(game.Away.Team.Logo, 18).At(column: 0),
                Ui.Caption("Team stats").Centered().At(column: 1),
                this.Logo(game.Home.Team.Logo, 18).At(column: 2)
            }
        });

        foreach (var s in stats)
        {
            var total = s.AwayValue + s.HomeValue;
            var share = total > 0 ? s.AwayValue / total : 0.5;
            var away = Ui.Label(s.Away, 13, bold: true, color: share >= 0.5 ? Ui.Text : Ui.Secondary);
            var home = Ui.Label(s.Home, 13, bold: true, color: share <= 0.5 ? Ui.Text : Ui.Secondary);
            home.HorizontalTextAlignment = TextAlignment.End;

            var bar = Drawing.View(new SplitBarDrawable(share, awayColor, homeColor), Ui.ContentWidth - 24, 4);
            bar.HorizontalOptions = LayoutOptions.Fill;

            list.Children.Add(new VerticalStackLayout
            {
                Spacing = 3,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = [new ColumnDefinition(80), new ColumnDefinition(GridLength.Star), new ColumnDefinition(80)],
                        Children =
                        {
                            away.At(column: 0),
                            Ui.Label(s.Label, 11, bold: true, color: Ui.Secondary).Centered().At(column: 1),
                            home.At(column: 2)
                        }
                    },
                    bar
                }
            });
        }
        return list;
    }

    /// <summary>A category's leader each side: "C. Dach 2" either side of "Goals".</summary>
    static View Leaders(SportsGame game, IReadOnlyList<GameLeader> leaders)
    {
        var list = new VerticalStackLayout { Spacing = 6, Children = { Ui.Caption("Leaders").Centered() } };
        foreach (var l in leaders)
        {
            View Who(string? name, string? value, TextAlignment align)
            {
                var row = new HorizontalStackLayout
                {
                    Spacing = 6,
                    HorizontalOptions = align == TextAlignment.Start ? LayoutOptions.Start : LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Center
                };
                var who = Ui.Label(name ?? "—", 12, bold: true).CenteredVertically();
                var amount = Ui.Label(value ?? "", 12, bold: true, color: Ui.Teal).CenteredVertically();
                if (align == TextAlignment.Start)
                {
                    row.Children.Add(who);
                    row.Children.Add(amount);
                }
                else
                {
                    row.Children.Add(amount);
                    row.Children.Add(who);
                }
                return row;
            }

            list.Children.Add(new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(130), new ColumnDefinition(GridLength.Star)],
                Children =
                {
                    Who(l.Away, l.AwayValue, TextAlignment.Start).At(column: 0),
                    Ui.Label(l.Category, 11, bold: true, color: Ui.Secondary).Centered().At(column: 1),
                    Who(l.Home, l.HomeValue, TextAlignment.End).At(column: 2)
                }
            });
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------------
    // Leagues
    // ---------------------------------------------------------------------------------------------

    /// <summary>Every league as a chip, filled when followed; a click follows or unfollows it.</summary>
    View LeaguesPage()
    {
        // Three across: "Women's College Basketball" fits a third of the panel, not a quarter.
        const int columns = 3;
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var all = SportsLeague.All;
        for (var i = 0; i < all.Count; i++)
        {
            if (i % columns == 0)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var league = all[i];
            var followed = this.sports.IsFollowed(league.Key);
            var chip = this.LeagueChip(league, followed, () => this.sports.SetLeagueFollowed(league.Key, !followed), league.Name);
            chip.HorizontalOptions = LayoutOptions.Fill;
            grid.Children.Add(chip.At(row: i / columns, column: i % columns));
        }

        return Ui.Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children = { Ui.Label("Click a league to show or hide it across the top of the tab.", 12, color: Ui.Secondary), grid }
        }, new Thickness(12, 10));
    }

    // ---------------------------------------------------------------------------------------------
    // Standings
    // ---------------------------------------------------------------------------------------------

    View StandingsPage()
    {
        var page = new VerticalStackLayout { Spacing = 8 };
        if (this.sports.StandingsLeague is not { } key || SportsLeague.Find(key) is not { } league)
        {
            page.Children.Add(Ui.Card(Ui.Label("Follow a league in Settings to see its standings.", 12, color: Ui.Secondary), new Thickness(14, 12)));
            return page;
        }

        if (this.sports.Standings is not { } standings)
        {
            page.Children.Add(Ui.Card(Ui.Label($"Loading the {league.Name} standings…", 12, color: Ui.Secondary), new Thickness(14, 12)));
            return page;
        }

        var group = this.sports.ShownGroup(standings);
        if (group == null)
        {
            page.Children.Add(Ui.Card(Ui.Label("No standings for this league right now.", 12, color: Ui.Secondary), new Thickness(14, 12)));
            return page;
        }

        // The conferences (or divisions) as chips, when there's more than one table.
        if (standings.Groups.Count > 1)
        {
            var chips = new HorizontalStackLayout { Spacing = 6 };
            foreach (var g in standings.Groups)
            {
                var name = g.Name;
                chips.Children.Add(Ui.Chip(g.Short, () => this.sports.ShowStandingsGroup(name), g == group));
            }
            page.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = chips });
        }

        var favourites = this.sports.Favourites.Select(t => t.Key).ToHashSet();
        var table = new VerticalStackLayout { Spacing = 0 };
        for (var i = 0; i < group.Rows.Count; i++)
            table.Children.Add(this.StandingRow(i + 1, group.Rows[i], standings.Columns, favourites.Contains(group.Rows[i].Team.Key)));

        View rows = group.Rows.Count > 9 ? new ScrollView { Content = table, HeightRequest = 9 * RowHeight } : table;
        page.Children.Add(Ui.Card(new VerticalStackLayout
        {
            Spacing = 2,
            Children = { StandingsHeader(group.Name, standings.Columns), rows }
        }, new Thickness(8, 8)));
        return page;
    }

    static Grid StandingsGrid(int columns)
    {
        var grid = new Grid { HeightRequest = RowHeight, ColumnSpacing = 4, Padding = new Thickness(4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(22));
        grid.ColumnDefinitions.Add(new ColumnDefinition(22));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < columns; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(44));
        return grid;
    }

    static View StandingsHeader(string name, IReadOnlyList<string> columns)
    {
        var grid = StandingsGrid(columns.Count);
        grid.Children.Add(Ui.Caption(name).At(column: 0, columnSpan: 3));
        for (var i = 0; i < columns.Count; i++)
        {
            var label = Ui.Caption(columns[i]);
            label.HorizontalTextAlignment = TextAlignment.End;
            grid.Children.Add(label.At(column: i + 3));
        }
        return grid;
    }

    /// <summary>A team's line: your teams on a raised row, in bold. Tapping opens its schedule.</summary>
    View StandingRow(int rank, StandingsRow row, IReadOnlyList<string> columns, bool favourite)
    {
        var grid = StandingsGrid(columns.Count);
        grid.Children.Add(Ui.Label($"{rank}", 11, bold: true, color: Ui.Tertiary).At(column: 0));
        grid.Children.Add(this.Logo(row.Team.Logo, 16).At(column: 1));
        grid.Children.Add(Ui.Label(row.Team.ShortName, 12, bold: favourite).At(column: 2));
        for (var i = 0; i < columns.Count && i < row.Values.Count; i++)
        {
            var value = Ui.Label(row.Values[i], 12, bold: favourite || i == columns.Count - 1, color: favourite ? Ui.Text : Ui.Secondary);
            value.HorizontalTextAlignment = TextAlignment.End;
            grid.Children.Add(value.At(column: i + 3));
        }

        var team = row.Team;
        var surface = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = favourite ? Ui.Raised : Colors.Transparent,
            Content = grid
        };
        return surface.Pressable(() => this.sports.ShowTeam(team));
    }

    // ---------------------------------------------------------------------------------------------
    // A team
    // ---------------------------------------------------------------------------------------------

    View TeamPage()
    {
        var page = new VerticalStackLayout { Spacing = 8 };
        if (this.sports.OpenTeam is not { } team)
            return page;

        var schedule = this.sports.OpenTeamSchedule;
        var info = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Ui.Label(team.Name, 16, bold: true),
                Ui.Label(String.Join("  ·  ", new[] { SportsLeague.Find(team.League)?.Name, schedule?.Record }.Where(s => !String.IsNullOrWhiteSpace(s))), 11, bold: true, color: Ui.Secondary)
            }
        };
        page.Children.Add(Ui.Card(new HorizontalStackLayout { Spacing = 12, Children = { this.Logo(team.Logo, 40), info } }, new Thickness(14, 10), wash: TeamColor(team).WithAlpha(0.5f)));

        if (schedule == null)
        {
            page.Children.Add(Ui.Card(Ui.Label("Loading the schedule…", 12, color: Ui.Secondary), new Thickness(14, 12)));
            return page;
        }

        var games = this.sports.ScheduleWindow(schedule);
        if (games.Count == 0)
        {
            page.Children.Add(Ui.Card(Ui.Label("No games scheduled right now.", 12, color: Ui.Secondary), new Thickness(14, 12)));
            return page;
        }

        var rows = new VerticalStackLayout { Spacing = 0 };
        foreach (var game in games)
            rows.Children.Add(this.ScheduleRow(team, game));
        page.Children.Add(Ui.Card(new VerticalStackLayout { Spacing = 2, Children = { Ui.Caption("Schedule"), rows } }, new Thickness(8, 8)));
        return page;
    }

    /// <summary>"Sat, Nov 29   @ [logo] Bruins          W 4-2" - or the start time, or the clock while it's on.</summary>
    View ScheduleRow(SportsTeam team, SportsGame game)
    {
        var home = game.Home.Team.Id == team.Id;
        var us = home ? game.Home : game.Away;
        var them = home ? game.Away : game.Home;

        View result;
        switch (game.State)
        {
            case GameState.Final:
                var won = us.Winner || us.ScoreValue > them.ScoreValue;
                var tied = !us.Winner && !them.Winner && us.ScoreValue == them.ScoreValue;
                result = new HorizontalStackLayout
                {
                    Spacing = 6,
                    HorizontalOptions = LayoutOptions.End,
                    Children =
                    {
                        Ui.Label(tied ? "D" : won ? "W" : "L", 12, bold: true, color: tied ? Ui.Secondary : won ? Ui.Green : Ui.Red).CenteredVertically(),
                        Ui.Label($"{us.Score}-{them.Score}", 12, bold: true).CenteredVertically()
                    }
                };
                break;
            case GameState.Live:
                result = new HorizontalStackLayout
                {
                    Spacing = 4,
                    HorizontalOptions = LayoutOptions.End,
                    Children = { LiveDot(), Ui.Label($"{us.Score}-{them.Score}  {game.Status}", 12, bold: true, color: Ui.Red).CenteredVertically() }
                };
                break;
            default:
                var when = Ui.Label(game.State == GameState.Off ? game.Status : game.Status.Length > 0 ? game.Status : LocalTime(game), 12, bold: true, color: game.State == GameState.Off ? Ui.Orange : Ui.Secondary);
                when.HorizontalTextAlignment = TextAlignment.End;
                result = when;
                break;
        }

        var grid = new Grid
        {
            HeightRequest = RowHeight,
            ColumnSpacing = 6,
            Padding = new Thickness(4, 0),
            ColumnDefinitions =
            [
                new ColumnDefinition(90),
                new ColumnDefinition(18),
                new ColumnDefinition(20),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(150)
            ],
            Children =
            {
                Ui.Label(game.StartUtc.ToLocalTime().ToString("ddd, MMM d"), 12, bold: true, color: Ui.Secondary).At(column: 0),
                Ui.Label(home ? "vs" : "@", 11, bold: true, color: Ui.Tertiary).At(column: 1),
                this.Logo(them.Team.Logo, 16).At(column: 2),
                Ui.Label(them.Team.Name, 12, bold: true).At(column: 3),
                result.At(column: 4)
            }
        };

        var surface = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 6 }, BackgroundColor = Colors.Transparent, Content = grid };
        return surface.Pressable(() => this.sports.OpenGame(game));
    }

    // ---------------------------------------------------------------------------------------------
    // Logos and colours
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A team or league logo. One already loaded shows straight away; otherwise the image goes in empty and
    /// the logo is set on it in place when it arrives, so a page of logos doesn't rebuild once per logo.
    /// </summary>
    Image Logo(string? url, double size) => SportsLogo.Create(this.sports, url, size);

    /// <summary>
    /// A team's colour, made to show on black: its primary unless that's too dark to see, then its
    /// alternate, else plain grey.
    /// </summary>
    public static Color TeamColor(SportsTeam team)
    {
        static Color? Parse(string? hex) => hex is { Length: 6 } ? Color.FromArgb("#" + hex) : null;
        static double Luminance(Color c) => 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue;

        var primary = Parse(team.Color);
        if (primary != null && Luminance(primary) > 0.12)
            return primary;
        var alternate = Parse(team.AlternateColor);
        if (alternate != null && Luminance(alternate) > 0.12)
            return alternate;
        return Color.FromArgb("#8E8E93");
    }
}

/// <summary>Team and league logos, from <see cref="SportsService"/>'s cache.</summary>
static class SportsLogo
{
    public static Image Create(SportsService sports, string? url, double size)
    {
        var image = new Image
        {
            WidthRequest = size,
            HeightRequest = size,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        if (url == null)
        {
            image.Source = Ui.SymbolSource("sportscourt.fill", size * 0.55, Ui.Tertiary);
            return image;
        }

        var cached = sports.CachedLogo(url);
        if (cached != null)
            image.Source = FromBytes(cached);
        else
            Load(sports, url, image);
        return image;
    }

    static async void Load(SportsService sports, string url, Image image)
    {
        var bytes = await sports.LogoAsync(url);
        if (bytes != null)
            image.Source = FromBytes(bytes);
    }

    public static ImageSource FromBytes(byte[] bytes) => ImageSource.FromStream(() => new MemoryStream(bytes));
}

/// <summary>
/// A bar split at <paramref name="share"/>: the away side's part from the left in its colour, the home
/// side's from the right in its colour, a gap between. The larger part is full strength, the other dimmed.
/// </summary>
sealed class SplitBarDrawable(double share, Color away, Color home) : IDrawable
{
    public void Draw(ICanvas canvas, RectF rect)
    {
        const float gap = 3;
        var radius = rect.Height / 2;
        var split = rect.X + (float)Math.Clamp(share, 0, 1) * rect.Width;

        var left = new RectF(rect.X, rect.Y, Math.Max(split - rect.X - gap / 2, rect.Height), rect.Height);
        var right = new RectF(split + gap / 2, rect.Y, Math.Max(rect.Right - split - gap / 2, rect.Height), rect.Height);

        canvas.FillColor = share >= 0.5 ? away : away.WithAlpha(0.45f);
        canvas.FillRoundedRectangle(left, radius);
        canvas.FillColor = share <= 0.5 ? home : home.WithAlpha(0.45f);
        canvas.FillRoundedRectangle(right, radius);
    }
}
