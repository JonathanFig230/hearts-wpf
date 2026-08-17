using HeartsWpf.Models;

namespace HeartsWpf;

/// <summary>
/// Holds all Hearts game state and rules, and drives the turn-by-turn flow
/// (AI pacing, trick resolution, scoring). UI subscribes to <see cref="Changed"/>
/// and re-renders from the public state after every mutation, mirroring a
/// simple render-loop rather than full MVVM binding.
/// </summary>
public class GameEngine
{
    private static readonly string[] Names = { "You", "West", "North", "East" };
    private static readonly PassDirection[] PassCycle =
        { PassDirection.Left, PassDirection.Right, PassDirection.Across, PassDirection.None };

    private readonly Random _rng = new();
    private CancellationTokenSource _cts = new();

    /// <summary>
    /// Bumped every time a card is played or a trick resolves. A continuation
    /// that wakes up after an await checks this against the value it captured
    /// before waiting; a mismatch means some other path already advanced the
    /// trick/turn while it slept, so it backs off instead of double-processing
    /// (which would otherwise eventually ask an empty hand to play a card).
    /// </summary>
    private int _moveSeq;

    public IReadOnlyList<Player> Players { get; private set; } = Array.Empty<Player>();
    public int HandNumber { get; private set; }
    public PassDirection PassDirection { get; private set; } = PassDirection.Left;
    public bool HeartsBroken { get; private set; }
    public List<(int Player, Card Card)> CurrentTrick { get; } = new();
    public int Turn { get; private set; }
    public int TricksPlayed { get; private set; }
    public bool FirstTrick { get; private set; } = true;
    public GamePhase Phase { get; private set; } = GamePhase.Idle;
    public string StatusText { get; private set; } = "";

    public List<Card> SelectedPass { get; } = new();
    private readonly Dictionary<int, List<Card>> _aiPassSelections = new();
    public bool HumanPassConfirmed { get; private set; }

    /// <summary>Fired after every state mutation the UI should re-render for.</summary>
    public event Action? Changed;
    /// <summary>Fired once a hand's scoring is final. Argument is the shooter's index, or -1.</summary>
    public event Action<int>? HandCompleted;
    public event Action? GameCompleted;

    public void CancelGame()
    {
        _cts.Cancel();
    }

    public void NewGame()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();

        Players = Enumerable.Range(0, 4)
            .Select(i => new Player(Names[i], isHuman: i == 0))
            .ToList();
        HandNumber = 0;
        PassDirection = PassDirection.Left;

