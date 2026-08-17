namespace HeartsWpf.Models;

public enum Suit
{
    Clubs,
    Diamonds,
    Spades,
    Hearts
}

public static class SuitExtensions
{
    public static string Symbol(this Suit suit) => suit switch
    {
        Suit.Clubs => "♣",
        Suit.Diamonds => "♦",
        Suit.Spades => "♠",
        Suit.Hearts => "♥",
        _ => "?"
    };

    public static bool IsRed(this Suit suit) => suit is Suit.Diamonds or Suit.Hearts;
}
