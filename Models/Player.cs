namespace HeartsWpf.Models;

public class Player
{
    public string Name { get; }
    public bool IsHuman { get; }
    public List<Card> Hand { get; } = new();
    public List<Card> TricksWon { get; } = new();
    public int RoundScore { get; set; }
    public int TotalScore { get; set; }

    /// <summary>Points collected so far this round (live, updates as tricks are won).</summary>
    public int CurrentRoundPoints => TricksWon.Sum(c => c.Points);

    public Player(string name, bool isHuman)
    {
        Name = name;
        IsHuman = isHuman;
    }

    public void SortHand()
    {
        // Spades, Hearts, Clubs, Diamonds (alternating colors for readability)
        int Order(Suit s) => s switch
        {
            Suit.Spades => 0,
            Suit.Hearts => 1,
            Suit.Clubs => 2,
            Suit.Diamonds => 3,
            _ => 4
        };
        Hand.Sort((a, b) => Order(a.Suit) != Order(b.Suit)
            ? Order(a.Suit) - Order(b.Suit)
            : a.Rank - b.Rank);
    }
}
