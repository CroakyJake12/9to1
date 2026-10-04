using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Application.Play;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Views.Pages.Play;

public sealed partial class PlayPage : UserControl
{
    private static readonly IReadOnlyList<PlayExperienceDescriptor> Experiences =
    [
        new("chess", "Chess", "Strategy", "A complete local board with legal turns, checkmate detection and a deterministic Haven opponent.", PlayExperienceKind.Chess),
        new("quick-quiz", "Quick quiz", "Quiz", "Five local questions with progress, scoring and instant continuation.", PlayExperienceKind.Quiz)
    ];

    private readonly PlaySessionService _sessions;
    private readonly GenerativeUiEventRouter _router;
    private PlaySessionSnapshot? _active;
    private PlayGameDefinition? _generatedGame;
    private Guid? _activeMatchId;
    private Guid? _activeContestantId;
    private int? _selectedSquare;
    private string _category = "All";
    private bool _activated;

    public PlayPage(PlaySessionService sessions, GenerativeUiEventRouter router)
    {
        _sessions = sessions;
        _router = router;
        InitializeComponent();

        CreateButton.Click += (_, _) => CreateRequested?.Invoke(this, EventArgs.Empty);
        GenerateGameButton.Click += async (_, _) => await GenerateMathsGameAsync();
        BackButton.Click += (_, _) => ShowHome();
        RestartButton.Click += async (_, _) => await RestartActiveAsync();
        SearchBox.TextChanged += (_, _) => RenderFeatured();
        BuildCategories();
        RenderFeatured();
    }

