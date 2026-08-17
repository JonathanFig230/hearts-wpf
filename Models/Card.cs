namespace HeartsWpf.Models;

public class Card
{
    public Suit Suit { get; }
    public int Rank { get; } // 2..14, where 11=J, 12=Q, 13=K, 14=A

    public Card(Suit suit, int rank)
    {
        Suit = suit;
        Rank = rank;
    }

    public int Points =>
        Suit == Suit.Hearts ? 1 :
        Suit == Suit.Spades && Rank == 12 ? 13 :
        0;

    public bool IsQueenOfSpades => Suit == Suit.Spades && Rank == 12;
    public bool IsTwoOfClubs => Suit == Suit.Clubs && Rank == 2;

    public string RankLabel => Rank switch
    {
        11 => "J",
        12 => "Q",
        13 => "K",
        14 => "A",
        _ => Rank.ToString()
    };

    public override string ToString() => $"{RankLabel}{Suit.Symbol()}";

    public override bool Equals(object? obj) =>
        obj is Card c && c.Suit == Suit && c.Rank == Rank;

    public override int GetHashCode() => (Suit, Rank).GetHashCode();
}