        StartHand();
    }

    private void StartHand()
    {
        var token = _cts.Token;
        var deck = BuildShuffledDeck();
        foreach (var p in Players)
        {
            p.Hand.Clear();
            p.TricksWon.Clear();
            p.RoundScore = 0;
        }
        for (int i = 0; i < 52; i++) Players[i % 4].Hand.Add(deck[i]);
        foreach (var p in Players) p.SortHand();

        PassDirection = PassCycle[HandNumber % PassCycle.Length];
        HeartsBroken = false;
        CurrentTrick.Clear();
        TricksPlayed = 0;
        FirstTrick = true;
        SelectedPass.Clear();
        _aiPassSelections.Clear();
        HumanPassConfirmed = false;

        if (PassDirection == PassDirection.None)
        {
            Changed?.Invoke();
            RunAsync(() => BeginPlayPhaseAsync(token));
        }
        else
        {
            Phase = GamePhase.Passing;
            for (int i = 1; i < 4; i++)
                _aiPassSelections[i] = ChooseAIPass(Players[i].Hand);
            StatusText = $"Select 3 cards to pass {PassDirection.ToString().ToLower()}.";
            Changed?.Invoke();
        }
    }

    private static List<Card> BuildShuffledDeck()
    {
        var deck = new List<Card>(52);
        foreach (Suit s in Enum.GetValues<Suit>())
            for (int r = 2; r <= 14; r++)
                deck.Add(new Card(s, r));

        var rng = new Random();
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }

    // ---------------- Passing ----------------

    public void ToggleHumanPassSelection(Card card)
    {
        if (Phase != GamePhase.Passing || HumanPassConfirmed) return;
        if (SelectedPass.Remove(card)) { }
        else
        {
            if (SelectedPass.Count >= 3) return;
            SelectedPass.Add(card);
        }
        StatusText = $"Select 3 cards to pass {PassDirection.ToString().ToLower()}. ({SelectedPass.Count}/3)";
        Changed?.Invoke();
    }

    public void ConfirmHumanPass()
    {
        if (Phase != GamePhase.Passing || SelectedPass.Count != 3) return;
        HumanPassConfirmed = true;
        StatusText = "Passing cards...";
        Changed?.Invoke();
        var token = _cts.Token;
        RunAsync(async () =>
        {
            await Task.Delay(400, token);
            DoPassing();
            await BeginPlayPhaseAsync(token);
        });
    }

    private static int PassTarget(int fromIndex, PassDirection direction) => direction switch
    {
        PassDirection.Left => (fromIndex + 1) % 4,
        PassDirection.Right => (fromIndex + 3) % 4,
        PassDirection.Across => (fromIndex + 2) % 4,
        _ => fromIndex
    };

    private List<Card> ChooseAIPass(List<Card> hand)
    {
        return hand.OrderByDescending(DangerScore).Take(3).ToList();
    }

    private static int DangerScore(Card c)
    {
        if (c.IsQueenOfSpades) return 200;
        if (c.Suit == Suit.Spades && c.Rank >= 13) return 90 + c.Rank;
        if (c.Suit == Suit.Hearts) return 40 + c.Rank;
        return c.Rank;
    }

    private void DoPassing()
    {
        var selections = new Dictionary<int, List<Card>> { [0] = new List<Card>(SelectedPass) };
        for (int i = 1; i < 4; i++) selections[i] = _aiPassSelections[i];

        var incoming = new Dictionary<int, List<Card>> { [0] = new(), [1] = new(), [2] = new(), [3] = new() };
        for (int i = 0; i < 4; i++)
        {
            int target = PassTarget(i, PassDirection);
            incoming[target].AddRange(selections[i]);
            Players[i].Hand.RemoveAll(c => selections[i].Contains(c));
        }
        for (int i = 0; i < 4; i++)
        {
            Players[i].Hand.AddRange(incoming[i]);
            Players[i].SortHand();
        }
    }

    // ---------------- Playing ----------------

    private async Task BeginPlayPhaseAsync(CancellationToken token)
    {
        Phase = GamePhase.Playing;
        int leader = 0;
        for (int i = 0; i < 4; i++)
        {
            if (Players[i].Hand.Any(c => c.IsTwoOfClubs)) { leader = i; break; }
        }
        Turn = leader;
        CurrentTrick.Clear();
        Changed?.Invoke();
        await ProceedTurnAsync(token);
    }

    private Suit? LedSuit => CurrentTrick.Count > 0 ? CurrentTrick[0].Card.Suit : null;

    public List<Card> ValidPlays(int playerIndex)
    {
        var hand = Players[playerIndex].Hand;
        if (CurrentTrick.Count == 0)
        {
            if (FirstTrick) return hand.Where(c => c.IsTwoOfClubs).ToList();
            if (!HeartsBroken)
            {
                var nonHearts = hand.Where(c => c.Suit != Suit.Hearts).ToList();
                if (nonHearts.Count > 0) return nonHearts;
            }
            return new List<Card>(hand);
        }
        else
        {
            var led = LedSuit!.Value;
            var followSuit = hand.Where(c => c.Suit == led).ToList();
            if (followSuit.Count > 0) return followSuit;
            if (FirstTrick)
            {
                var safe = hand.Where(c => c.Points == 0).ToList();
                if (safe.Count > 0) return safe;
            }
            return new List<Card>(hand);
        }
    }

    private async Task ProceedTurnAsync(CancellationToken token)
    {
        if (CurrentTrick.Count == 4) return;
        if (Players[Turn].Hand.Count == 0) return; // defensive: this turn was already handled elsewhere

        if (Players[Turn].IsHuman)
        {
            StatusText = "Your turn — pick a card to play.";
            Changed?.Invoke();
            return; // wait for UI to call PlayHumanCard
        }

        StatusText = $"{Players[Turn].Name} is thinking...";
        Changed?.Invoke();
        int seq = _moveSeq;
        await Task.Delay(550 + _rng.Next(400), token);
        if (seq != _moveSeq) return; // trick already advanced elsewhere while we waited

        var card = ChooseAICard(Turn);
        await PlayCardAsync(Turn, card, token);
    }

    /// <summary>Entry point for the human clicking a card in their hand.</summary>
    public void PlayHumanCard(Card card)
    {
        if (Phase != GamePhase.Playing || Turn != 0) return;
        var valid = ValidPlays(0);
        if (!valid.Contains(card)) return;
        var token = _cts.Token;
        RunAsync(() => PlayCardAsync(0, card, token));
    }

    private Card ChooseAICard(int playerIndex)
    {
        var valid = ValidPlays(playerIndex);

        if (CurrentTrick.Count == 0)
        {
            if (FirstTrick) return valid[0]; // forced 2C
            var nonHearts = valid.Where(c => c.Suit != Suit.Hearts).ToList();
            var pool = nonHearts.Count > 0 ? nonHearts : valid;
            return pool.OrderBy(c => c.Rank).First();
        }

        var led = LedSuit!.Value;
        var inSuit = valid.Where(c => c.Suit == led).ToList();
        if (inSuit.Count > 0)
        {
            var currentBest = TrickWinningCard();
            var losers = inSuit.Where(c => c.Rank < currentBest.Card.Rank).ToList();
            if (losers.Count > 0) return losers.OrderByDescending(c => c.Rank).First();
            return inSuit.OrderBy(c => c.Rank).First();
        }

        // void: discard most dangerous card
        var qs = valid.FirstOrDefault(c => c.IsQueenOfSpades);
        if (qs != null) return qs;
        var hearts = valid.Where(c => c.Suit == Suit.Hearts).ToList();
        if (hearts.Count > 0) return hearts.OrderByDescending(c => c.Rank).First();
        var highSpades = valid.Where(c => c.Suit == Suit.Spades && c.Rank >= 13).ToList();
        if (highSpades.Count > 0) return highSpades.OrderByDescending(c => c.Rank).First();
        return valid.OrderByDescending(c => c.Rank).First();
    }

    private (int Player, Card Card) TrickWinningCard()
    {
        var led = CurrentTrick[0].Card.Suit;
        var best = CurrentTrick[0];
        foreach (var entry in CurrentTrick)
            if (entry.Card.Suit == led && entry.Card.Rank > best.Card.Rank)
                best = entry;
        return best;
    }

    private async Task PlayCardAsync(int playerIndex, Card card, CancellationToken token)
    {
        var hand = Players[playerIndex].Hand;
        hand.RemoveAll(c => c.Equals(card));
        CurrentTrick.Add((playerIndex, card));
        if (card.Suit == Suit.Hearts) HeartsBroken = true;
        _moveSeq++;
        Changed?.Invoke();

        if (CurrentTrick.Count == 4)
        {
            int seq = _moveSeq;
            await Task.Delay(900, token);
            if (seq != _moveSeq) return;
            await ResolveTrickAsync(token);
        }
        else
        {
            Turn = (Turn + 1) % 4;
            int seq = _moveSeq;
            await Task.Delay(250, token);
            if (seq != _moveSeq) return;
            await ProceedTurnAsync(token);
        }
    }

    private async Task ResolveTrickAsync(CancellationToken token)
    {
        var winnerEntry = TrickWinningCard();
        int winner = winnerEntry.Player;
        Players[winner].TricksWon.AddRange(CurrentTrick.Select(e => e.Card));
        TricksPlayed++;
        FirstTrick = false;
        Turn = winner;
        CurrentTrick.Clear();
        _moveSeq++;
        Changed?.Invoke();

        if (TricksPlayed == 13)
        {
            int seq = _moveSeq;
            await Task.Delay(500, token);
            if (seq != _moveSeq) return;
            EndHand();
        }
        else
        {
            StatusText = $"{Players[winner].Name} won the trick.";
            Changed?.Invoke();
            int seq = _moveSeq;
            await Task.Delay(700, token);
            if (seq != _moveSeq) return;
            await ProceedTurnAsync(token);
        }
    }

    private void EndHand()
    {
        int shooter = -1;
        for (int i = 0; i < 4; i++)
        {
            int pts = Players[i].TricksWon.Sum(c => c.Points);
            Players[i].RoundScore = pts;
            if (pts == 26) shooter = i;
        }
        if (shooter != -1)
        {
            for (int i = 0; i < 4; i++)
                Players[i].RoundScore = i == shooter ? 0 : 26;
        }
        foreach (var p in Players) p.TotalScore += p.RoundScore;

        HandNumber++;
        Phase = GamePhase.Idle;
        Changed?.Invoke();
        HandCompleted?.Invoke(shooter);
    }

    /// <summary>Called by the UI after the round-results modal is dismissed.</summary>
    public void ContinueAfterRound()
    {
        if (Players.Any(p => p.TotalScore >= 100))
        {
            GameCompleted?.Invoke();
        }
        else
        {
            StartHand();
        }
    }

    private async void RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // game was exited or restarted; abandon this chain silently
        }
    }
}
