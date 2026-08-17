using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using HeartsWpf.Models;

namespace HeartsWpf;

public partial class MainWindow : Window
{
    private readonly GameEngine _engine = new();
    private Action? _statusButtonAction;

    public MainWindow()
    {
        InitializeComponent();
        _engine.Changed += () => Dispatcher.Invoke(RenderAll);
        _engine.HandCompleted += shooter => Dispatcher.Invoke(() => ShowRoundModal(shooter));
        _engine.GameCompleted += () => Dispatcher.Invoke(ShowGameOverModal);
        ShowScreen("intro");
    }

    // Defensive: CenterScreen can push an oversized window off-screen on small
    // displays, leaving the title bar unreachable. Clamp size/position to the
    // actual work area before the window is ever shown.
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        if (Width > wa.Width) Width = wa.Width - 20;
        if (Height > wa.Height) Height = wa.Height - 20;
        Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
        Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
    }

    // ---------------- Screen / navigation ----------------

    private void ShowScreen(string name)
    {
        IntroScreen.Visibility = name == "intro" ? Visibility.Visible : Visibility.Collapsed;
        InstructionsScreen.Visibility = name == "instructions" ? Visibility.Visible : Visibility.Collapsed;
        GameScreen.Visibility = name == "game" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen("game");
        _engine.NewGame();
    }

    private void BtnHowToPlay_Click(object sender, RoutedEventArgs e) => ShowScreen("instructions");

    private void BtnInstructionsBack_Click(object sender, RoutedEventArgs e) => ShowScreen("intro");

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        ExitConfirmModal.Visibility = Visibility.Visible;
    }

    private void ExitConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        ExitConfirmModal.Visibility = Visibility.Collapsed;
    }

    private void ExitConfirmYes_Click(object sender, RoutedEventArgs e)
    {
        _engine.CancelGame();
        RoundModal.Visibility = Visibility.Collapsed;
        GameOverModal.Visibility = Visibility.Collapsed;
        ScoreboardModal.Visibility = Visibility.Collapsed;
        ExitConfirmModal.Visibility = Visibility.Collapsed;
        ShowScreen("intro");
    }

    private void BtnScoreboard_Click(object sender, RoutedEventArgs e)
    {
        var rows = _engine.Players.Select(p => new[] { p.Name, p.TotalScore.ToString() }).ToList();
        ScoreboardTable.Children.Clear();
        ScoreboardTable.RowDefinitions.Clear();
        ScoreboardTable.ColumnDefinitions.Clear();
        BuildTable(ScoreboardTable, new[] { "Player", "Total Score" }, rows, winnerRow: -1);
        ScoreboardModal.Visibility = Visibility.Visible;
    }

    private void ScoreboardClose_Click(object sender, RoutedEventArgs e)
    {
        ScoreboardModal.Visibility = Visibility.Collapsed;
    }

    private void RoundModalContinue_Click(object sender, RoutedEventArgs e)
    {
        RoundModal.Visibility = Visibility.Collapsed;
        _engine.ContinueAfterRound();
    }

    private void GameOverRestart_Click(object sender, RoutedEventArgs e)
    {
        GameOverModal.Visibility = Visibility.Collapsed;
        _engine.NewGame();
    }

    private void StatusActionBtn_Click(object sender, RoutedEventArgs e) => _statusButtonAction?.Invoke();

    // ---------------- Modals ----------------

    private void ShowRoundModal(int shooter)
    {
        RoundModalTitle.Text = shooter != -1
            ? $"{_engine.Players[shooter].Name} shot the moon!"
            : "Hand Results";

        var rows = _engine.Players.Select(p => new[] { p.Name, p.RoundScore.ToString(), p.TotalScore.ToString() }).ToList();
        RoundModalTable.Children.Clear();
        RoundModalTable.RowDefinitions.Clear();
        RoundModalTable.ColumnDefinitions.Clear();
        BuildTable(RoundModalTable, new[] { "Player", "Hand pts", "Total" }, rows, winnerRow: -1);
        RoundModal.Visibility = Visibility.Visible;
    }

    private void ShowGameOverModal()
    {
        int minScore = _engine.Players.Min(p => p.TotalScore);
        var winners = _engine.Players.Where(p => p.TotalScore == minScore).Select(p => p.Name).ToList();
        GameOverMsg.Text = $"{string.Join(" & ", winners)} win{(winners.Count == 1 ? "s" : "")} with the lowest score!";

        var sorted = _engine.Players.OrderBy(p => p.TotalScore).ToList();
        var rows = sorted.Select(p => new[] { p.Name, p.TotalScore.ToString() }).ToList();
        int winnerRow = sorted.FindIndex(p => p.TotalScore == minScore);
        GameOverTable.Children.Clear();
        GameOverTable.RowDefinitions.Clear();
        GameOverTable.ColumnDefinitions.Clear();
        BuildTable(GameOverTable, new[] { "Player", "Total" }, rows, winnerRow);
        GameOverModal.Visibility = Visibility.Visible;
    }

    private static void BuildTable(Grid grid, string[] headers, List<string[]> rows, int winnerRow)
    {
        for (int c = 0; c < headers.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddRow(string[] cells, bool isHeader, bool isWinner)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cells.Length; c++)
            {
                var tb = new TextBlock
                {
                    Text = cells[c],
                    Foreground = isWinner ? new SolidColorBrush(Color.FromRgb(0x88, 0xff, 0x88))
                                          : new SolidColorBrush(Colors.Gainsboro),
                    FontWeight = isHeader || isWinner ? FontWeights.Bold : FontWeights.Normal,
                    Padding = new Thickness(10, 6, 10, 6),
                    HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                };
                Grid.SetRow(tb, r);
                Grid.SetColumn(tb, c);
                grid.Children.Add(tb);
            }
        }

        AddRow(headers, isHeader: true, isWinner: false);
        for (int i = 0; i < rows.Count; i++)
            AddRow(rows[i], isHeader: false, isWinner: i == winnerRow);
    }

    // ---------------- Rendering ----------------

    private void RenderAll()
    {
        RenderTopScores();
        RenderSeats();
        RenderTrick();
        RenderStatus();
        RenderHand();
    }

    private void RenderTopScores()
    {
        TopScoresPanel.Children.Clear();
        foreach (var p in _engine.Players)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0) };
            sp.Children.Add(new TextBlock
            {
                Text = p.Name + " ",
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0xaa, 0xdd)),
                FontSize = 13
            });
            sp.Children.Add(new TextBlock
            {
                Text = p.CurrentRoundPoints.ToString(),
                Foreground = Brushes.White,
                FontSize = 13
            });
            TopScoresPanel.Children.Add(sp);
        }
    }

    private void RenderSeats()
    {
        // seat index: 1=West, 2=North, 3=East
        WestLabel.Text = $"West ({_engine.Players[1].Hand.Count})";
        NorthLabel.Text = $"North ({_engine.Players[2].Hand.Count})";
        EastLabel.Text = $"East ({_engine.Players[3].Hand.Count})";

        bool active(int idx) => _engine.Phase == GamePhase.Playing && _engine.Turn == idx;
        SetActiveBorder(WestLabelBorder, active(1));
        SetActiveBorder(NorthLabelBorder, active(2));
        SetActiveBorder(EastLabelBorder, active(3));

        RenderBacksHorizontal(NorthBacks, _engine.Players[2].Hand.Count);
        RenderBacksVertical(WestBacks, _engine.Players[1].Hand.Count);
        RenderBacksVertical(EastBacks, _engine.Players[3].Hand.Count);
    }

    private static void SetActiveBorder(Border border, bool active)
    {
        border.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(0xd4, 0xaf, 0x37)) : Brushes.Transparent;
        border.BorderThickness = new Thickness(active ? 2 : 0);
    }

    private static void RenderBacksHorizontal(Panel panel, int count)
    {
        panel.Children.Clear();
        for (int i = 0; i < Math.Min(count, 13); i++)
            panel.Children.Add(MakeCardBack(26, 38, new Thickness(i == 0 ? 0 : 2, 0, 0, 0)));
    }

    private static void RenderBacksVertical(Panel panel, int count)
    {
        panel.Children.Clear();
        for (int i = 0; i < Math.Min(count, 13); i++)
            panel.Children.Add(MakeCardBack(38, 26, new Thickness(0, i == 0 ? 0 : -14, 0, 0)));
    }

    private static Border MakeCardBack(double w, double h, Thickness margin)
    {
        return new Border
        {
            Width = w,
            Height = h,
            Margin = margin,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(0x7a, 0x1f, 0x1f)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x00, 0x00)),
            BorderThickness = new Thickness(1),
        };
    }

    private void RenderTrick()
    {
        TrickNorth.Content = null;
        TrickWest.Content = null;
        TrickEast.Content = null;
        TrickSouth.Content = null;

        foreach (var (player, card) in _engine.CurrentTrick)
        {
            var visual = MakeCardVisual(card);
            switch (player)
            {
                case 0: TrickSouth.Content = visual; break;
                case 1: TrickWest.Content = visual; break;
                case 2: TrickNorth.Content = visual; break;
                case 3: TrickEast.Content = visual; break;
            }
        }
    }

    private void RenderStatus()
    {
        StatusTextBlock.Text = _engine.StatusText;

        if (_engine.Phase == GamePhase.Passing && !_engine.HumanPassConfirmed)
        {
            StatusActionBtn.Content = "Pass Cards";
            StatusActionBtn.IsEnabled = _engine.SelectedPass.Count == 3;
            StatusActionBtn.Visibility = Visibility.Visible;
            _statusButtonAction = _engine.ConfirmHumanPass;
        }
        else
        {
            StatusActionBtn.Visibility = Visibility.Collapsed;
            _statusButtonAction = null;
        }
    }

    private void RenderHand()
    {
        SouthHandPanel.Children.Clear();
        var hand = _engine.Players[0].Hand;

        bool canPlay = _engine.Phase == GamePhase.Playing && _engine.Turn == 0 && _engine.CurrentTrick.Count < 4;
        var valid = canPlay ? _engine.ValidPlays(0) : new List<Card>();
        bool canPass = _engine.Phase == GamePhase.Passing && !_engine.HumanPassConfirmed;

        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            bool isSelected = canPass && _engine.SelectedPass.Contains(card);
            bool isPlayable = canPass || (canPlay && valid.Contains(card));

            var visual = MakeCardVisual(card, selected: isSelected, dim: !isPlayable);
            visual.Margin = new Thickness(i == 0 ? 0 : -26, isSelected ? -18 : 0, 0, 0);

            // Base z-index only breaks the tie for a selected card (raised, needs
            // to clear its neighbors); otherwise leave every card at 0 so later
            // cards in the fan naturally stack over earlier ones, same as the DOM order.
            int baseZ = isSelected ? 6 : 0;
            Panel.SetZIndex(visual, baseZ);

            if (isPlayable)
            {
                visual.Cursor = Cursors.Hand;
                var capturedCard = card;

                visual.MouseLeftButtonUp += (s, e) =>
                {
                    if (canPass) _engine.ToggleHumanPassSelection(capturedCard);
                    else _engine.PlayHumanCard(capturedCard);
                };
                visual.MouseEnter += (s, e) =>
                {
                    Panel.SetZIndex(visual, 20); // pop fully above neighboring cards while hovered
                    if (!isSelected) visual.Margin = new Thickness(visual.Margin.Left, -14, 0, 0);
                };
                visual.MouseLeave += (s, e) =>
                {
                    Panel.SetZIndex(visual, baseZ);
                    if (!isSelected) visual.Margin = new Thickness(visual.Margin.Left, 0, 0, 0);
                };
            }

            SouthHandPanel.Children.Add(visual);
        }
    }

    /// <summary>
    /// Darkens a color like CSS `filter: brightness()` — scales channels down
    /// without introducing transparency, so a dimmed card stays fully opaque
    /// and doesn't let an overlapping neighbor bleed through its edge.
    /// </summary>
    private static Color Darken(Color c, double factor) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private const double DimFactor = 0.55;

    private static Border MakeCardVisual(Card card, bool selected = false, bool dim = false)
    {
        var rankColor = card.Suit.IsRed()
            ? Color.FromRgb(0xc0, 0x27, 0x2d)
            : Color.FromRgb(0x1a, 0x1a, 0x1a);
        if (dim) rankColor = Darken(rankColor, DimFactor);
        var brush = new SolidColorBrush(rankColor);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        var topRank = new TextBlock
        {
            Text = card.RankLabel + card.Suit.Symbol(),
            Foreground = brush,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5, 3, 0, 0)
        };
        Grid.SetRow(topRank, 0);

        var bigSuit = new TextBlock
        {
            Text = card.Suit.Symbol(),
            Foreground = brush,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(bigSuit, 1);

        var bottomRank = new TextBlock
        {
            Text = card.RankLabel + card.Suit.Symbol(),
            Foreground = brush,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 5, 3),
            LayoutTransform = new RotateTransform(180),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(bottomRank, 2);

        grid.Children.Add(topRank);
        grid.Children.Add(bigSuit);
        grid.Children.Add(bottomRank);

        var bgColor = Color.FromRgb(0xfd, 0xfd, 0xf7);
        var borderColor = Color.FromRgb(0x99, 0x99, 0x99);
        if (dim)
        {
            bgColor = Darken(bgColor, DimFactor);
            borderColor = Darken(borderColor, DimFactor);
        }

        var border = new Border
        {
            Width = 64,
            Height = 90,
            Background = new SolidColorBrush(bgColor),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid,
            Effect = new DropShadowEffect { ShadowDepth = 2, BlurRadius = 4, Opacity = 0.4, Color = Colors.Black },
        };

        if (selected)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xd4, 0xaf, 0x37));
            border.BorderThickness = new Thickness(3);
        }

        return border;
    }
}