    public event EventHandler? CreateRequested;

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        PageStatus.Text = "Loading your local Play library…";
        try
        {
            await RefreshLibraryAsync(cancellationToken);
            await RefreshFormalLibraryAsync(cancellationToken);
            _activated = true;
            PageStatus.Text = "Play runs deterministic rules locally. Model reasoning is only needed for experiences that explicitly ask for it.";
        }
        catch (OperationCanceledException)
        {
            PageStatus.Text = "Play loading was cancelled.";
        }
        catch (Exception exception)
        {
            PageStatus.Text = "Play could not load your saved sessions: " + exception.Message;
        }
    }

    private async Task GenerateMathsGameAsync()
    {
        GenerateGameButton.IsEnabled = false;
        GameBuildStatus.Text = "Building and validating the game definition…";
        try
        {
            var result = await _sessions.Matches.GenerateGameAsync(GamePromptBox.Text ?? string.Empty, CancellationToken.None);
            if (!result.Succeeded)
            {
                _generatedGame = null;
                GeneratedGamesPanel.Children.Clear();
                GameBuildStatus.Text = result.Error?.Message ?? "The game could not be built.";
                return;
            }
            _generatedGame = result.Value;
            GameBuildStatus.Text = $"{_generatedGame!.Questions.Count} rounds · deterministic scoring · revision {_generatedGame.Revision}";
            RenderGeneratedGame(_generatedGame);
            await RefreshFormalLibraryAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            GameBuildStatus.Text = "Game creation was cancelled.";
        }
        catch (Exception exception)
        {
            GameBuildStatus.Text = "Play could not create the game: " + exception.Message;
        }
        finally
        {
            GenerateGameButton.IsEnabled = true;
        }
    }

    private void RenderGeneratedGame(PlayGameDefinition game, bool clear = true)
    {
        if (clear) GeneratedGamesPanel.Children.Clear();
        var start = new HavenPrimaryButton { Content = "Start solo", HorizontalAlignment = HorizontalAlignment.Left };
        start.Click += async (_, _) => await StartGeneratedSoloAsync(game);
        var note = game.MaximumContestants > 1
            ? "This definition supports session-scoped Agent opponents. This page starts solo until the shared Agent turn runtime is connected."
            : "Play this deterministic game locally.";
        GeneratedGamesPanel.Children.Add(new HavenCard
        {
            Width = 350,
            MinHeight = 150,
            Margin = new Thickness(0, 0, 12, 8),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = game.Title, FontSize = 18, FontWeight = FontWeight.ExtraBold },
                    new TextBlock { Text = $"{game.Category} · {game.Questions.Count} rounds · up to {game.MaximumContestants} contestants", Classes = { "muted" } },
                    new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap },
                    start
                }
            }
        });
    }

    private async Task StartGeneratedSoloAsync(PlayGameDefinition game)
    {
        var result = await _sessions.Matches.StartMatchAsync(game.GameDefinitionId, new PlayMatchConfiguration(SessionAgentCount: 0), CancellationToken.None);
        if (!result.Succeeded)
        {
            GameBuildStatus.Text = result.Error?.Message ?? "The match could not start.";
            return;
        }
        var match = result.Value!;
        var human = match.Contestants.FirstOrDefault(item => item.Kind == PlayContestantKind.Human);
        if (human is null)
        {
            GameBuildStatus.Text = "A player contestant could not be created.";
            return;
        }
        _active = null;
        _activeMatchId = match.MatchId;
        _activeContestantId = human.ContestantId;
        HomeSection.IsVisible = false;
        ExperienceSection.IsVisible = true;
        RestartButton.IsVisible = false;
        ExperienceTitle.Text = match.PinnedGame.Title;
        await RenderFormalMatchAsync();
        await RefreshFormalLibraryAsync(CancellationToken.None);
    }

    private async Task RefreshFormalLibraryAsync(CancellationToken cancellationToken)
    {
        var games = await _sessions.Matches.ListGamesAsync(cancellationToken);
        if (!games.Succeeded)
        {
            GameBuildStatus.Text = games.Error?.Message ?? "Saved games could not be loaded.";
            return;
        }
        if (_generatedGame is null) GeneratedGamesPanel.Children.Clear();
        foreach (var game in games.Value!.Take(8))
        {
            if (_generatedGame?.GameDefinitionId == game.GameDefinitionId) continue;
            RenderGeneratedGame(game, clear: false);
        }

        var matches = await _sessions.Matches.ListMatchesAsync(0, 8, cancellationToken);
        FormalMatchesPanel.Children.Clear();
        var active = matches.Succeeded ? matches.Value!.Where(item => item.Status is PlayMatchStatus.Active or PlayMatchStatus.Paused).ToArray() : [];
        FormalMatchesSection.IsVisible = active.Length > 0;
        foreach (var match in active)
        {
            var contestant = match.Contestants.FirstOrDefault(item => item.Kind == PlayContestantKind.Human);
            var open = new HavenTertiaryButton { Content = "Resume", IsEnabled = contestant is not null };
            open.Click += async (_, _) => await OpenFormalMatchAsync(match.MatchId, contestant!.ContestantId);
            FormalMatchesPanel.Children.Add(new HavenCard
            {
                Width = 250,
                Margin = new Thickness(0, 0, 10, 8),
                Padding = new Thickness(12),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children = { new TextBlock { Text = match.PinnedGame.Title, FontWeight = FontWeight.Bold }, new TextBlock { Text = $"Round {match.MatchState.RoundIndex + 1} · {match.Status}", Classes = { "muted" } }, open }
                }
            });
        }
    }

    private async Task OpenFormalMatchAsync(Guid matchId, Guid contestantId)
    {
        _active = null;
        _activeMatchId = matchId;
        _activeContestantId = contestantId;
        HomeSection.IsVisible = false;
        ExperienceSection.IsVisible = true;
        RestartButton.IsVisible = false;
        var match = await _sessions.Matches.GetMatchAsync(matchId, CancellationToken.None);
        ExperienceTitle.Text = match.Succeeded ? match.Value!.PinnedGame.Title : "Play match";
        await RenderFormalMatchAsync();
    }

    private async Task RenderFormalMatchAsync()
    {
        if (_activeMatchId is not Guid matchId || _activeContestantId is not Guid contestantId) return;
        var viewResult = await _sessions.Matches.GetContestantViewAsync(matchId, contestantId, CancellationToken.None);
        if (!viewResult.Succeeded)
        {
            ExperienceStatus.Text = viewResult.Error?.Message ?? "The match could not be opened.";
            ExperienceHost.Content = new TextBlock { Text = ExperienceStatus.Text, TextWrapping = TextWrapping.Wrap };
            return;
        }
        var view = viewResult.Value!;
        var leaderboard = await _sessions.Matches.GetLeaderboardAsync(matchId, CancellationToken.None);
        var matchResult = await _sessions.Matches.GetMatchAsync(matchId, CancellationToken.None);
        if (!matchResult.Succeeded) return;
        var match = matchResult.Value!;
        ExperienceStatus.Text = match.Status == PlayMatchStatus.Completed
            ? "Match complete · " + string.Join(" · ", match.Contestants.OrderByDescending(item => item.Score).Select(item => $"{item.DisplayName}: {item.Score}"))
            : $"Round {view.QuestionIndex + 1} of {match.PinnedGame.Questions.Count} · {view.Phase} · {view.PublicScores.GetValueOrDefault(contestantId)} points";
        var content = new StackPanel { MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 14 };
        if (view.Prompt is null)
        {
            content.Children.Add(new TextBlock { Text = match.Status == PlayMatchStatus.Completed ? "All rounds are resolved. Your score has been saved in the match history." : "This round is waiting for its contestants. Submissions remain sealed until resolution.", FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            content.Children.Add(new TextBlock { Text = view.Prompt, FontSize = 21, FontWeight = FontWeight.ExtraBold, TextWrapping = TextWrapping.Wrap });
            if (view.HasSubmitted) content.Children.Add(new TextBlock { Text = "Answer locked. Waiting for the remaining contestants before reveal.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            else foreach (var (optionText, index) in view.Options.Select((text, index) => (text, index)))
            {
                var selectedOption = index;
                var answer = new HavenSecondaryButton { Content = optionText, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetName(answer, $"Answer {index + 1}: {optionText}");
                answer.Click += async (_, _) => await SubmitFormalAnswerAsync(selectedOption);
                content.Children.Add(answer);
            }
            if (view.Deadline is DateTimeOffset deadline) content.Children.Add(new TextBlock { Text = $"Closes {deadline.LocalDateTime:g}", Classes = { "muted" } });
        }
        if (leaderboard.Succeeded)
            content.Children.Add(new TextBlock { Text = "Leaderboard · " + string.Join(" · ", match.Contestants.OrderByDescending(item => item.Score).Select(item => $"{item.DisplayName}: {item.Score}")), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        ExperienceHost.Content = content;
    }

    private async Task SubmitFormalAnswerAsync(int selectedOption)
    {
        if (_activeMatchId is not Guid matchId || _activeContestantId is not Guid contestantId) return;
        var answer = await _sessions.Matches.SubmitAnswerAsync(matchId, contestantId, selectedOption, CancellationToken.None);
        if (!answer.Succeeded)
        {
            PageStatus.Text = answer.Error?.Message ?? "The answer could not be submitted.";
            return;
        }
        if (answer.Value!.MatchState.Phase == PlayRoundPhase.Resolving)
        {
            var resolved = await _sessions.Matches.ResolveRoundAsync(matchId, CancellationToken.None);
            if (!resolved.Succeeded) PageStatus.Text = resolved.Error?.Message ?? "The round could not be resolved.";
        }
        await RenderFormalMatchAsync();
        await RefreshFormalLibraryAsync(CancellationToken.None);
    }

    private void BuildCategories()
    {
        CategoryPanel.Children.Clear();
        foreach (var category in new[] { "All", "Strategy", "Quiz", "AI-ready" })
        {
            var button = new HavenChipButton { Content = category, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) =>
            {
                _category = category;
                RenderFeatured();
            };
            CategoryPanel.Children.Add(button);
        }
    }

    private void RenderFeatured()
    {
        FeaturedPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = Experiences.Where(item =>
            (_category is "All" or "AI-ready" || item.Category.Equals(_category, StringComparison.OrdinalIgnoreCase))
            && (query.Length == 0 || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();

        foreach (var item in filtered) FeaturedPanel.Children.Add(BuildExperienceCard(item));
        if (filtered.Length == 0)
            FeaturedPanel.Children.Add(new TextBlock { Text = "No local Play experiences match that search.", Classes = { "muted" }, Margin = new Thickness(4, 10) });
    }

    private Control BuildExperienceCard(PlayExperienceDescriptor item)
    {
        var launch = new HavenPrimaryButton { Content = "Play", HorizontalAlignment = HorizontalAlignment.Left };
        launch.Click += async (_, _) =>
        {
            PageStatus.Text = "Starting " + item.Title + "…";
            var session = item.Kind == PlayExperienceKind.Chess
                ? await _sessions.StartChessAsync(CancellationToken.None)
                : await _sessions.StartQuizAsync(CancellationToken.None);
            await OpenSessionAsync(session);
        };
        return new HavenCard
        {
            Width = 310,
            MinHeight = 166,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    new TextBlock { Text = item.Title, FontSize = 18, FontWeight = FontWeight.ExtraBold },
                    new TextBlock { Text = item.Category + " · Local-first", Classes = { "muted" } },
                    new TextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap },
                    launch
                }
            }
        };
    }

    private async Task RefreshLibraryAsync(CancellationToken cancellationToken)
    {
        var recent = await _sessions.GetRecentAsync(cancellationToken);
        RecentPanel.Children.Clear();
        ContinuePanel.Children.Clear();

        var continuable = recent.FirstOrDefault(item => item.Status == PlaySessionStatus.Active);
        ContinueSection.IsVisible = continuable is not null;
        if (continuable is not null) ContinuePanel.Children.Add(BuildSessionCard(continuable, "Continue"));

        foreach (var item in recent.Take(8)) RecentPanel.Children.Add(BuildSessionCard(item, item.Status == PlaySessionStatus.Active ? "Resume" : "View"));
        RecentEmptyText.IsVisible = recent.Count == 0;
    }

    private Control BuildSessionCard(PlaySessionSummary item, string actionText)
    {
        var open = new HavenTertiaryButton { Content = actionText, HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += async (_, _) =>
        {
            var session = await _sessions.GetAsync(item.Id, CancellationToken.None);
            if (session is null)
            {
                PageStatus.Text = "That saved Play session is no longer available.";
                await RefreshLibraryAsync(CancellationToken.None);
                return;
            }
            await OpenSessionAsync(session);
        };
        return new HavenCard
        {
            Width = 260,
            MinHeight = 128,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = item.Title, FontWeight = FontWeight.ExtraBold },
                    new TextBlock { Text = item.Subtitle, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                    open
                }
            }
        };
    }

    private async Task OpenSessionAsync(PlaySessionSnapshot session)
    {
        _active = session;
        _activeMatchId = null;
        _activeContestantId = null;
        _selectedSquare = null;
        HomeSection.IsVisible = false;
        ExperienceSection.IsVisible = true;
        ExperienceTitle.Text = session.Title;
        RenderActive();
        await RefreshLibraryAsync(CancellationToken.None);
    }

    private void ShowHome()
    {
        _active = null;
        _activeMatchId = null;
        _activeContestantId = null;
        _selectedSquare = null;
        RestartButton.IsVisible = true;
        ExperienceSection.IsVisible = false;
        HomeSection.IsVisible = true;
        ExperienceHost.Content = null;
        if (_activated) _ = RefreshLibraryAsync(CancellationToken.None);
    }

    private void RenderActive()
    {
        if (_active is null) return;
        if (_active.Kind == PlayExperienceKind.Chess) RenderChess();
        else RenderQuiz();
    }

    private void RenderChess()
    {
        if (_active is null) return;
        var state = _sessions.ReadChess(_active);
        ExperienceStatus.Text = state.Result == "playing"
            ? (state.WhiteToMove ? "White to move · You are White" : "Black to move · Haven local opponent is thinking")
            : state.Result;

        var board = new Grid { Width = 520, Height = 520, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 8; i++)
        {
            board.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            board.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        var legal = _selectedSquare is int selected ? _sessions.GetLegalChessMoves(state, selected).ToHashSet() : [];
        for (var square = 0; square < 64; square++)
        {
            var captured = square;
            var piece = state.Board[square];
            HavenButtonBase cell = ((square / 8 + square % 8) % 2 == 0) ? new HavenSecondaryButton() : new HavenTertiaryButton();
            cell.Content = PieceGlyph(piece);
            cell.FontSize = 28;
            cell.Padding = new Thickness(0);
            cell.MinWidth = 44;
            cell.MinHeight = 44;
            cell.IsEnabled = state.Result == "playing" && state.WhiteToMove;
            AutomationProperties.SetName(cell, SquareName(square) + (piece == '.' ? string.Empty : " " + PieceName(piece)));
            if (_selectedSquare == square) cell.Classes.Add("selected");
            if (legal.Contains(square)) cell.Classes.Add("accent");
            cell.Click += async (_, _) => await OnChessSquareAsync(captured);
            Grid.SetRow(cell, square / 8);
            Grid.SetColumn(cell, square % 8);
            board.Children.Add(cell);
        }
        ExperienceHost.Content = board;
    }

    private async Task OnChessSquareAsync(int square)
    {
        if (_active is null) return;
        var state = _sessions.ReadChess(_active);
        if (!state.WhiteToMove || state.Result != "playing") return;

        if (_selectedSquare is null)
        {
            var piece = state.Board[square];
            if (piece != '.' && char.IsUpper(piece) && _sessions.GetLegalChessMoves(state, square).Count > 0)
            {
                _selectedSquare = square;
                RenderChess();
            }
            return;
        }

        var from = _selectedSquare.Value;
        if (!_sessions.GetLegalChessMoves(state, from).Contains(square))
        {
            _selectedSquare = null;
            RenderChess();
            return;
        }

        _selectedSquare = null;
        if (!await RouteAsync("play.chess.move", "chess-board", new { sessionId = _active.Id, from, to = square }, GenUiEventSource.User))
            return;
        _active = await _sessions.GetAsync(_active.Id, CancellationToken.None);
        RenderActive();
        await RunLocalOpponentAsync();
    }

    private async Task RunLocalOpponentAsync()
    {
        if (_active is null || _active.Kind != PlayExperienceKind.Chess) return;
        var state = _sessions.ReadChess(_active);
        if (state.WhiteToMove || state.Result != "playing") return;
        var move = _sessions.ChooseLocalChessOpponentMove(state);
        if (move is null) return;
        ExperienceStatus.Text = "Haven local opponent chose a legal move…";
        if (await RouteAsync("play.chess.move", "chess-board", new { sessionId = _active.Id, from = move.Value.From, to = move.Value.To }, GenUiEventSource.Agent))
        {
            _active = await _sessions.GetAsync(_active.Id, CancellationToken.None);
            RenderActive();
            await RefreshLibraryAsync(CancellationToken.None);
        }
    }

    private void RenderQuiz()
    {
        if (_active is null) return;
        var state = _sessions.ReadQuiz(_active);
        var feedback = FormatQuizFeedback(state);
        ExperienceStatus.Text = state.Completed
            ? $"Finished · {state.Score}/{state.Questions.Count}"
            : $"Question {state.QuestionIndex + 1} of {state.Questions.Count} · {state.Score} correct";

        if (state.Completed)
        {
            var completion = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10
            };
            completion.Children.Add(new TextBlock { Text = "Quiz complete", FontSize = 24, FontWeight = FontWeight.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center });
            completion.Children.Add(new TextBlock { Text = $"Score: {state.Score}/{state.Questions.Count}", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center });
            if (feedback is not null) completion.Children.Add(BuildQuizFeedback(feedback));
            completion.Children.Add(new TextBlock { Text = "Restart to try the same local question set again.", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center });
            ExperienceHost.Content = completion;
            return;
        }

        if (state.Questions.Count == 0 || state.QuestionIndex < 0 || state.QuestionIndex >= state.Questions.Count)
        {
            ExperienceHost.Content = new TextBlock { Text = "This saved quiz has no playable question data.", TextWrapping = TextWrapping.Wrap };
            return;
        }

        var question = state.Questions[state.QuestionIndex];
        var options = new StackPanel { Spacing = 8 };
        for (var i = 0; i < question.Options.Count; i++)
        {
            var answer = i;
            var button = new HavenSecondaryButton { Content = question.Options[i], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(button, $"Answer {i + 1}: {question.Options[i]}");
            button.Click += async (_, _) =>
            {
                if (_active is null) return;
                if (!await RouteAsync("play.quiz.answer", "quiz-options", new { sessionId = _active.Id, answerIndex = answer }, GenUiEventSource.User)) return;
                _active = await _sessions.GetAsync(_active.Id, CancellationToken.None);
                RenderActive();
                await RefreshLibraryAsync(CancellationToken.None);
            };
            options.Children.Add(button);
        }

        var quizContent = new StackPanel
        {
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 14
        };
        if (feedback is not null) quizContent.Children.Add(BuildQuizFeedback(feedback));
        quizContent.Children.Add(new TextBlock { Text = question.Prompt, FontSize = 21, FontWeight = FontWeight.ExtraBold, TextWrapping = TextWrapping.Wrap });
        quizContent.Children.Add(options);
        quizContent.Children.Add(new TextBlock { Text = "Questions and scoring run locally. A future model-backed host can adapt this activity through the same semantic Play events.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        ExperienceHost.Content = quizContent;
    }

    internal static string? FormatQuizFeedback(QuizGameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.LastAnswerCorrect is not bool correct || state.Questions.Count == 0)
            return null;

        var answeredIndex = state.Completed ? state.QuestionIndex : state.QuestionIndex - 1;
        if (answeredIndex < 0 || answeredIndex >= state.Questions.Count)
            return null;

        var explanation = state.Questions[answeredIndex].Explanation?.Trim() ?? string.Empty;
        var result = correct ? "Correct." : "Not quite.";
        return string.IsNullOrWhiteSpace(explanation) ? result : result + " " + explanation;
    }

    private static TextBlock BuildQuizFeedback(string feedback)
    {
        var text = new TextBlock
        {
            Name = "QuizFeedback",
            Text = feedback,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        AutomationProperties.SetName(text, "Last answer result. " + feedback);
        return text;
    }

    private async Task RestartActiveAsync()
    {
        if (_active is null) return;
        if (!await RouteAsync("play.session.restart", "experience", new { sessionId = _active.Id }, GenUiEventSource.User)) return;
        _active = await _sessions.GetAsync(_active.Id, CancellationToken.None);
        _selectedSquare = null;
        RenderActive();
        await RefreshLibraryAsync(CancellationToken.None);
    }

    private async Task<bool> RouteAsync(string actionId, string componentId, object payload, GenUiEventSource source)
    {
        if (_active is null) return false;
        var origin = new GenUiOrigin(Guid.Empty, PlaySessionService.AppTargetKey, null, _active.Id);
        var semanticEvent = new GenUiEvent(Guid.NewGuid(), GenUiEventType.ActionInvoked, DateTimeOffset.UtcNow, origin,
            componentId, actionId, _active.Id.ToString("N"), null, null, JsonSerializer.SerializeToElement(payload),
            source, source == GenUiEventSource.Agent ? "Local deterministic Play opponent action." : "Play user interaction.");
        var result = await _router.RouteAsync(semanticEvent,
            new GenUiActionBinding(actionId, GenUiRouteKind.App, PlaySessionService.AppTargetKey, CapabilityRiskClass.Low, false),
            CancellationToken.None);
        if (result.Status == GenUiActionStatus.Completed) return true;
        PageStatus.Text = result.Summary;
        return false;
    }

    private static string PieceGlyph(char piece) => piece switch
    {
        'K' => "♔", 'Q' => "♕", 'R' => "♖", 'B' => "♗", 'N' => "♘", 'P' => "♙",
        'k' => "♚", 'q' => "♛", 'r' => "♜", 'b' => "♝", 'n' => "♞", 'p' => "♟",
        _ => string.Empty
    };

    private static string PieceName(char piece) => char.ToLowerInvariant(piece) switch
    {
        'k' => "king", 'q' => "queen", 'r' => "rook", 'b' => "bishop", 'n' => "knight", 'p' => "pawn", _ => "empty"
    };

    private static string SquareName(int square) => $"{(char)('a' + square % 8)}{8 - square / 8}";

    private sealed record PlayExperienceDescriptor(string Key, string Title, string Category, string Description, PlayExperienceKind Kind);
}
